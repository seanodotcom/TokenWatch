using System.Text.Json;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

internal static class SettingsMigration
{
    internal static TokenWatch.Settings Load(string destination, bool demo)
    {
        TokenWatch.Settings.FilePath = destination;
        if (File.Exists(destination))
        {
            using var json = Read(destination);
            if (json is not null && json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Order", out _))
            {
                var converted = new TokenWatch.Settings(); ApplyPreview(converted, destination); return converted;
            }
            var loaded = TokenWatch.Settings.Load();
            if (demo && json is not null && json.RootElement.ValueKind == JsonValueKind.Object && !json.RootElement.TryGetProperty("ProviderOrder", out _)) loaded.ProviderOrder = ProviderCatalog.All.Select(p => p.Id).ToArray();
            return loaded;
        }
        if (demo) return new TokenWatch.Settings { ProviderOrder = ProviderCatalog.All.Select(p => p.Id).ToArray() };
        var location = Path.GetDirectoryName(destination)!;
        var releases = CandidateDirectories(location).ToArray();
        var directories = releases.Prepend(location).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        TokenWatch.Settings? settings = null;
        var liveIndex = int.MaxValue;
        // Real WPF settings carry executable overrides, cooldowns, pinning and geometry.
        for (var index = 0; index < directories.Length; index++)
        {
            var directory = directories[index];
            var source = Path.Combine(directory, "TokenWatch.settings.json");
            if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) || !File.Exists(source)) continue;
            try { settings = JsonSerializer.Deserialize<TokenWatch.Settings>(File.ReadAllText(source)); if (settings is not null) { liveIndex = index; break; } }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        settings ??= new TokenWatch.Settings();
        // The most recent preview preferences are the user's newer visibility/display choices.
        var previewIndex = Array.FindIndex(directories, d => File.Exists(Path.Combine(d, "TokenWatch.WinUI-preview.settings.json")));
        if (previewIndex >= 0 && previewIndex < liveIndex) ApplyPreview(settings, Path.Combine(directories[previewIndex], "TokenWatch.WinUI-preview.settings.json"));
        settings.ProviderOrder = ProviderCatalog.Normalize(settings.ProviderOrder);
        settings.RefreshMinutes = Math.Clamp(settings.RefreshMinutes, 1, 60);
        settings.DailyUsagePercent = WeeklyPace.NormalizeDailyPercent(settings.DailyUsagePercent);
        return settings;
    }
    internal static IEnumerable<string> CandidateDirectories(string current)
    {
        var parent = Directory.GetParent(current);
        if (parent is null || !parent.Exists) return [];
        return parent.EnumerateDirectories("TokenWatch-v*").Select(d => new { d.FullName, Version = Version.TryParse(d.Name.Replace("TokenWatch-v", ""), out var version) ? version : null })
            .Where(d => d.Version is not null).OrderByDescending(d => d.Version).Select(d => d.FullName);
    }
    private static void ApplyPreview(TokenWatch.Settings settings, string path)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<PreviewSession.SavedPreview>(File.ReadAllText(path));
            if (saved is null) return;
            settings.ProviderOrder = ProviderCatalog.Normalize(saved.Order ?? ProviderCatalog.All.Select(p => p.Id));
            settings.ClaudeEnabled = saved.Paused?.Contains("claude") != true; settings.CodexEnabled = saved.Paused?.Contains("codex") != true;
            settings.AntigravityEnabled = saved.Paused?.Contains("antigravity") != true; settings.CopilotEnabled = saved.Paused?.Contains("copilot") != true;
            settings.CursorEnabled = saved.Paused?.Contains("cursor") != true;
            settings.ShowRemaining = saved.ShowRemaining; settings.ShowResetDetails = saved.ShowReset; settings.AlwaysOnTop = saved.Topmost; settings.RefreshMinutes = Math.Clamp(saved.RefreshMinutes, 1, 60);
            settings.ShowPaceMarkers = saved.ShowPaceMarkers; settings.DailyUsagePercent = WeeklyPace.NormalizeDailyPercent(saved.DailyUsagePercent);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private static JsonDocument? Read(string path)
    { try { return JsonDocument.Parse(File.ReadAllText(path)); } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; } }
}
