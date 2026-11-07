using System.Globalization;
using System.Text.Json;

namespace TokenWatch.Core;

public enum ConnectionState { Loading, Connected, SignInRequired, NotRunning, Unavailable, Disabled, Stale, RateLimited }

public sealed record QuotaWindow(string Label, double? UsedPercent, DateTimeOffset? ResetsAt = null,
    string? Note = null, string? Key = null, string? Group = null)
{
    public string PercentText => UsedPercent is double value ? $"{value:0}%" : "—";
    public double? DisplayPercent(bool showRemaining) => UsedPercent is double value
        ? showRemaining ? 100 - Math.Clamp(value, 0, 100) : Math.Clamp(value, 0, 100) : null;
    public string DisplayText(bool showRemaining) => DisplayPercent(showRemaining) is double value ? $"{value:0}%" : "—";
    public double Fraction => Math.Clamp((UsedPercent ?? 0) / 100, 0, 1);
    public bool IsWarning => UsedPercent >= 80;
    public bool IsExhausted => UsedPercent >= 100;

    public string ResetSummaryText(DateTimeOffset now)
    {
        if (ResetsAt is not { } reset) return "—";
        if (reset <= now) return "Due";
        var local = reset.LocalDateTime;
        return local.Date == now.LocalDateTime.Date
            ? local.ToString("h:mm tt") : local.ToString("MMM d");
    }

    public string ResetDetailsText(DateTimeOffset now)
    {
        if (ResetsAt is not { } reset) return Note ?? "Reset time unavailable";
        if (reset <= now) return "Reset due · refreshing soon";
        var local = reset.LocalDateTime;
        return local.Date == now.LocalDateTime.Date
            ? $"Resets at {local:h:mm tt}" : $"Resets {local:ddd, MMM d 'at' h:mm tt}";
    }

    public string ResetText(DateTimeOffset now)
    {
        if (ResetsAt is not { } reset) return Note ?? "Reset time unavailable";
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return "Reset due · refreshing soon";
        if (remaining.TotalHours >= 24) return $"Resets {reset.LocalDateTime:ddd, h:mm tt}";
        if (remaining.TotalHours >= 1) return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"Resets in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
}

public sealed record UsageSnapshot(string Id, ConnectionState State, string Message,
    IReadOnlyList<QuotaWindow> Windows, DateTimeOffset? UpdatedAt = null, DateTimeOffset? RetryAt = null)
{
    public static UsageSnapshot Empty(string id, ConnectionState state, string message) => new(id, state, message,
        id is "copilot" or "cursor" ? [new(id == "cursor" ? "Included usage" : "Allowance", null, Note: "Usage unavailable")]
        : [new("5 hour", null, Note: "Usage unavailable"), new("Weekly", null, Note: "Usage unavailable")]);

    public UsageSnapshot PreserveLastGood(UsageSnapshot? previous) =>
        State != ConnectionState.Connected && previous?.UpdatedAt is not null &&
        previous.State is ConnectionState.Connected or ConnectionState.Stale
            ? this with { State = ConnectionState.Stale, Windows = previous.Windows, UpdatedAt = previous.UpdatedAt,
                Message = "Last known usage · " + Message }
            : this;
}

public static class UsageParser
{
    public static UsageSnapshot Claude(JsonElement root, DateTimeOffset now)
    {
        var windows = new List<QuotaWindow>();
        windows.Add(ClaudeWindow(root, "five_hour", "5 hour"));
        windows.Add(ClaudeWindow(root, "seven_day", "Weekly"));
        if (windows.All(w => w.UsedPercent is null))
            return UsageSnapshot.Empty("claude", ConnectionState.Unavailable, "Claude did not return usage windows.");
        return new("claude", ConnectionState.Connected, "Claude Code sign-in", windows, now);
    }

    private static QuotaWindow ClaudeWindow(JsonElement root, string key, string label)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object)
            return new(label, null, Note: "Not supplied by Claude");
        return new(label, Percentage(value, "utilization"), Date(value, "resets_at"));
    }

    public static UsageSnapshot Codex(JsonElement result, DateTimeOffset now)
    {
        JsonElement bucket = default;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
            buckets.TryGetProperty("codex", out bucket);
        if (bucket.ValueKind != JsonValueKind.Object) result.TryGetProperty("rateLimits", out bucket);
        if (bucket.ValueKind != JsonValueKind.Object)
            return UsageSnapshot.Empty("codex", ConnectionState.Unavailable, "No subscription limits returned. API-key plans do not have these quotas.");
        var supplied = new List<(int Minutes, QuotaWindow Window)>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) continue;
            var minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.TryGetInt32(out var n) ? n : 0;
            // Map by duration, never by primary/secondary order: weekly can be the primary bucket.
            var label = minutes switch { 300 => "5 hour", 10080 => "Weekly", > 0 when minutes % 60 == 0 => $"{minutes / 60} hour", > 0 => $"{minutes} min", _ => "Quota" };
            supplied.Add((minutes, new(label, Percentage(window, "usedPercent"), UnixDate(window, "resetsAt"))));
        }
        if (supplied.Count == 0 || supplied.All(w => w.Window.UsedPercent is null))
            return UsageSnapshot.Empty("codex", ConnectionState.Unavailable, "No metered quota windows returned.");
        var windows = new List<QuotaWindow>();
        windows.Add(supplied.FirstOrDefault(w => w.Minutes == 300).Window ?? new("5 hour", null, Note: "Not supplied for this plan"));
        windows.Add(supplied.FirstOrDefault(w => w.Minutes == 10080).Window ?? new("Weekly", null, Note: "Not supplied for this plan"));
        windows.AddRange(supplied.Where(w => w.Minutes is not (300 or 10080)).Select(w => w.Window));
        return new("codex", ConnectionState.Connected, "Codex account limits", windows, now);
    }

    public static UsageSnapshot Antigravity(JsonElement root, DateTimeOffset now)
    {
        var summary = AntigravitySummary(root, now);
        if (summary is not null) return summary;
        if (root.TryGetProperty("userStatus", out var status)) root = status;
        JsonElement models = default;
        if (root.TryGetProperty("cascadeModelConfigData", out var config)) config.TryGetProperty("clientModelConfigs", out models);
        if (models.ValueKind != JsonValueKind.Array) root.TryGetProperty("clientModelConfigs", out models);
        if (models.ValueKind != JsonValueKind.Array)
            return UsageSnapshot.Empty("antigravity", ConnectionState.Unavailable, "Antigravity returned no model quota data.");
        var windows = new List<QuotaWindow>();
        foreach (var model in models.EnumerateArray())
        {
            if (!model.TryGetProperty("quotaInfo", out var quota) || quota.ValueKind != JsonValueKind.Object) continue;
            if (!quota.TryGetProperty("remainingFraction", out var remaining) || !remaining.TryGetDouble(out var fraction) || !double.IsFinite(fraction)) continue;
            var label = String(model, "label") ?? String(model, "displayName") ?? "Model quota";
            var key = model.TryGetProperty("modelOrAlias", out var alias) && alias.ValueKind == JsonValueKind.Object
                ? String(alias, "model") ?? String(alias, "alias") : null;
            windows.Add(new("Model quota", Math.Clamp((1 - fraction) * 100, 0, 100), Date(quota, "resetTime"), Key: key ?? label, Group: label));
        }
        if (windows.Count == 0)
            return UsageSnapshot.Empty("antigravity", ConnectionState.Unavailable, "No model quotas are available for this Antigravity account.");
        return new("antigravity", ConnectionState.Connected, "Antigravity usage", windows, now);
    }

    private static UsageSnapshot? AntigravitySummary(JsonElement root, DateTimeOffset now)
    {
        if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object) root = response;
        else if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object) root = summary;
        if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return null;
        var windows = new List<QuotaWindow>();
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
            var groupName = String(group, "displayName") ?? String(group, "name") ?? "Quota";
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True) continue;
                var label = String(bucket, "displayName") ?? String(bucket, "name") ?? String(bucket, "bucketId") ?? "Quota";
                JsonElement fraction;
                double? remaining = null;
                if (bucket.TryGetProperty("remainingFraction", out fraction) && fraction.ValueKind == JsonValueKind.Number && fraction.TryGetDouble(out var n)) remaining = n;
                else if (bucket.TryGetProperty("remaining", out var wrapper) && wrapper.ValueKind == JsonValueKind.Object)
                {
                    if (wrapper.TryGetProperty("remainingFraction", out fraction) && fraction.ValueKind == JsonValueKind.Number && fraction.TryGetDouble(out n)) remaining = n;
                    else if (String(wrapper, "case") == "remainingFraction" && wrapper.TryGetProperty("value", out fraction) && fraction.ValueKind == JsonValueKind.Number && fraction.TryGetDouble(out n)) remaining = n;
                }
                windows.Add(new(label, remaining is double value && double.IsFinite(value) ? Math.Clamp((1 - value) * 100, 0, 100) : null,
                    Date(bucket, "resetTime"), "Not supplied by Antigravity", String(bucket, "bucketId") ?? label, groupName));
            }
        }
        return windows.Any(w => w.UsedPercent is not null) ? new("antigravity", ConnectionState.Connected,
            "Antigravity usage", windows, now) : null;
    }

    private static double? Percentage(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Clamp(n, 0, 100) : null;
    private static string? String(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement element, string key) =>
        DateTimeOffset.TryParse(String(element, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    private static DateTimeOffset? UnixDate(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || !value.TryGetInt64(out var seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { return null; }
    }
}
