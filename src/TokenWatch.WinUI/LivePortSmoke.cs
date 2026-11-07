using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

internal static class LivePortSmoke
{
    internal static async Task RunAsync(string reportPath, Action<string, bool> check)
    {
        var fixture = Path.ChangeExtension(reportPath, ".migration-fixture");
        var oldDirectory = Path.Combine(fixture, "dist", "TokenWatch-v0.3.0");
        var previewDirectory = Path.Combine(fixture, "dist", "TokenWatch-v0.4.1");
        var newDirectory = Path.Combine(fixture, "dist", "TokenWatch-v0.5.0");
        foreach (var directory in new[] { oldDirectory, previewDirectory, newDirectory }) Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(oldDirectory, "TokenWatch.settings.json");
        var previewPath = Path.Combine(previewDirectory, "TokenWatch.WinUI-preview.settings.json");
        var newPath = Path.Combine(newDirectory, "TokenWatch.settings.json");
        if (File.Exists(newPath)) File.Delete(newPath);
        var deadline = DateTimeOffset.UtcNow.AddHours(1);
        await File.WriteAllTextAsync(oldPath, JsonSerializer.Serialize(new { ProviderOrder = new[] { "claude", "codex", "antigravity" }, CodexExecutable = "C:\\fixture\\codex.exe", ClaudeRetryNotBefore = deadline, Pinned = false, Left = 123, Top = 234, WindowWidth = 330 }));
        await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(new { Order = new[] { "claude", "codex", "antigravity", "copilot", "cursor" }, Paused = new[] { "copilot", "cursor" }, ShowRemaining = true, ShowReset = true, Topmost = true, RefreshMinutes = 5 }));
        var oldHash = SHA256.HashData(await File.ReadAllBytesAsync(oldPath)); var previewHash = SHA256.HashData(await File.ReadAllBytesAsync(previewPath));
        var session = PreviewSession.Load(newPath, false);
        check("Migration preserves the live executable override and server cooldown", session.CodexExecutable == "C:\\fixture\\codex.exe" && session.Backend.ClaudeRetryNotBefore == deadline);
        check("Migration preserves widget pinning and position", !session.KeepOpen && session.Backend.Left == 123 && session.Backend.Top == 234 && session.Backend.WindowWidth == 330);
        check("Older settings gain enabled pace markers and a twenty-percent daily budget", session.ShowPaceMarkers && session.DailyUsagePercent == 20);
        check("Newer preview visibility and appearance choices are migrated", session.Providers.Count == 5 && session.Providers.Count(p => p.Enabled) == 3 && session.ShowRemaining && session.ShowReset && session.Topmost && session.RefreshMinutes == 5);
        check("Live startup contains no sample percentages", session.Providers.All(p => p.Quotas.All(q => q.UsedPercent is null) && p.Connection == ConnectionState.Loading));
        session.Save();
        check("Migrated preferences are written to the new canonical settings file", File.Exists(newPath) && PreviewSession.Load(newPath, false).Providers.Count(p => p.Enabled) == 3);
        var oldAfter = SHA256.HashData(await File.ReadAllBytesAsync(oldPath)); var previewAfter = SHA256.HashData(await File.ReadAllBytesAsync(previewPath));
        check("Migration leaves both source settings files unchanged", oldHash.SequenceEqual(oldAfter) && previewHash.SequenceEqual(previewAfter));
        session.Providers.First(p => p.Id == "cursor").Enabled = true; session.ShowRemaining = false;
        var futureDirectory = Path.Combine(fixture, "dist", "TokenWatch-v0.5.1"); Directory.CreateDirectory(futureDirectory);
        var futureSettings = PreviewSession.Load(Path.Combine(futureDirectory, "TokenWatch.settings.json"), false);
        check("Later live settings take priority over older preview preferences", futureSettings.Providers.First(p => p.Id == "cursor").Enabled && !futureSettings.ShowRemaining);
        session.Providers.First(p => p.Id == "cursor").Enabled = false; session.ShowRemaining = true;
        var providers = ProviderCatalog.All.Select(p => new FixtureProvider(p.Id)).ToArray();
        using (var monitor = new UsageMonitor(session, providers))
        {
            await monitor.RefreshAsync();
            check("Live polling requests enabled providers only", providers.Where(p => p.Id is "claude" or "codex" or "antigravity").All(p => p.Calls == 1) && providers.Where(p => p.Id is "copilot" or "cursor").All(p => p.Calls == 0));
            var claude = session.Providers.First(p => p.Id == "claude");
            check("Real service results reach the provider model", claude.Connection == ConnectionState.Connected && claude.Quotas[0].UsedPercent == 25 && claude.Snapshot.UpdatedAt is not null);
            App.HideWidget(true); var calls = providers.Sum(p => p.Calls);
            await monitor.RefreshAsync();
            check("Usage polling continues while the widget is hidden", !App.Widget.AppWindow.IsVisible && providers.Sum(p => p.Calls) == calls + 3);
            App.ShowWidget();
            var originalTime = claude.Snapshot.UpdatedAt;
            providers.First(p => p.Id == "claude").Failure = true;
            await monitor.CheckAsync("claude");
            check("Failed live refresh preserves the last good value and timestamp", claude.Connection == ConnectionState.Stale && claude.Quotas[0].UsedPercent == 25 && claude.Snapshot.UpdatedAt == originalTime);
            await Task.WhenAll(monitor.CheckAsync("codex"), monitor.CheckAsync("codex"));
            check("Polling and manual checks cannot overlap for one provider", providers.First(p => p.Id == "codex").MaxConcurrent == 1);
            check("Live monitor records a refresh deadline", monitor.LastAttempt is not null && monitor.NextRefresh > DateTimeOffset.UtcNow);
        }
        check("Canonical settings contain no credential or usage snapshots", !File.ReadAllText(newPath).Contains("accessToken", StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(newPath).Contains("UsedPercent", StringComparison.OrdinalIgnoreCase));
        App.Session.Save(); // Restore the shared adapter settings destination after fixture loads.
        check("Native tray host exists", App.Tray is not null && App.Tray.Handle != IntPtr.Zero);
        check("Windows Shell accepted the tray icon", App.Tray?.IsRegistered == true);
        check("Tray uses version-4 activation notifications", App.Tray?.UsesVersion4 == true);
        PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(App.Widget), 0x10, IntPtr.Zero, IntPtr.Zero);
        await Task.Delay(150);
        check("Window close hides to tray instead of exiting", !App.Widget.AppWindow.IsVisible && !App.IsExiting && App.Tray?.IsRegistered == true);
        App.ShowWidget();
        using (var menu = new TokenWatch.NativeTrayMenu(App.Session.KeepOpen, App.Session.Topmost, false))
        {
            check("Tray menu exposes Show, Settings and Exit through Windows", menu.Count == 8 && menu.Default == TokenWatch.TrayCommand.Show && menu.Label(TokenWatch.TrayCommand.Exit).Replace("&", "").Contains("Exit"));
            check("Sample mode disables live refresh in the tray", (menu.State(TokenWatch.TrayCommand.Refresh) & 3) != 0);
        }
        App.HideWidget(true); await Task.Delay(100);
        check("Hidden widget keeps its window and tray host alive", !App.Widget.AppWindow.IsVisible && App.Tray is not null && App.Tray.Handle != IntPtr.Zero && !App.IsExiting);
        if (App.Tray is { } tray)
        {
            var notification = tray.UsesVersion4 ? (1 << 16) | 0x401 : 0x202;
            PostMessage(tray.Handle, 0x8001, IntPtr.Zero, new IntPtr(notification));
            await Task.Delay(200);
        }
        check("Tray activation restores the existing widget", App.Widget.AppWindow.IsVisible && !App.IsExiting);
        var keepOpen = App.Session.KeepOpen;
        App.ExecuteTrayCommand(TokenWatch.TrayCommand.Pin);
        check("Keep-open menu action is separate from always-on-top", App.Session.KeepOpen != keepOpen);
        App.Session.KeepOpen = keepOpen;
        App.ExecuteTrayCommand(TokenWatch.TrayCommand.Show);
        App.Tray?.Dispose();
        check("Exit cleanup removes the icon from Windows Shell", App.Tray is not null && !App.Tray.IsRegistered && !App.Tray.HasShellRectangle);
    }
    private sealed class FixtureProvider(string id) : TokenWatch.IUsageProvider
    {
        public string Id { get; } = id;
        internal int Calls { get; private set; }
        internal int MaxConcurrent { get; private set; }
        private int _active;
        internal bool Failure { get; set; }
        public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            Calls++; _active++; MaxConcurrent = Math.Max(MaxConcurrent, _active);
            try
            {
                await Task.Delay(50, cancellationToken);
                return Failure ? UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Fixture offline response") : new(Id, ConnectionState.Connected, "Fixture service response", [new("5 hour", 25, DateTimeOffset.UtcNow.AddHours(1))], DateTimeOffset.UtcNow);
            }
            finally { _active--; }
        }
    }
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
