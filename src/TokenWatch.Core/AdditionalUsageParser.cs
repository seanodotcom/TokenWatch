using System.Globalization;
using System.Text.Json;

namespace TokenWatch.Core;

// Internal provider interfaces: missing, unlimited and malformed allowances stay unknown.
public static class AdditionalUsageParser
{
    public static UsageSnapshot Copilot(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object) return Missing("copilot");
        var reset = Timestamp(root, "quota_reset_date_utc") ?? Timestamp(root, "quota_reset_date") ?? Timestamp(root, "limited_user_reset_date");
        var windows = new List<QuotaWindow>();
        if (Object(root, "quota_snapshots") is { } snapshots)
        {
            foreach (var (key, name) in new[] { ("premium_interactions", "Premium requests"), ("chat", "Chat"), ("completions", "Completions") })
            {
                if (Object(snapshots, key) is not { } quota) continue;
                var unlimited = True(quota, "unlimited");
                if (!unlimited && Number(quota, "entitlement") == 0) continue;
                var credits = True(quota, "token_based_billing") || True(root, "token_based_billing");
                var label = key == "premium_interactions" && credits ? "AI credits" : name;
                double? used = unlimited ? null : Number(quota, "percent_remaining") is { } left && left >= 0 && left <= 100 ? 100 - left : null;
                if (!unlimited && used is null && Number(quota, "entitlement") is > 0 and var total && (Number(quota, "quota_remaining") ?? Number(quota, "remaining")) is >= 0 and var remaining)
                    used = Math.Clamp((total - remaining) / total * 100, 0, 100);
                windows.Add(new(label, used, Timestamp(quota, "quota_reset_at") ?? reset,
                    unlimited ? "Unlimited allowance" : used is null ? "Quota not supplied by this plan" : null, Key: key));
            }
        }
        if (windows.Count == 0 && Object(root, "monthly_quotas") is { } totals && Object(root, "limited_user_quotas") is { } remainingQuotas)
            foreach (var key in new[] { "chat", "completions" })
                if (Number(totals, key) is > 0 and var total && Number(remainingQuotas, key) is >= 0 and var remaining)
                    windows.Add(new(key == "chat" ? "Chat" : "Completions", Math.Clamp((total - remaining) / total * 100, 0, 100), reset, Key: key));
        return windows.Count == 0 ? Missing("copilot") : new("copilot", ConnectionState.Connected, "GitHub Copilot allowance", windows, now);
    }

    public static UsageSnapshot Cursor(JsonElement root, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || Object(root, "planUsage") is not { } plan) return Missing("cursor");
        var reset = Timestamp(root, "billingCycleEnd");
        var windows = new List<QuotaWindow>();
        double? used = null;
        // includedSpend excludes bonus credits. Do not substitute totalSpend or invent a plan limit.
        if (Number(plan, "limit") is > 0 and var limit && Number(plan, "includedSpend") is >= 0 and var spend)
            used = Math.Clamp(spend / limit * 100, 0, 100);
        else if (Number(plan, "totalPercentUsed") is >= 0 and <= 100 and var percent) used = percent;
        if (used is not null) windows.Add(new("Included usage", used, reset, Key: "included"));
        if (Object(root, "spendLimitUsage") is { } budget && Number(budget, "individualLimit") is > 0 and var cap && Number(budget, "individualUsed") is >= 0 and var spent)
            windows.Add(new("On-demand budget", Math.Clamp(spent / cap * 100, 0, 100), reset, "Share of your on-demand spending cap", "on-demand"));
        return windows.Count == 0 ? Missing("cursor") : new("cursor", ConnectionState.Connected, "Cursor billing-cycle allowance", windows, now);
    }

    private static UsageSnapshot Missing(string id) => UsageSnapshot.Empty(id, ConnectionState.Unavailable, "This account did not supply a supported quota. Open the provider usage page for details.");
    private static JsonElement? Object(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
    private static bool True(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static double? Number(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        double result;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out result) || value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return double.IsFinite(result) ? result : null;
        return null;
    }
    private static DateTimeOffset? Timestamp(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value)) return null;
        if (Number(root, key) is { } number)
        {
            if (number <= 0) return null;
            try { return number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)number) : DateTimeOffset.FromUnixTimeSeconds((long)number); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }
}
