using TokenWatch.Core;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Microsoft.UI.Text;
using System.ComponentModel;
using Microsoft.UI.Input;

namespace TokenWatch.WinUI;

public sealed partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _nextRefresh = DateTimeOffset.Now.AddMinutes(App.Session.RefreshMinutes);
    private bool _fitPending;
    private bool _sizing;
    private bool _loaded;
    private readonly WidgetSizing _resize;
    private readonly InputNonClientPointerSource _caption;
    private readonly PropertyChangedEventHandler _sessionHandler;
    private bool _updatePending;
    private readonly Dictionary<string, ProviderVisual> _visuals = [];
    private readonly List<ProviderVisual> _activeVisuals = [];
    private TextBlock? _emptyState;
    internal Grid Surface => Root;
    internal int QuotaCount { get; private set; }
    internal int ProviderCount => ProviderStack.Children.OfType<StackPanel>().Count();
    public WidgetWindow()
    {
        InitializeComponent();
        Title = App.Session.IsDemo ? "TokenWatch · Sample data" : "TokenWatch";
        VersionText.Text = TokenWatch.AppVersion.Display;
        BuildCaption.Text = App.Session.IsDemo ? "SAMPLE DATA" : "TOKENWATCH";
        ((OverlappedPresenter)AppWindow.Presenter).SetBorderAndTitleBar(true, false);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "TokenWatch.ico"));
        _resize = new(WinRT.Interop.WindowNative.GetWindowHandle(this), () => Root.XamlRoot?.RasterizationScale ?? 1,
            MeasureContentHeight, () =>
            {
                FitContent();
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (!App.IsExiting) SaveGeometry(); });
            }, App.Session.Backend.WidgetPreferredHeight);
        _caption = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        DragRegion.SizeChanged += (_, _) => UpdateCaptionRegion();
        Root.Loaded += async (_, _) =>
        {
            Ui.Place(this, (int)Math.Clamp(App.Session.Backend.WindowWidth ?? 380, 220, 560), 740, true);
            await Task.Delay(120); if (App.IsExiting) return; _loaded = true; Update(); _clock.Start();
            if (App.Session.Backend.Left is { } left && App.Session.Backend.Top is { } top)
            { var scale = Root.XamlRoot.RasterizationScale; AppWindow.Move(new Windows.Graphics.PointInt32((int)(left * scale), (int)(top * scale))); }
            App.Monitor.Start();
            UpdateCaptionRegion();
        };
        AppWindow.Closing += (_, args) => { if (!App.IsExiting) { args.Cancel = true; App.HideWidget(); } };
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated && !App.Session.KeepOpen && !App.ManagerOpen && !App.MenuOpen) App.HideWidget(); };
        Root.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.F5) { args.Handled = true; await App.Monitor.RefreshAsync(); }
            else if (args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; App.HideWidget(); }
        };
        Root.SizeChanged += (_, args) =>
        {
            UpdateResponsiveHeader(args.NewSize.Width);
            ApplyResponsiveLayout(args.NewSize.Width, args.NewSize.Height);
            UpdateCaptionRegion();
            // Height changes are the result of fitting; they must not request another fit.
            if (!_resize.IsSizing && Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > .5) FitContent();
        };
        _sessionHandler = (_, _) =>
        {
            if (App.IsExiting || _updatePending) return;
            _updatePending = true;
            if (!DispatcherQueue.TryEnqueue(() => { _updatePending = false; if (!App.IsExiting) Update(); })) _updatePending = false;
        };
        App.Session.PropertyChanged += _sessionHandler;
        _clock.Tick += (_, _) => { UpdateCountdown(); foreach (var provider in App.Session.Providers.Where(p => p.Enabled)) if (_visuals.TryGetValue(provider.Id, out var visual)) visual.Tick(provider); };
        Closed += (_, _) => PrepareShutdown();
        foreach (var minutes in new[] { 1, 2, 5, 10, 15, 30, 60 })
        {
            var item = new RadioMenuFlyoutItem { Text = minutes == 1 ? "Every minute" : $"Every {minutes} minutes", Tag = minutes, GroupName = "Interval" };
            item.Click += (_, _) => { App.Session.RefreshMinutes = minutes; _nextRefresh = DateTimeOffset.Now.AddMinutes(minutes); UpdateCountdown(); };
            IntervalFlyout.Items.Add(item);
        }
    }
    private void Update()
    {
        if (App.IsExiting) return;
        var state = App.Session;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = state.Topmost;
        PinButton.IsChecked = state.Topmost;
        ModeCaption.Text = state.IsDemo ? "Sample data · " + (state.ShowRemaining ? "remaining" : "used") : App.Tray?.IsRegistered != true ? "Tray unavailable · taskbar active" : App.Monitor.IsRefreshing ? "Refreshing…" : (state.ShowRemaining ? "Remaining quota" : "Used quota");
        var desired = new List<UIElement>(); QuotaCount = 0; _activeVisuals.Clear();
        foreach (var provider in state.Providers.Where(p => p.Enabled))
        {
            if (!_visuals.TryGetValue(provider.Id, out var visual)) { visual = new ProviderVisual(provider); _visuals.Add(provider.Id, visual); }
            visual.Update(provider, state.ShowRemaining, state.ShowReset, state.ShowPaceMarkers, state.DailyUsagePercent);
            _activeVisuals.Add(visual);
            desired.Add(visual.Section); QuotaCount += visual.QuotaCount;
        }
        if (desired.Count == 0)
        {
            _emptyState ??= new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0, 24, 0, 24) };
            _emptyState.Text = state.Providers.Count == 0 ? "Add a provider in Settings to start tracking usage." : "All providers are switched off. Turn one on in Settings to show it here.";
            desired.Add(_emptyState);
        }
        ProviderVisual.Reconcile(ProviderStack, desired);
        if (_nextRefresh > DateTimeOffset.Now.AddMinutes(state.RefreshMinutes)) _nextRefresh = DateTimeOffset.Now.AddMinutes(state.RefreshMinutes);
        UpdateCountdown(); ApplyResponsiveLayout(Root.ActualWidth, Root.ActualHeight); FitContent();
    }
    private void FitContent()
    {
        if (App.IsExiting || !_loaded || _fitPending || Root.XamlRoot is null || _sizing || _resize.IsSizing) return;
        _fitPending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _fitPending = false; if (App.IsExiting || Root.XamlRoot is null || _resize.IsSizing) return;
            _sizing = true;
            try
            {
                var scale = Root.XamlRoot.RasterizationScale;
                var current = AppWindow.ClientSize;
                var pixelWidth = Math.Clamp(current.Width, (int)Math.Ceiling(WidgetSizing.MinimumWidth * scale), (int)Math.Floor(WidgetSizing.MaximumWidth * scale));
                var width = pixelWidth / scale;
                UpdateResponsiveHeader(width);
                var height = (int)Math.Ceiling(_resize.HeightForWidth(width) * scale);
                if (current.Width != pixelWidth || Math.Abs(current.Height - height) > 1)
                    AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(pixelWidth, height));
            }
            finally { _sizing = false; }
        });
    }
    private double MeasureContentHeight(double width)
    {
        var available = Math.Max(1, width - Root.Padding.Left - Root.Padding.Right);
        var providers = _activeVisuals.Sum(visual => visual.MeasureHeight(available));
        if (_activeVisuals.Count == 0 && _emptyState is not null)
        { _emptyState.Measure(new Size(available, double.PositiveInfinity)); providers = _emptyState.DesiredSize.Height; }
        return Root.Padding.Top + Root.Padding.Bottom + Root.RowDefinitions[0].Height.Value
            + providers + Root.RowDefinitions[2].Height.Value + Root.RowSpacing * 2;
    }
    private void ApplyResponsiveLayout(double width, double height)
    {
        if (_activeVisuals.Count == 0 || width <= 0 || height <= 0) return;
        var contentWidth = Math.Max(1, width - Root.Padding.Left - Root.Padding.Right);
        var chrome = Root.Padding.Top + Root.Padding.Bottom + Root.RowDefinitions[0].Height.Value
            + Root.RowDefinitions[2].Height.Value + Root.RowSpacing * 2;
        var available = Math.Max(0, height - chrome);
        double Measure(double scale) => _activeVisuals.Sum(visual => visual.MeasureHeight(contentWidth, scale));
        var natural = Measure(1);
        // Spacing takes the first extra room. Text, logos and bars then grow gently,
        // with a width-aware cap so tall, narrow widgets keep useful label space.
        var cap = Math.Clamp((width - 220) / 500, 0, .3);
        var scale = 1 + Math.Clamp((available / Math.Max(1, natural) - 1 - .08) * .45, 0, cap);
        var measured = Measure(scale);
        if (measured > available && scale > 1)
        {
            scale = 1 + (scale - 1) * Math.Clamp((available - natural) / Math.Max(1, measured - natural), 0, 1);
            measured = Measure(scale);
            if (measured > available) { scale = 1; measured = natural; }
        }
        var extra = Math.Max(0, available - measured);
        var units = 2 * (_activeVisuals.Count + QuotaCount);
        var spacing = Math.Min(12, extra / units);
        var outer = Math.Max(0, extra - spacing * units) / (2 * _activeVisuals.Count);
        foreach (var visual in _activeVisuals) visual.ApplyLayout(scale, spacing, outer);
    }
    private void UpdateResponsiveHeader(double width)
    {
        FullTitle.Visibility = width >= 350 ? Visibility.Visible : Visibility.Collapsed;
        ShortTitle.Visibility = width < 350 ? Visibility.Visible : Visibility.Collapsed;
        VersionText.Visibility = width < 280 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateCaptionRegion()
    {
        if (App.IsExiting || Root.XamlRoot is null || DragRegion.ActualWidth <= 0) return;
        var scale = Root.XamlRoot.RasterizationScale;
        var bounds = DragRegion.TransformToVisual(Root).TransformBounds(new Rect(0, 0, DragRegion.ActualWidth, DragRegion.ActualHeight));
        _caption.SetRegionRects(NonClientRegionKind.Caption, [new Windows.Graphics.RectInt32(
            (int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale), (int)Math.Round(bounds.Height * scale))]);
    }
    private void UpdateCountdown()
    {
        if (App.IsExiting) return;
        if (DateTimeOffset.Now >= _nextRefresh) _nextRefresh = DateTimeOffset.Now.AddMinutes(App.Session.RefreshMinutes);
        var remaining = App.Session.IsDemo ? _nextRefresh - DateTimeOffset.Now : App.Monitor.NextRefresh - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var fraction = Math.Clamp(remaining.TotalMinutes / App.Session.RefreshMinutes, .001, .9999);
        var angle = fraction * Math.PI * 2;
        var figure = new PathFigure { StartPoint = new Point(14, 3) };
        figure.Segments.Add(new ArcSegment { Point = new Point(14 + 11 * Math.Sin(angle), 14 - 11 * Math.Cos(angle)), Size = new Size(11, 11), IsLargeArc = fraction > .5, SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure); CountdownArc.Data = geometry;
        ToolTipService.SetToolTip(CountdownButton, $"{(App.Session.IsDemo ? "Sample refresh" : App.Monitor.IsRefreshing ? "Refreshing usage" : "Next refresh")} in {(int)remaining.TotalMinutes}:{remaining.Seconds:00} · change interval");
        foreach (var item in IntervalFlyout.Items.OfType<RadioMenuFlyoutItem>()) item.IsChecked = (int)item.Tag == App.Session.RefreshMinutes;
    }
    private void Countdown_Click(object sender, RoutedEventArgs e) => UpdateCountdown();
    private void Pin_Click(object sender, RoutedEventArgs e) => App.Session.Topmost = PinButton.IsChecked == true;
    private void Settings_Click(object sender, RoutedEventArgs e) => App.ShowManager();
    private void Close_Click(object sender, RoutedEventArgs e) => App.HideWidget();
    internal void SaveGeometry()
    {
        if (!_loaded || Root.XamlRoot is null) return;
        var scale = Root.XamlRoot.RasterizationScale;
        App.Session.Backend.Left = AppWindow.Position.X / scale; App.Session.Backend.Top = AppWindow.Position.Y / scale;
        App.Session.Backend.WindowWidth = Root.ActualWidth; App.Session.Backend.WindowHeight = Root.ActualHeight;
        App.Session.Backend.WidgetPreferredHeight = _resize.PreferredHeight; App.Session.Save();
    }
    internal void PrepareShutdown() { _clock.Stop(); _loaded = false; _resize.Dispose(); App.Session.PropertyChanged -= _sessionHandler; }
}
