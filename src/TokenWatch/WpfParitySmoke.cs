using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TokenWatch.Core;

namespace TokenWatch;

internal static class WpfParitySmoke
{
    internal static async Task RunAsync(MainWindow window, string reportPath, Action<string, bool> check)
    {
        var list = (ItemsControl)window.FindName("ProviderList");
        var provider = list.Items.OfType<ProviderViewModel>().First(p => p.Id == "claude");
        var original = provider.Snapshot;
        var remaining = window.Preferences.ShowRemaining;
        var budget = window.Preferences.DailyUsagePercent;
        var markers = window.Preferences.ShowPaceMarkers;
        var fixture = new UsageSnapshot("claude", ConnectionState.Connected, "Sample data", [new("5 hour", 25, DateTimeOffset.UtcNow.AddHours(2)), new("Weekly", 50, DateTimeOffset.UtcNow.AddDays(2))], DateTimeOffset.UtcNow);
        provider.Set(fixture); window.Preferences.ShowPaceMarkers = true; window.Preferences.DailyUsagePercent = 20; window.SavePreferences();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var rows = provider.Rows.ToArray();
        var bars = Descendants<ProgressBar>(window).Where(b => b.DataContext is QuotaRow row && rows.Contains(row)).ToArray();
        var changes = 0;
        foreach (var bar in bars) bar.ValueChanged += (_, _) => changes++;
        provider.Set(fixture with { UpdatedAt = DateTimeOffset.UtcNow });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        check("Unchanged observations retain quota rows and native bar instances", rows.SequenceEqual(provider.Rows) && bars.SequenceEqual(Descendants<ProgressBar>(window).Where(b => b.DataContext is QuotaRow row && rows.Contains(row))) && changes == 0);
        provider.Set(fixture with { Windows = [fixture.Windows[0] with { UsedPercent = 26 }, fixture.Windows[1]] });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check("Changing one quota updates only its existing progress bar", rows.SequenceEqual(provider.Rows) && changes == 1);
        check("Only available weekly quotas receive pace markers", rows[0].PacePercent is null && rows[1].PacePercent is not null && rows[1].Description.Contains("White tick"));
        var position = rows[1].PacePercent!.Value;
        window.Preferences.ShowRemaining = !remaining; window.SavePreferences();
        check("Pace marker follows used versus remaining display", Math.Abs(rows[1].PacePercent!.Value + position - 100) < .001);
        window.Preferences.ShowRemaining = remaining; window.Preferences.DailyUsagePercent = 10; window.SavePreferences();
        var usedBefore = remaining ? 100 - position : position;
        var usedAfter = remaining ? 100 - rows[1].PacePercent!.Value : rows[1].PacePercent!.Value;
        check("Custom daily budget changes the expected usage marker", usedAfter <= usedBefore && Settings.Load().DailyUsagePercent == 10);
        window.Preferences.ShowPaceMarkers = false; window.SavePreferences();
        check("Hiding pace markers retains the budget and quota controls", rows[1].PacePercent is null && Settings.Load().DailyUsagePercent == 10 && rows.SequenceEqual(provider.Rows));
        window.Preferences.ShowPaceMarkers = true; window.SavePreferences();
        provider.Set(fixture with { State = ConnectionState.Stale });
        check("Stale readings hide pace markers and keep percentages", rows[1].PacePercent is null && rows[1].Value == (remaining ? 50 : 50));
        provider.Set(fixture with { Windows = [fixture.Windows[0], fixture.Windows[1] with { ResetsAt = DateTimeOffset.UtcNow.AddMinutes(-1) }] });
        check("Expired weekly reset hides its pace marker", rows[1].PacePercent is null);
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/copilot.png")).Stream)
            check("Copilot product icon is packaged as a resource", stream.Length > 0);
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cursor.png")).Stream)
            check("Cursor product icon is packaged as a resource", stream.Length > 0);
        var setup = new ClaudeSetupWindow { Owner = window };
        setup.Loaded += (_, _) =>
        {
            var labels = Descendants<Button>(setup).Select(b => b.Content?.ToString()).ToArray();
            check("Guided Claude setup offers installation guide and cancel", labels.Contains("Set up and sign in") && labels.Contains("Setup guide") && labels.Contains("Cancel"));
            MainWindow.CaptureWindow(setup, Path.ChangeExtension(reportPath, ".claude-setup.png"));
            setup.DialogResult = false;
        };
        setup.ShowDialog();
        check("Cancelling guided setup performs no installation", setup.Choice == ClaudeSetupChoice.Cancel);
        var disabled = window.Preferences.ClaudeEnabled;
        window.Preferences.ClaudeEnabled = false; window.SyncProviderOrder();
        check("Paused providers are hidden while their selection is retained", !list.Items.OfType<ProviderViewModel>().Any(p => p.Id == "claude") && window.Preferences.ProviderOrder.Contains("claude"));
        window.Preferences.ClaudeEnabled = disabled; window.SyncProviderOrder();
        check("Re-enabling a provider restores the existing provider model", list.Items.OfType<ProviderViewModel>().Contains(provider));
        window.SetTrayAvailable(false); window.HideWidget();
        check("Missing tray keeps a taskbar recovery path", window.ShowInTaskbar && window.WindowState == WindowState.Minimized);
        window.SetTrayAvailable(((App)Application.Current).HasRegisteredTrayIcon); window.ShowWidget();
        window.Preferences.ShowRemaining = remaining; window.Preferences.DailyUsagePercent = budget; window.Preferences.ShowPaceMarkers = markers;
        provider.Set(original); window.SavePreferences();
        await Task.Delay(100); window.Capture(Path.ChangeExtension(reportPath, ".parity.png"));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
