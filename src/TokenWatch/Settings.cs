using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TokenWatch.Core;

namespace TokenWatch;

internal sealed class Settings
{
    public bool Pinned { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ShowRemaining { get; set; } = true;
    public bool ShowResetDetails { get; set; } = true;
    public bool ShowPaceMarkers { get; set; } = true;
    public double DailyUsagePercent { get; set; } = 20;
    public int RefreshMinutes { get; set; } = 2;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WidgetPreferredHeight { get; set; }
    public string? CodexExecutable { get; set; }
    public bool ClaudeEnabled { get; set; } = true;
    public bool CodexEnabled { get; set; } = true;
    public bool AntigravityEnabled { get; set; } = true;
    public bool CopilotEnabled { get; set; } = true;
    public bool CursorEnabled { get; set; } = true;
    public string[] ProviderOrder { get; set; } = ProviderCatalog.DefaultOrder;
    public DateTimeOffset? CopilotRetryNotBefore { get; set; }
    public DateTimeOffset? CursorRetryNotBefore { get; set; }
    public DateTimeOffset? ClaudeRetryNotBefore { get; set; }

    public bool IsProviderEnabled(string id) => Array.IndexOf(ProviderOrder, id) >= 0 && (id switch
    { "claude" => ClaudeEnabled, "codex" => CodexEnabled, "antigravity" => AntigravityEnabled, "copilot" => CopilotEnabled, "cursor" => CursorEnabled, _ => false });

    public static string FilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "TokenWatch.settings.json");
    public static string? PersistenceError { get; private set; }
    private static string? _protectedPath;
    public Settings Normalize()
    {
        RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 60);
        DailyUsagePercent = WeeklyPace.NormalizeDailyPercent(DailyUsagePercent);
        ProviderOrder = ProviderCatalog.Normalize(ProviderOrder);
        if (Left is double x && !double.IsFinite(x)) Left = null;
        if (Top is double y && !double.IsFinite(y)) Top = null;
        if (WindowWidth is double width) WindowWidth = WidgetGeometry.ClampWidth(width);
        if (WindowHeight is double height) WindowHeight = WidgetGeometry.ClampHeight(height);
        if (WidgetPreferredHeight is double preferred && (!double.IsFinite(preferred) || preferred <= 0)) WidgetPreferredHeight = null;
        return this;
    }
    public static Settings Load(bool migrate = false)
    {
        try
        {
            _protectedPath = null; PersistenceError = null;
            var source = File.Exists(FilePath) ? FilePath : migrate ? FindPrevious() : null;
            if (source is null) return new();
            using var document = JsonDocument.Parse(File.ReadAllText(source));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Expected settings object.");
            return (JsonSerializer.Deserialize<Settings>(document.RootElement) ?? new()).Normalize();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            PersistenceError = "Saved settings could not be read. Defaults are in use.";
            if (File.Exists(FilePath))
                try { File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")); }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { _protectedPath = FilePath; }
            return new();
        }
    }

    private static string? FindPrevious()
    {
        var parent = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        if (parent is null || !parent.Exists) return null;
        return parent.EnumerateDirectories("TokenWatch-v*")
            .Select(d => new { Directory = d, Version = Version.TryParse(d.Name.Replace("TokenWatch-v", ""), out var v) ? v : null })
            .Where(d => d.Version is not null).OrderByDescending(d => d.Version)
            .Select(d => Path.Combine(d.Directory.FullName, "TokenWatch.settings.json")).FirstOrDefault(File.Exists);
    }

    public bool Save()
    {
        try
        {
            if (_protectedPath == FilePath) { PersistenceError = "Unreadable settings could not be backed up. Move TokenWatch to a writable folder."; return false; }
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            PersistenceError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { PersistenceError = "Settings could not be saved. Move TokenWatch to a writable folder."; return false; }
    }
}
