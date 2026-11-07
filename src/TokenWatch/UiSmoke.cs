using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TokenWatch.Core;

namespace TokenWatch;

// Exercises the real WPF controls in sample mode without injecting mouse/keyboard input.
// Requires an explicit test settings path and is never run during normal startup.
internal static class UiSmoke
{
    internal static async Task<bool> RunAsync(MainWindow window, string reportPath)
    {
        var checks = new List<object>();
        var failures = 0;
        void Check(string name, bool passed) { checks.Add(new { Name = name, Passed = passed }); if (!passed) failures++; }
        await Task.Delay(300);
        window.UpdateLayout();
        await TrayBrandingSmoke.RunAsync(window, Check);
        await ResizeRegression.RunAsync(window, reportPath, Check);
        Check("Widget renders within readable size limits", window.ActualWidth <= WidgetGeometry.MaximumWidth && window.ActualWidth >= WidgetGeometry.MinimumWidth);
        var versionLabel = (TextBlock)window.FindName("VersionLabel");
        var headerActions = (FrameworkElement)window.FindName("HeaderActions");
        Check("Header shows the compiled version", versionLabel.Text == "v" + typeof(App).Assembly.GetName().Version!.ToString(3));
        Check("Header version uses small secondary text", versionLabel.FontSize <= 11 && versionLabel.Foreground.ToString() == Application.Current.Resources["TextSecondary"].ToString());
        Check("Header version does not overlap action buttons", versionLabel.TranslatePoint(new Point(versionLabel.ActualWidth, 0), window).X < headerActions.TranslatePoint(new Point(0, 0), window).X);
        Check("Instruction and connection-count taglines are removed", window.FindName("Subtitle") is null && window.FindName("FooterHint") is null);
        var originalWidth = window.Width; var originalHeight = window.Height;
        var originalLeft = window.Left; var originalTop = window.Top;
        var resizeCorner = (Thumb)window.FindName("ResizeCorner");
        var realPointerReader = window.ReadResizePointer;
        var testPointer = new Point(1000, 1000);
        var dpi = VisualTreeHelper.GetDpi(window);
        window.ReadResizePointer = () => testPointer;
        resizeCorner.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        testPointer.Offset(-1000 * dpi.DpiScaleX, -1000 * dpi.DpiScaleY);
        resizeCorner.RaiseEvent(new DragDeltaEventArgs(-1000, -1000) { RoutedEvent = Thumb.DragDeltaEvent });
        resizeCorner.RaiseEvent(new DragCompletedEventArgs(-1000, -1000, false) { RoutedEvent = Thumb.DragCompletedEvent });
        window.UpdateLayout();
        Check("Resize reaches a substantially smaller readable layout", Math.Abs(window.ActualWidth - 220) < 1 && window.ActualHeight < originalHeight * .6 && window.IsMini);
        Check("Window dimensions persist after resizing", Settings.Load().WindowWidth == window.Width && Settings.Load().WindowHeight == window.Height);
        Check("Header controls fit at minimum width", versionLabel.TranslatePoint(new Point(versionLabel.ActualWidth, 0), window).X < headerActions.TranslatePoint(new Point(0, 0), window).X);
        var canvas = (FrameworkElement)window.FindName("WidgetCanvas");
        var canvasBounds = canvas.TransformToAncestor(window).TransformBounds(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
        var scale = canvasBounds.Width / canvas.ActualWidth;
        Check("Minimum layout reflows instead of shrinking text", Math.Abs(scale - 1) < .003 && Math.Abs(canvas.ActualWidth - 220) < 1);
        Check("Whole widget fits without scrollbars or clipping", !Descendants<ScrollViewer>(window).Any() && canvasBounds.Right <= window.ActualWidth + 1 && canvasBounds.Bottom <= window.ActualHeight + 1 && Descendants<ProgressBar>(window).Count() == 7);
        Check("Minimum usage values stay readable", Descendants<TextBlock>(window).Where(t => t.IsVisible && t.DataContext is QuotaRow && t.FontWeight == FontWeights.SemiBold && t.FontSize > 11).All(t => t.FontSize * scale >= 12));
        Check("Footer and refresh icon are removed", window.FindName("WidgetFooter") is null && window.FindName("RefreshButton") is null);
        window.Capture(Path.ChangeExtension(reportPath, ".minimum.png"));
        resizeCorner.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        testPointer.Offset(1000 * dpi.DpiScaleX, 1000 * dpi.DpiScaleY);
        resizeCorner.RaiseEvent(new DragDeltaEventArgs(1000, 1000) { RoutedEvent = Thumb.DragDeltaEvent });
        resizeCorner.RaiseEvent(new DragCompletedEventArgs(1000, 1000, false) { RoutedEvent = Thumb.DragCompletedEvent });
        window.UpdateLayout();
        Check("Resize reaches the current monitor limit", Math.Abs(window.Width - window.MaxWidth) < 1 && window.Width >= WidgetGeometry.MinimumWidth && window.Height <= 1001);
        var beforeCancel = new WidgetRectangle(window.Left, window.Top, window.Width, window.Height);
        resizeCorner.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        testPointer.Offset(-20 * dpi.DpiScaleX, -40 * dpi.DpiScaleY);
        resizeCorner.RaiseEvent(new DragDeltaEventArgs(-20, -40) { RoutedEvent = Thumb.DragDeltaEvent });
        resizeCorner.RaiseEvent(new DragCompletedEventArgs(-20, -40, true) { RoutedEvent = Thumb.DragCompletedEvent });
        Check("Cancelled resize restores original bounds", new WidgetRectangle(window.Left, window.Top, window.Width, window.Height) == beforeCancel);
        window.ReadResizePointer = realPointerReader;
        Check("All edges and corners have resize handles", Descendants<Thumb>(window).Count(t => Enum.TryParse<ResizeEdges>(t.Tag?.ToString(), out _)) == 8);
        Check("Resize corners have no visible grip drawing", !Descendants<System.Windows.Shapes.Path>(resizeCorner).Any());
        foreach (var width in new[] { 220, 240, 260, 261, 300, 340, 341, 360, 410, 480, 560 })
        {
            window.ApplyResize(ResizeEdges.Right, width - window.Width, 0);
            window.UpdateLayout();
            var bars = Descendants<ProgressBar>(window).ToArray();
            var bounds = canvas.TransformToAncestor(window).TransformBounds(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
            Check($"All content fits at width {width}", Math.Abs(window.Width - Math.Min(width, window.MaxWidth)) < 1 && bounds.Bottom <= window.ActualHeight + 1 && bounds.Right <= window.ActualWidth + 1 && bars.Length == 7 && bars.All(b => b.ActualWidth >= 24));
            Check($"Provider details remain clickable at width {width}", Descendants<Button>(window).Count(b => b.IsVisible && b.IsHitTestVisible && b.DataContext is ProviderViewModel) == 3);
            Check($"Header controls fit at width {width}", versionLabel.TranslatePoint(new Point(versionLabel.ActualWidth, 0), window).X < headerActions.TranslatePoint(new Point(0, 0), window).X);
            var captions = Descendants<TextBlock>(window).Where(t => Exposed(t) && t.DataContext is QuotaRow && t.FontSize == 10 && t.Text is "5 hour" or "Weekly").Count();
            Check($"Quota captions fade at width {width}", width <= 300 ? captions == 0 : width >= 410 ? captions == 7 : captions <= 7);
            if (width is 220 or 300 or 340 or 360 or 560) window.Capture(Path.ChangeExtension(reportPath, $".{width}px.png"));
        }
        window.Width = originalWidth; window.Height = originalHeight; window.Left = originalLeft; window.Top = originalTop;
        window.HideWidget(); window.ShowWidget(); window.UpdateLayout();
        window.ApplyResize(ResizeEdges.Right, WidgetGeometry.DefaultWidth - window.Width, 0);
        window.UpdateLayout();
        window.FitPreferredHeight(null); window.UpdateLayout();
        var expandedHeight = window.Height;
        var expandedWidth = window.Width;
        window.Preferences.ShowResetDetails = false;
        window.SavePreferences(); window.UpdateLayout();
        Check("Hiding reset captions trims height while preserving scale", window.Height < expandedHeight && window.Width == expandedWidth);
        window.Preferences.ShowResetDetails = true;
        window.SavePreferences(); window.UpdateLayout();
        Check("Showing captions restores fitted height without clipping", Math.Abs(window.Height - expandedHeight) < 1);
        Check("Provider logos load as resources", new[] { "claude", "codex", "antigravity" }.All(id =>
            Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{id}.png")) is not null));
        var providers = ((ItemsControl)window.FindName("ProviderList")).Items.OfType<ProviderViewModel>().ToArray();
        var initialInterval = window.Preferences.RefreshMinutes;
        var headerLogo = (FrameworkElement)window.FindName("HeaderLogo");
        var logoHit = VisualTreeHelper.HitTest(headerLogo, new Point(headerLogo.ActualWidth / 2, headerLogo.ActualHeight / 2));
        Check("Countdown logo center receives pointer input", logoHit is not null);
        window.PopulateCountdownMenu();
        var intervalMenu = (ContextMenu)window.FindName("CountdownMenu");
        intervalMenu.PlacementTarget = headerLogo; intervalMenu.IsOpen = true; await Task.Delay(100);
        intervalMenu.UpdateLayout();
        var menuLabels = Descendants<TextBlock>(intervalMenu).Where(text => text.Text.Contains("minute")).ToArray();
        Check("Countdown menu has readable light text on dark background", intervalMenu.Background.ToString() == "#FF283446" && menuLabels.Length > 0 && menuLabels.All(label => label.Foreground.ToString() == "#FFF1F4FA"));
        MainWindow.CaptureElement(intervalMenu, Path.ChangeExtension(reportPath, ".interval-menu.png"));
        intervalMenu.IsOpen = false;
        var fiveMinutes = intervalMenu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "5 minutes");
        fiveMinutes.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check("Countdown menu changes refresh interval", window.Preferences.RefreshMinutes == 5);
        window.Preferences.RefreshMinutes = initialInterval; window.SavePreferences();
        window.SetCountdownPulse(true);
        var countdownScale = (System.Windows.Media.ScaleTransform)window.FindName("CountdownScale");
        Check("Countdown pulse starts and stops", countdownScale.HasAnimatedProperties);
        window.SetCountdownPulse(false);
        Check("Countdown returns to normal size", !countdownScale.HasAnimatedProperties && countdownScale.ScaleX == 1 && countdownScale.ScaleY == 1);
        var antigravity = providers.Single(p => p.Id == "antigravity");
        Check("Gemini and Argon show together without selection", antigravity.Rows.Count == 3 && antigravity.Rows.Select(r => r.GroupHeading).Where(h => h is not null).SequenceEqual(new[] { "Gemini Models", "Gemini 4 Argon" }));
        Check("Antigravity metadata is hidden", antigravity.MessageVisibility == Visibility.Collapsed);
        var originalAntigravity = antigravity.Snapshot;
        var antigravityFixture = originalAntigravity with { Windows = originalAntigravity.Windows.Select(w => w with { Label = w.Key == "gemini-5h" ? "Gemini five-hour limit" : "Gemini weekly limit" }).ToArray() };
        antigravity.Set(antigravityFixture);
        var changedAntigravity = antigravityFixture with { Windows = antigravityFixture.Windows.Select((w, index) => index == 0 ? w with { UsedPercent = 84 } : w).ToArray() };
        antigravity.Set(changedAntigravity); window.UpdateLayout(); await Task.Delay(200);
        var activeAntigravity = antigravity.Rows[0];
        var antigravityBar = Descendants<ProgressBar>(window).First(bar => bar.DataContext == activeAntigravity);
        var antigravityShimmer = (Border)antigravityBar.Template.FindName("Shimmer", antigravityBar);
        Check("Visible Antigravity update highlights and shimmers with normalized labels", activeAntigravity.Active && !activeAntigravity.Highlight && antigravityShimmer.Opacity > 0 && (!SystemParameters.ClientAreaAnimation || antigravityShimmer.HasAnimatedProperties));
        Check("Shimmer is contained in the filled bar", ReferenceEquals(antigravityShimmer.Parent, antigravityBar.Template.FindName("PART_Indicator", antigravityBar)) && antigravityShimmer.ActualWidth > 0 && antigravityShimmer.ActualWidth < antigravityBar.ActualWidth);
        await Task.Delay(5200);
        Check("Antigravity shimmer continues after the initial highlight fades", activeAntigravity.Active && antigravityShimmer.Opacity > 0 && (!SystemParameters.ClientAreaAnimation || antigravityShimmer.HasAnimatedProperties));
        window.Capture(Path.ChangeExtension(reportPath, ".antigravity-active.png"));
        antigravity.Set(changedAntigravity);
        Check("Unchanged Antigravity refresh clears active state", !antigravity.Rows[0].Active);
        antigravity.Set(originalAntigravity);
        var claudePreview = providers.Single(p => p.Id == "claude");
        var sampleSnapshot = claudePreview.Snapshot;
        window.HideWidget();
        var changedSnapshot = sampleSnapshot with { Windows = sampleSnapshot.Windows.Select((quota, index) => index == 0 ? quota with { UsedPercent = 65 } : quota).ToArray() };
        claudePreview.Set(changedSnapshot);
        Check("Changed quota stays pending while hidden", claudePreview.Rows[0].Highlight && claudePreview.Rows[0].Active && !claudePreview.Rows[1].Active);
        window.ShowWidget(); window.UpdateLayout(); await Task.Delay(100);
        Check("Pending quota highlight starts when widget is shown", !claudePreview.Rows[0].Highlight && Descendants<Border>(window).Any(b => b.DataContext == claudePreview.Rows[0] && b.Opacity > .3));
        var changedBar = Descendants<ProgressBar>(window).First(b => b.DataContext == claudePreview.Rows[0]);
        var shimmer = (Border)changedBar.Template.FindName("Shimmer", changedBar);
        Check("Changed quota bar shimmers when animations are enabled", !SystemParameters.ClientAreaAnimation || (shimmer.HasAnimatedProperties && ((System.Windows.Media.TranslateTransform)((System.Windows.Media.LinearGradientBrush)shimmer.Background).RelativeTransform).HasAnimatedProperties));
        await Task.Delay(500);
        window.Capture(Path.ChangeExtension(reportPath, ".active.png"));
        claudePreview.Set(changedSnapshot with { State = ConnectionState.Stale });
        Check("Failed refresh retains active bar", claudePreview.Rows[0].Active);
        claudePreview.Set(changedSnapshot);
        Check("Next unchanged successful refresh clears active bar", !claudePreview.Rows[0].Active && !claudePreview.Rows[0].Highlight);
        claudePreview.Set(sampleSnapshot);
        claudePreview.Set(sampleSnapshot with { Message = "" });
        window.UpdateLayout();
        var liveButton = Descendants<Button>(window).Single(b => b.IsVisible && b.IsHitTestVisible && b.DataContext == claudePreview && b.Content is TextBlock t && t.Text == "Live");
        var liveTooltip = new ToolTip { Content = liveButton.ToolTip, PlacementTarget = liveButton, IsOpen = true };
        await Task.Delay(80);
        liveTooltip.UpdateLayout();
        Check("Live tooltip contains provider, status and action", liveTooltip.Content is string tip && tip.Contains("Claude · Live") && tip.Contains("Open usage page"));
        Check("Live tooltip renders readable text on dark background", liveTooltip.Background.ToString() == "#FF283446" && liveTooltip.ActualWidth > 80 && Descendants<TextBlock>(liveTooltip).Any(t => t.Text.Contains("Claude") && t.Foreground.ToString() == "#FFF1F4FA"));
        MainWindow.CaptureElement(liveTooltip, Path.ChangeExtension(reportPath, ".tooltip.png"));
        liveTooltip.IsOpen = false;
        claudePreview.Set(sampleSnapshot with { State = ConnectionState.Stale, Message = "Usage temporarily unavailable. Last known usage retained." });
        window.ApplyResize(ResizeEdges.Right, 220 - window.Width, 0);
        window.UpdateLayout();
        var compactClaude = Descendants<Button>(window).Single(b => b.IsVisible && b.IsHitTestVisible && b.DataContext == claudePreview);
        Check("Compact provider icon retains failure status and explanation", compactClaude.ToolTip is string errorTip && errorTip.Contains("Claude · Stale") && errorTip.Contains("Usage temporarily unavailable") && claudePreview.StatusBrush.ToString() == "#FFE9BB72");
        Check("Compact stale values and hidden quota labels remain accessible", claudePreview.Rows[0].Value == sampleSnapshot.Windows[0].DisplayPercent(window.Preferences.ShowRemaining) && claudePreview.Rows[0].Opacity == .5 && Descendants<Grid>(window).Count(g => g.IsVisible && g.DataContext is QuotaRow && g.ToolTip is string text && (text.StartsWith("5 hour:") || text.StartsWith("Weekly:"))) == 7);
        window.Capture(Path.ChangeExtension(reportPath, ".compact-stale.png"));
        claudePreview.Set(sampleSnapshot);
        window.ApplyResize(ResizeEdges.Right, WidgetGeometry.DefaultWidth - window.Width, 0);
        window.UpdateLayout();
        var fixturePath = Path.Combine(Path.GetDirectoryName(reportPath)!, "claude-request-fixture.json");
        var testClock = DateTimeOffset.Parse("2026-10-28T16:00:00Z");
        await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "test-only", expiresAt = testClock.AddDays(1).ToUnixTimeMilliseconds() } }));
        using var httpFixture = new UsageFixtureHandler();
        using var fixtureClient = new HttpClient(httpFixture);
        DateTimeOffset? savedRetry = null;
        var claude = new ClaudeProvider(fixtureClient, () => fixturePath, () => testClock, () => savedRetry, value => savedRetry = value);
        var rateLimited = await claude.FetchAsync(CancellationToken.None);
        Check("Claude records Retry-After from real HTTP response handling", rateLimited.State == ConnectionState.RateLimited && savedRetry == testClock.AddSeconds(2854));
        await claude.FetchAsync(CancellationToken.None);
        Check("Claude manual retries issue no HTTP request during cooldown", httpFixture.Requests == 1);
        var restartedClaude = new ClaudeProvider(fixtureClient, () => fixturePath, () => testClock, () => savedRetry);
        await restartedClaude.FetchAsync(CancellationToken.None);
        Check("Claude restart respects persisted server deadline", httpFixture.Requests == 1);
        testClock = testClock.AddSeconds(2855);
        var recovered = await claude.FetchAsync(CancellationToken.None);
        Check("Claude recovers automatically after server deadline", recovered.State == ConnectionState.Connected && httpFixture.Requests == 2 && recovered.Windows[0].UsedPercent == 12 && savedRetry is null);
        await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "test-only", expiresAt = testClock.AddMinutes(-1).ToUnixTimeMilliseconds() } }));
        var expired = await claude.FetchAsync(CancellationToken.None);
        Check("Expired Claude credentials cause no usage request", expired.State == ConnectionState.SignInRequired && httpFixture.Requests == 2);
        await ClaudeSessionSmoke.RunAsync(Check, Path.GetDirectoryName(reportPath)!);
        var data = new ProviderViewModel("codex", "Codex", "#80E6B4", () => { });
        data.Set(new("codex", ConnectionState.Connected, "Test fixture", [new("5 hour", 44), new("Weekly", 7)]));
        data.SetDisplayMode(true);
        Check("Remaining mode updates percentages and bars", data.Rows[0].Percent == "56%" && data.Rows[0].Value == 56 && data.Rows[1].Value == 93);
        Check("Remaining mode updates accessible description", data.Rows[0].Description.Contains("56% remaining"));
        data.SetDisplayMode(false);
        Check("Used mode restores percentages and bars", data.Rows[0].Percent == "44%" && data.Rows[0].Value == 44);
        data.SetResetDetails(false);
        Check("Reset captions can be hidden without changing usage", data.Rows.All(r => r.ResetVisibility == Visibility.Collapsed) && data.Rows[0].Value == 44);
        data.SetDisplayMode(true);
        Check("Usage-mode changes preserve reset-caption visibility", data.Rows.All(r => r.ResetVisibility == Visibility.Collapsed));
        data.Set(data.Snapshot);
        Check("Provider refresh preserves hidden reset captions", data.Rows.All(r => r.ResetVisibility == Visibility.Collapsed));
        data.SetResetDetails(true);
        Check("Reset captions can be shown again", data.Rows.All(r => r.ResetVisibility == Visibility.Visible));
        var lowRemaining = new QuotaRow(new("5 hour", 83), "#93A4FF", false, true);
        Check("Low remaining quota retains warning color", lowRemaining.Percent == "17%" && lowRemaining.PercentBrush.ToString() == "#FFFFCC67");
        var resetAt = DateTimeOffset.Now.AddHours(2);
        var timeRow = new QuotaRow(new("5 hour", 44, resetAt), "#80E6B4", false);
        Check("Reset is prominent and quota name remains the caption", timeRow.PrimaryText == (resetAt.LocalDateTime.Date == DateTime.Now.Date ? resetAt.LocalDateTime.ToString("h:mm tt") : resetAt.LocalDateTime.ToString("MMM d")) && timeRow.Label == "5 hour");
        var dateRow = new QuotaRow(new("Weekly", 7, resetAt.AddDays(2)), "#80E6B4", false);
        Check("Weekly date is prominent and full details stay available", dateRow.PrimaryText == resetAt.AddDays(2).LocalDateTime.ToString("MMM d") && dateRow.Label == "Weekly" && dateRow.Description.Contains(resetAt.AddDays(2).LocalDateTime.ToString("h:mm tt")));
        var hiddenTimeRow = new QuotaRow(new("5 hour", 44, resetAt), "#80E6B4", false, showResetDetails: false);
        Check("Disabling reset details restores the quota name as primary", hiddenTimeRow.PrimaryText == "5 hour" && hiddenTimeRow.ResetVisibility == Visibility.Collapsed);
        var pin = (Button)window.FindName("PinButton");
        var originallyPinned = window.Preferences.Pinned;
        var originalTopmost = window.Topmost;
        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("Header pin toggles always on top", window.Topmost != originalTopmost && window.Preferences.AlwaysOnTop == window.Topmost);
        Check("Header pin exposes its state", pin.ToolTip?.ToString()?.EndsWith(window.Topmost ? "on" : "off", StringComparison.Ordinal) == true);
        Check("Topmost remains independent of pin", window.Preferences.Pinned == originallyPinned);
        Check("Topmost setting persists", Settings.Load().AlwaysOnTop == window.Topmost);
        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("Header pin restores topmost", window.Topmost == originalTopmost);
        var beforeLeft = window.Left; var beforeTop = window.Top;
        window.Left -= 5; window.Top -= 5;
        window.HideWidget();
        Check("Close behavior hides window", !window.IsVisible);
        Check("Position is saved", Math.Abs(Settings.Load().Left!.Value - (beforeLeft - 5)) < 1 && Math.Abs(Settings.Load().Top!.Value - (beforeTop - 5)) < 1);
        window.ShowWidget();
        Check("Tray show behavior restores window", window.IsVisible);
        if (!window.Preferences.Pinned) window.TogglePin();
        window.HandleDeactivation();
        Check("Pinned widget survives focus loss", window.IsVisible);
        window.TogglePin();
        window.HandleDeactivation();
        Check("Unpinned widget hides on focus loss", !window.IsVisible);
        window.TogglePin();
        window.ShowWidget();
        var dialog = new SettingsWindow(window.Preferences) { Owner = window };
        var originalDisplayMode = window.Preferences.ShowRemaining;
        var originalResetDetails = window.Preferences.ShowResetDetails;
        dialog.Loaded += (_, _) =>
        {
            Check("Claude sign-in action is available", dialog.FindName("ClaudeSignInButton") is Button);
            Check("Reset-details checkbox loads saved preference", ((CheckBox)dialog.FindName("ResetDetailsOption")).IsChecked == window.Preferences.ShowResetDetails);
            dialog.UpdateLayout();
            MainWindow.CaptureWindow(dialog, Path.ChangeExtension(reportPath, ".settings.png"));
            var path = (TextBox)dialog.FindName("CodexPath");
            var error = (TextBlock)dialog.FindName("ErrorText");
            var save = (Button)dialog.FindName("SaveButton");
            path.Text = "C:\\missing-tokenwatch-test\\codex.exe";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Invalid executable is rejected inline", error.Text.Length > 0 && dialog.IsVisible);
            path.Text = "";
            dialog.Providers.First(p => p.Id == "claude").Enabled = false;
            ((RadioButton)dialog.FindName("ShowRemainingOption")).IsChecked = true;
            ((CheckBox)dialog.FindName("ResetDetailsOption")).IsChecked = false;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        Check("Settings Save closes successfully", dialog.ShowDialog() == true);
        Check("Settings provider preference is applied", !window.Preferences.ClaudeEnabled);
        Check("Settings applies remaining mode", window.Preferences.ShowRemaining);
        Check("Settings applies reset-caption preference", !window.Preferences.ShowResetDetails);
        window.Preferences.ClaudeEnabled = true;
        window.SavePreferences();
        Check("Display choice persists", Settings.Load().ShowRemaining);
        Check("Reset-caption preference persists", !Settings.Load().ShowResetDetails);
        Check("Reset-caption change applies to every provider immediately", providers.SelectMany(p => p.Rows).All(r => r.ResetVisibility == Visibility.Collapsed));
        Check("Quota tooltips identify remaining mode without a tagline", providers.SelectMany(p => p.Rows).All(r => r.Description.Contains("remaining")));
        var cancelDialog = new SettingsWindow(window.Preferences) { Owner = window };
        cancelDialog.Loaded += (_, _) =>
        {
            cancelDialog.Providers.First(p => p.Id == "claude").Enabled = false;
            ((RadioButton)cancelDialog.FindName("ShowUsedOption")).IsChecked = true;
            ((CheckBox)cancelDialog.FindName("ResetDetailsOption")).IsChecked = true;
            cancelDialog.DialogResult = false;
        };
        cancelDialog.ShowDialog();
        Check("Settings Cancel leaves preferences unchanged", window.Preferences.ClaudeEnabled);
        Check("Settings Cancel preserves display choice", window.Preferences.ShowRemaining);
        Check("Settings Cancel preserves reset-caption preference", !window.Preferences.ShowResetDetails);
        window.Preferences.ShowRemaining = originalDisplayMode;
        window.Preferences.ShowResetDetails = originalResetDetails;
        window.SavePreferences();
        Check("Preview is clearly labeled", ((FrameworkElement)window.FindName("HeaderLogo")).ToolTip?.ToString()?.Contains("Sample data") == true);
        window.Capture(Path.ChangeExtension(reportPath, ".png"));
        await ProviderManagementSmoke.RunAsync(window, reportPath, Check);
        await WpfParitySmoke.RunAsync(window, reportPath, Check);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { Passed = checks.Count - failures, Failed = failures, Checks = checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0;
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

    private static bool Exposed(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement visual && (!visual.IsVisible || visual.Opacity < .02)) return false;
        return element.ActualHeight > .1 && element.ActualWidth > .1;
    }

    private sealed class UsageFixtureHandler : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = new HttpResponseMessage(Requests == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (Requests == 1) response.Headers.TryAddWithoutValidation("Retry-After", "2854");
            else response.Content = new StringContent("{\"five_hour\":{\"utilization\":12},\"seven_day\":{\"utilization\":34}}");
            return Task.FromResult(response);
        }
    }
}
