using TokenWatch.Core;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TokenWatch.WinUI;

internal static class PreviewSmoke
{
    internal static async Task RunAsync(string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var checks = new List<object>(); var failed = 0;
        void Check(string name, bool passed) { checks.Add(new { Name = name, Passed = passed }); if (!passed) failed++; }
        try
        {
            await Task.Delay(900);
            var state = App.Session; var widget = App.Widget;
            Check("All five providers render in the native widget", widget.ProviderCount == 5);
            Check("All ten sample quota windows remain visible", widget.QuotaCount == 10 && Descendants<ProgressBar>(widget.Surface).Count() == 10);
            Check("Quota bars have visible geometry", Descendants<ProgressBar>(widget.Surface).All(b => b.ActualWidth >= 80 && b.ActualHeight >= 4));
            Check("Preview state uses a separate settings file", !Path.GetFileName(state.SettingsPath).Equals("TokenWatch.settings.json", StringComparison.OrdinalIgnoreCase));
            Check("Quota tooltips identify provider and remaining mode", Descendants<ProgressBar>(widget.Surface).All(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("remaining")));
            Check("Widget content stays inside its window", Fits(widget.Surface));
            var paceMarkers = Descendants<Border>(widget.Surface).Where(b => b.Name == "WeeklyPaceMarker" && b.Visibility == Visibility.Visible).ToArray();
            var pixelTolerance = 1 / widget.Surface.XamlRoot.RasterizationScale;
            Check("Only the four known weekly quotas receive pace markers", paceMarkers.Length == 4);
            Check("Pace ticks and carets align and fit within their quota rows", paceMarkers.All(b => b.Parent is Grid grid
                && grid.Children.OfType<Microsoft.UI.Xaml.Shapes.Polygon>().Single() is { Fill: not null } caret
                && caret.Visibility == Visibility.Visible && Math.Abs(b.ActualWidth - 2) <= pixelTolerance
                && caret.Margin.Top >= b.ActualHeight + 1
                && Math.Abs(b.Margin.Left + b.ActualWidth / 2 - caret.Margin.Left - caret.ActualWidth / 2) <= pixelTolerance
                && caret.Margin.Left >= 0 && caret.Margin.Left + caret.ActualWidth <= grid.ActualWidth + 1
                && caret.Margin.Top + caret.ActualHeight <= grid.ActualHeight));
            Check("Weekly tooltips explain the weekday budget", Descendants<ProgressBar>(widget.Surface).Count(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("White tick: expected by today's close")) == 4);
            var remainingPositions = paceMarkers.Select(b => (double)b.Tag).ToArray();
            state.ShowRemaining = false; await Task.Delay(120);
            Check("Pace positions invert with used display", paceMarkers.Select((b, i) => Math.Abs((double)b.Tag + remainingPositions[i] - 100) < .001).All(matches => matches));
            state.ShowRemaining = true; await Task.Delay(120);
            var paceProvider = state.Providers.First(p => p.Id == "claude");
            var paceSnapshot = paceProvider.Snapshot;
            paceProvider.SetSnapshot(paceSnapshot with { State = ConnectionState.Stale }); await Task.Delay(120);
            Check("Stale weekly readings hide their pace marker", Descendants<Border>(widget.Surface).Count(b => b.Name == "WeeklyPaceMarker" && b.Visibility == Visibility.Visible) == 3);
            paceProvider.SetSnapshot(paceSnapshot); await Task.Delay(120);
            var originalBars = Descendants<ProgressBar>(widget.Surface).ToArray();
            var loads = 0; var unloads = 0; var valueChanges = 0;
            RoutedEventHandler loaded = (_, _) => loads++;
            RoutedEventHandler unloaded = (_, _) => unloads++;
            var callbacks = originalBars.Select(bar =>
            {
                bar.Loaded += loaded; bar.Unloaded += unloaded;
                return bar.RegisterPropertyChangedCallback(ProgressBar.ValueProperty, (_, _) => valueChanges++);
            }).ToArray();
            for (var tick = 0; tick < 4; tick++) { state.NotifyUsage(); await Task.Delay(80); }
            Check("Repeated notifications retain the existing quota controls", originalBars.SequenceEqual(Descendants<ProgressBar>(widget.Surface)) && loads == 0 && unloads == 0 && valueChanges == 0);
            var observedProvider = state.Providers.First(p => p.Id == "claude");
            var originalSnapshot = observedProvider.Snapshot;
            observedProvider.SetSnapshot(originalSnapshot with { UpdatedAt = DateTimeOffset.UtcNow });
            await Task.Delay(180);
            Check("An unchanged provider observation does not restart quota bars", originalBars.SequenceEqual(Descendants<ProgressBar>(widget.Surface)) && loads == 0 && unloads == 0 && valueChanges == 0);
            observedProvider.SetSnapshot(originalSnapshot with { Windows = originalSnapshot.Windows.Select((quota, index) => index == 0 ? quota with { UsedPercent = quota.UsedPercent + 1 } : quota).ToArray() });
            await Task.Delay(180);
            Check("A changed quota updates only its existing bar", originalBars.SequenceEqual(Descendants<ProgressBar>(widget.Surface)) && loads == 0 && unloads == 0 && valueChanges == 1 && originalBars[0].Value == originalSnapshot.Windows[0].DisplayPercent(true) - 1);
            observedProvider.SetSnapshot(originalSnapshot);
            await Task.Delay(180); valueChanges = 0;
            await Task.Delay(5300);
            Check("Tray watchdog and clock ticks leave stable quota bars untouched", originalBars.SequenceEqual(Descendants<ProgressBar>(widget.Surface)) && loads == 0 && unloads == 0 && valueChanges == 0);
            for (var index = 0; index < originalBars.Length; index++)
            {
                originalBars[index].UnregisterPropertyChangedCallback(ProgressBar.ValueProperty, callbacks[index]);
                originalBars[index].Loaded -= loaded; originalBars[index].Unloaded -= unloaded;
            }
            await AdaptiveLayoutSmoke.RunAsync(widget, reportPath, Check);
            await ResizeSmoke.RunAsync(widget, Check);
            await CaptureAsync(widget.Surface, Path.ChangeExtension(reportPath, ".widget.png"));
            var manager = App.ShowManager(); await Task.Delay(600);
            manager.SelectProvider("copilot");
            Check("Providers page opens selected provider details", manager.SelectedId == "copilot" && manager.ShowsConnectionInfo);
            Check("Native provider toggles are present", Descendants<ToggleSwitch>(manager.Surface).Count() >= 5);
            var setupDialog = ManagerWindow.ClaudeSetupDialog(manager.Surface.XamlRoot);
            Check("New-user Claude setup offers setup, guide and cancel without a terminal command", setupDialog.PrimaryButtonText == "Set up and sign in" && setupDialog.SecondaryButtonText == "Setup guide" && setupDialog.CloseButtonText == "Cancel"
                && setupDialog.Content is TextBlock setupText && setupText.Text.Contains("existing Claude account"));
            var setupDisplay = setupDialog.ShowAsync();
            await Task.Delay(220);
            await CaptureAsync(setupDialog, Path.ChangeExtension(reportPath, ".claude-setup.png"));
            setupDialog.Hide();
            Check("New-user Claude setup renders and cancels without installing anything", await setupDisplay == ContentDialogResult.None);
            await CaptureAsync(manager.Surface, Path.ChangeExtension(reportPath, ".providers.png"));
            var copilot = state.Providers.First(p => p.Id == "copilot");
            manager.MoveSelected(-1); await Task.Delay(180);
            Check("Reordering updates widget and persisted order", state.Providers[2].Id == "copilot" && PreviewSession.Load(state.SettingsPath).Providers[2].Id == "copilot");
            manager.MoveSelected(1);
            copilot.Enabled = false; await Task.Delay(180);
            Check("Off hides Copilot immediately and persists its selection", !PreviewSession.Load(state.SettingsPath).Providers.First(p => p.Id == "copilot").Enabled && widget.ProviderCount == 4 && widget.QuotaCount == 9);
            var cursorToggle = state.Providers.First(p => p.Id == "cursor");
            cursorToggle.Enabled = false; await Task.Delay(200);
            Check("Off hides both new providers but keeps them in Settings", widget.ProviderCount == 3 && widget.QuotaCount == 7 && state.Providers.Count == 5 && !Descendants<TextBlock>(widget.Surface).Any(t => t.Text is "GitHub Copilot" or "Cursor"));
            await CaptureAsync(widget.Surface, Path.ChangeExtension(reportPath, ".disabled-providers.png"));
            cursorToggle.Enabled = true;
            copilot.Enabled = true;
            await Task.Delay(180);
            Check("Turning providers back on restores their widget sections", widget.ProviderCount == 5 && widget.QuotaCount == 10);
            copilot.Connection = ConnectionState.SignInRequired; await Task.Delay(200);
            Check("Sign-in-required state leaves the allowance unknown", Descendants<ProgressBar>(widget.Surface).Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("GitHub Copilot") && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("unavailable")));
            await manager.CheckSampleConnectionAsync(); await Task.Delay(180);
            Check("Reconnect action restores the sample reading", copilot.Connection == ConnectionState.Connected);
            copilot.Connection = ConnectionState.Stale; await Task.Delay(200);
            await CaptureAsync(widget.Surface, Path.ChangeExtension(reportPath, ".stale.png"));
            Check("Stale quota retains its previous sample value", Descendants<ProgressBar>(widget.Surface).Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("GitHub Copilot") && b.Value == 66));
            copilot.Connection = ConnectionState.RateLimited; await Task.Delay(180);
            await manager.CheckSampleConnectionAsync();
            Check("Manual sample check respects the cooldown state", copilot.Connection == ConnectionState.RateLimited);
            await CaptureAsync(manager.Surface, Path.ChangeExtension(reportPath, ".cooldown.png"));
            copilot.Connection = ConnectionState.Connected;
            state.ShowRemaining = false; await Task.Delay(180);
            Check("Used display updates quota descriptions and persists", Descendants<ProgressBar>(widget.Surface).All(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains(" used")) && !PreviewSession.Load(state.SettingsPath).ShowRemaining);
            state.ShowRemaining = true;
            state.Topmost = true; await Task.Delay(180);
            Check("Pin changes native window stacking", ((OverlappedPresenter)widget.AppWindow.Presenter).IsAlwaysOnTop);
            state.Topmost = false;
            manager.ShowAppearance(); await Task.Delay(180);
            var paceSwitch = Descendants<ToggleSwitch>(manager.Surface).Single(control => control.Name == "PaceMarkersSwitch");
            var dailyPicker = Descendants<NumberBox>(manager.Surface).Single(control => control.Name == "DailyUsagePicker");
            Check("Expected usage settings default to enabled and twenty percent", paceSwitch.IsOn && dailyPicker.Value == 20 && state.ShowPaceMarkers && state.DailyUsagePercent == 20);
            var settingsBars = Descendants<ProgressBar>(widget.Surface).ToArray();
            paceSwitch.IsOn = false; await Task.Delay(180);
            Check("Settings switch hides ticks and filled carets immediately", !Descendants<Border>(widget.Surface).Any(b => b.Name == "WeeklyPaceMarker" && b.Visibility == Visibility.Visible)
                && !Descendants<Microsoft.UI.Xaml.Shapes.Polygon>(widget.Surface).Any(b => b.Name == "WeeklyPaceCaret" && b.Visibility == Visibility.Visible)
                && !dailyPicker.IsEnabled && !PreviewSession.Load(state.SettingsPath).ShowPaceMarkers
                && !settingsBars.Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("White tick:")));
            paceSwitch.IsOn = true; await Task.Delay(180);
            var defaultPositions = Descendants<Border>(widget.Surface).Where(b => b.Name == "WeeklyPaceMarker" && b.Visibility == Visibility.Visible).Select(b => (double)b.Tag).ToArray();
            dailyPicker.Value = 10; await Task.Delay(180);
            var customPositions = Descendants<Border>(widget.Surface).Where(b => b.Name == "WeeklyPaceMarker" && b.Visibility == Visibility.Visible).Select(b => (double)b.Tag).ToArray();
            Check("Daily budget control changes marker positions and persists", customPositions.Length == 4 && customPositions.Select((position, i) => Math.Abs(position - (100 - (100 - defaultPositions[i]) / 2)) < .001).All(matches => matches)
                && PreviewSession.Load(state.SettingsPath).DailyUsagePercent == 10 && state.DailyUsagePercent == 10
                && settingsBars.Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("10% per full weekday")));
            paceSwitch.IsOn = false; await Task.Delay(120);
            paceSwitch.IsOn = true; await Task.Delay(180);
            Check("Toggling markers retains the configured budget and quota controls", dailyPicker.Value == 10 && dailyPicker.IsEnabled && state.DailyUsagePercent == 10
                && settingsBars.SequenceEqual(Descendants<ProgressBar>(widget.Surface)));
            await CaptureAsync(widget.Surface, Path.ChangeExtension(reportPath, ".custom-budget.png"));
            dailyPicker.Value = 20; await Task.Delay(180);
            await CaptureAsync(manager.Surface, Path.ChangeExtension(reportPath, ".appearance.png"));
            var scale = widget.Surface.XamlRoot.RasterizationScale;
            widget.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(240 * scale), (int)(700 * scale)));
            await Task.Delay(500);
            Check("Compact layout retains all quota captions", widget.QuotaCount == 10 && Descendants<TextBlock>(widget.Surface).Count(t => t.Text.Contains("Weekly") || t.Text.Contains("5 hour") || t.Text.Contains("5h")) >= 6);
            Check("Compact quota values stay readable", Descendants<TextBlock>(widget.Surface).Where(t => t.Text.EndsWith('%')).All(t => t.FontSize >= 12.5));
            Check("Long compact quota captions wrap instead of clipping", Descendants<TextBlock>(widget.Surface).Where(t => t.Text.EndsWith("· Weekly")).All(t => t.ActualHeight >= 24));
            Check("Compact content fits without widget scrollbars", Fits(widget.Surface) && !Descendants<ScrollViewer>(widget.Surface).Any());
            await CaptureAsync(widget.Surface, Path.ChangeExtension(reportPath, ".compact.png"));
            var cursor = state.Providers.First(p => p.Id == "cursor"); state.Providers.Remove(cursor); await Task.Delay(180);
            Check("Removing a provider updates the widget", widget.ProviderCount == 4 && widget.QuotaCount == 8);
            state.Add("cursor"); state.Add("cursor"); await Task.Delay(180);
            Check("Adding providers cannot create duplicates", widget.ProviderCount == 5 && state.Providers.Count(p => p.Id == "cursor") == 1);
            foreach (var provider in state.Providers) provider.Enabled = false;
            await Task.Delay(200);
            Check("Switching every provider off shows a useful empty state", widget.ProviderCount == 0 && widget.QuotaCount == 0 && Descendants<TextBlock>(widget.Surface).Any(t => t.Text.Contains("switched off")));
            foreach (var provider in state.Providers) provider.Enabled = true;
            Check("Native settings content has a usable size", manager.Surface.ActualWidth >= 900 && manager.Surface.ActualHeight >= 600);
            Check("Preview persistence has no credential or usage history fields", !File.ReadAllText(state.SettingsPath).Contains("token", StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(state.SettingsPath).Contains("quota", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) { failed++; checks.Add(new { Name = "Unexpected test exception", Passed = false, Error = error.ToString() }); }
        try { await LivePortSmoke.RunAsync(reportPath, Check); }
        catch (Exception error) { failed++; checks.Add(new { Name = "Live port checks exception", Passed = false, Error = error.ToString() }); }
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Passed = checks.Count - failed, Failed = failed, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failed == 0 ? 0 : 1;
        App.Shutdown();
    }
    private static bool Fits(FrameworkElement root)
    {
        var bars = Descendants<ProgressBar>(root).ToArray();
        return bars.All(bar => { var point = bar.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)); return point.X >= 0 && point.Y >= 0 && point.X + bar.ActualWidth <= root.ActualWidth + 1 && point.Y + bar.ActualHeight <= root.ActualHeight + 1; });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static async Task CaptureAsync(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync(); var bytes = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        var file = await StorageFile.GetFileFromPathAsync(CreateEmpty(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private static string CreateEmpty(string path) { File.WriteAllBytes(path, []); return path; }
}
