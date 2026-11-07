using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using TokenWatch.Core;

namespace TokenWatch;

public partial class MainWindow : Window
{
    private void QuotaHighlight_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border) ScheduleQuotaHighlight(border);
    }
    private void QuotaHighlight_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && (bool)e.NewValue) ScheduleQuotaHighlight(border);
    }
    private static void ScheduleQuotaHighlight(System.Windows.Controls.Border border) =>
        border.Dispatcher.BeginInvoke(() => ShowQuotaHighlight(border), DispatcherPriority.Loaded);
    private void QuotaHighlight_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border) ScheduleQuotaHighlight(border);
    }
    private static System.Windows.Controls.ProgressBar? FindQuotaBar(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.ProgressBar bar) return bar;
            if (FindQuotaBar(child) is { } nested) return nested;
        }
        return null;
    }
    private static void ShowQuotaHighlight(System.Windows.Controls.Border border)
    {
        if (!border.IsVisible || border.DataContext is not QuotaRow row) return;
        if (border.Parent is System.Windows.Controls.Grid rowGrid && FindQuotaBar(rowGrid) is { } bar)
        {
            bar.ApplyTemplate();
            if (bar.Template.FindName("Shimmer", bar) is System.Windows.Controls.Border shimmer && shimmer.Background is LinearGradientBrush original)
            {
                var brush = original.Clone();
                shimmer.Background = brush;
                var sweep = (TranslateTransform)brush.RelativeTransform;
                shimmer.BeginAnimation(OpacityProperty, null); sweep.BeginAnimation(TranslateTransform.XProperty, null);
                shimmer.Opacity = row.Active ? .85 : 0;
                if (row.Active && SystemParameters.ClientAreaAnimation)
                {
                    shimmer.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromSeconds(.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    sweep.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                }
                else sweep.X = 0;
            }
        }
        if (!row.Highlight) return;
        row.ClearHighlight();
        if (!SystemParameters.ClientAreaAnimation)
        {
            border.Opacity = .42;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); border.Opacity = 0; };
            timer.Start();
            return;
        }
        border.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.42, To = 0, BeginTime = TimeSpan.FromSeconds(1), Duration = TimeSpan.FromSeconds(4),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        border.Opacity = .42;
    }
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMiniProperty = DependencyProperty.Register(nameof(IsMini), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); private set => SetValue(IsCompactProperty, value); }
    public bool IsMini { get => (bool)GetValue(IsMiniProperty); private set => SetValue(IsMiniProperty, value); }
    private static readonly DependencyProperty IdentityRestProgressProperty = DependencyProperty.Register(nameof(IdentityRestProgress), typeof(double), typeof(MainWindow),
        new PropertyMetadata(1d, static (owner, _) => ((MainWindow)owner).UpdateProviderIdentityOpacity()));
    private double IdentityRestProgress => (double)GetValue(IdentityRestProgressProperty);
    internal Settings Preferences { get; }
    internal bool Demo { get; }
    private readonly ObservableCollection<ProviderViewModel> _providers;
    private readonly ObservableCollection<ProviderViewModel> _displayProviders = [];
    private readonly IUsageProvider[] _adapters;
    private readonly System.Collections.Generic.Dictionary<string, SemaphoreSlim> _providerChecks = ProviderCatalog.All.ToDictionary(p => p.Id, _ => new SemaphoreSlim(1, 1));
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _resizeIdle = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private bool _refreshing;
    private bool _refreshAfterCurrent;
    private bool _ready;
    private bool _exiting;
    private bool _trayAvailable;
    private bool _settingsOpen;
    private bool _countdownMenuOpen;
    private bool _countdownPulsing;
    private bool _resizing;
    private bool _identityMoving;
    private double _providerProgress;
    private double _titleProgress;
    private double _titleSlotWidth;
    private double _fullTitleWidth;
    private bool _fittingContent;
    private double _baseContentHeight;
    private bool _contentSizeDirty = true;
    private DispatcherOperation? _pendingContentFit;
    private WidgetRectangle _resizeOrigin;
    private double? _preferredHeight;
    private double? _resizePreferredOrigin;
    private double _layoutScale = 1;
    private ResizeEdges _resizeEdges;
    private Point _resizePointerOrigin;
    private Point _resizePointer;
    private Point? _lastResizePointer;
    private Matrix _resizeDeviceToLogical;
    private DispatcherOperation? _pendingResize;
    internal Func<Point> ReadResizePointer { get; set; } = ReadCursorPosition;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    internal event Action? PreferencesChanged;

    internal MainWindow(Settings settings, bool demo)
    {
        Preferences = settings; Demo = demo;
        _preferredHeight = settings.WidgetPreferredHeight;
        InitializeComponent();
        VersionLabel.Text = AppVersion.Display;
        Width = WidgetGeometry.ClampWidth(settings.WindowWidth ?? WidgetGeometry.DefaultWidth);
        // Restore scale from width; old independently saved heights must not add padding or crop content.
        // Preview windows remain discoverable by desktop UI testing tools. The real widget is tray-only.
        ShowInTaskbar = demo;
        _providers = new(ProviderCatalog.All.Select(p => new ProviderViewModel(p.Id, p.Name, p.Color,
            () => { if (p.UsageUrl.Length > 0) OpenProviderDetails(p.Id, p.UsageUrl); else OpenSettings(p.Id); })));
        _adapters = [new ClaudeProvider(readRetry: () => settings.ClaudeRetryNotBefore,
                saveRetry: value => { settings.ClaudeRetryNotBefore = value; settings.Save(); }),
            new CodexProvider(() => Preferences.CodexExecutable), new AntigravityProvider(), new AdditionalProvider("copilot", settings), new AdditionalProvider("cursor", settings)];
        ProviderList.DataContext = _displayProviders;
        SyncProviderOrder();
        foreach (var provider in _providers)
        {
            provider.Rows.CollectionChanged += (_, _) => RequestContentFit();
            provider.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProviderViewModel.Message)) RequestContentFit(); };
        }
        foreach (var provider in _providers) provider.SetDisplayMode(settings.ShowRemaining);
        foreach (var provider in _providers) provider.SetResetDetails(settings.ShowResetDetails);
        foreach (var provider in _providers) provider.SetPace(settings.ShowPaceMarkers, settings.DailyUsagePercent);
        Topmost = settings.AlwaysOnTop;
        _ready = true;
        ApplyPinAppearance();
        _resizeIdle.Tick += (_, _) => { _resizeIdle.Stop(); AnimateIdentityRest(true); };
        Loaded += async (_, _) =>
        {
            PlaceWindow();
            _clock.Start();
            if (Demo) LoadSampleData(); else await RefreshAsync();
        };
        _clock.Tick += async (_, _) =>
        {
            foreach (var row in _providers.SelectMany(p => p.Rows)) row.Tick();
            UpdateFooter();
            if (!Demo && DateTimeOffset.UtcNow - _lastAttempt >= TimeSpan.FromMinutes(Preferences.RefreshMinutes)) await RefreshAsync();
            UpdateCountdown();
        };
        Closing += (_, e) => { if (!_exiting) { e.Cancel = true; HideWidget(); } };
    }

    internal async Task RefreshAsync()
    {
        if (Demo || _exiting) return;
        if (_refreshing) { _refreshAfterCurrent = true; return; }
        _refreshing = true;
        _lastAttempt = DateTimeOffset.UtcNow;
        UpdateCountdown();
        try
        {
            await Task.WhenAll(_adapters.Select(async adapter =>
            {
                var target = _providers.First(p => p.Id == adapter.Id);
                if (!IsProviderEnabled(adapter.Id))
                { target.Set(UsageSnapshot.Empty(adapter.Id, ConnectionState.Disabled, "Paused in Settings.")); return; }
                UsageSnapshot snapshot;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try { snapshot = await CheckProviderAsync(adapter.Id, timeout.Token); }
                catch (OperationCanceledException) { snapshot = UsageSnapshot.Empty(adapter.Id, ConnectionState.Unavailable, "Usage check timed out. Retrying on the next refresh."); }
                catch (Exception) { snapshot = UsageSnapshot.Empty(adapter.Id, ConnectionState.Unavailable, "Usage is unavailable. Check provider sign-in and retry."); }
                if (!_exiting)
                {
                    // Provider may have been paused in Settings while its request was in flight.
                    target.Set(IsProviderEnabled(adapter.Id) ? snapshot.PreserveLastGood(target.Snapshot) : UsageSnapshot.Empty(adapter.Id, ConnectionState.Disabled, "Paused in Settings."));
                    UpdateFooter();
                }
            }));
        }
        finally
        {
            _refreshing = false; UpdateFooter(); UpdateCountdown();
            if (_refreshAfterCurrent) { _refreshAfterCurrent = false; _ = RefreshAsync(); }
        }
    }

    private void UpdateCountdown()
    {
        if (_exiting) return;
        var interval = TimeSpan.FromMinutes(Preferences.RefreshMinutes);
        var remaining = _lastAttempt == DateTimeOffset.MinValue ? interval : _lastAttempt + interval - DateTimeOffset.UtcNow;
        var fraction = Demo ? 1 : Math.Clamp(remaining.TotalSeconds / interval.TotalSeconds, 0, 1);
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        var description = Demo ? "Sample data · automatic refresh is off\nRight-click to change interval" : (_refreshing ? "Refreshing usage…" : $"Next update in {seconds / 60}:{seconds % 60:00}") + "\nRight-click to change interval";
        HeaderLogo.ToolTip = description;
        System.Windows.Automation.AutomationProperties.SetName(HeaderLogo, description);

        CountdownArc.Fraction = fraction;
        SetCountdownPulse(!Demo && !_refreshing && remaining.TotalSeconds > 0 && remaining.TotalSeconds <= 5 && SystemParameters.ClientAreaAnimation);
    }

    internal void SetCountdownPulse(bool enabled)
    {
        if (_countdownPulsing == enabled) return;
        _countdownPulsing = enabled;
        var pulse = enabled ? new DoubleAnimation(1, 1.35, TimeSpan.FromMilliseconds(400)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever } : null;
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        CountdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void CountdownMenu_Opening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        _countdownMenuOpen = true;
        PopulateCountdownMenu();
    }
    internal void PopulateCountdownMenu()
    {
        CountdownMenu.Items.Clear();
        var intervals = new[] { 1, 2, 5, 10, 15, 30, 60 }.Append(Preferences.RefreshMinutes).Distinct().OrderBy(value => value);
        foreach (var minutes in intervals)
        {
            var item = new System.Windows.Controls.MenuItem { Header = $"{minutes} {(minutes == 1 ? "minute" : "minutes")}", IsCheckable = true, IsChecked = minutes == Preferences.RefreshMinutes };
            item.Click += (_, _) => { Preferences.RefreshMinutes = minutes; SavePreferences(); };
            CountdownMenu.Items.Add(item);
        }
    }
    private void CountdownMenu_Closed(object sender, RoutedEventArgs e) => _countdownMenuOpen = false;

    private bool IsProviderEnabled(string id) => Preferences.IsProviderEnabled(id);

    private async Task<UsageSnapshot> CheckProviderAsync(string id, CancellationToken cancellationToken)
    {
        var gate = _providerChecks[id];
        await gate.WaitAsync(cancellationToken);
        try { return await _adapters.First(p => p.Id == id).FetchAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    internal void SyncProviderOrder()
    {
        var desired = ProviderCatalog.Normalize(Preferences.ProviderOrder).Where(IsProviderEnabled).Select(id => _providers.First(p => p.Id == id)).ToArray();
        for (var i = _displayProviders.Count - 1; i >= 0; i--) if (!desired.Contains(_displayProviders[i])) _displayProviders.RemoveAt(i);
        for (var i = 0; i < desired.Length; i++) { var old = _displayProviders.IndexOf(desired[i]); if (old < 0) _displayProviders.Insert(i, desired[i]); else if (old != i) _displayProviders.Move(old, i); }
        EmptyWidgetText.Text = Preferences.ProviderOrder.Length == 0 ? "Add a provider in Settings to start tracking usage." : "All providers are switched off. Turn one on in Settings.";
        EmptyWidgetText.Visibility = _displayProviders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RequestContentFit();
    }

    private void UpdateFooter()
    {
        if (_exiting) return;
        if (SettingsError.Text != (Settings.PersistenceError ?? "")) RequestContentFit();
        SettingsError.Text = Settings.PersistenceError ?? "";
        SettingsError.Visibility = Settings.PersistenceError is null ? Visibility.Collapsed : Visibility.Visible;

    }

    internal void ShowWidget()
    {
        if (_exiting) return;
        Show();
        WindowState = WindowState.Normal;
        EnsureOnScreen();
        Activate();
        if (!Demo && DateTimeOffset.UtcNow - _lastAttempt > TimeSpan.FromMinutes(Preferences.RefreshMinutes)) _ = RefreshAsync();
    }

    internal void HideWidget() { SavePosition(); if (_trayAvailable) Hide(); else { ShowInTaskbar = true; WindowState = WindowState.Minimized; } }
    internal void SetTrayAvailable(bool available)
    {
        _trayAvailable = available;
        ShowInTaskbar = Demo || !available;
    }
    internal void TogglePin() { Preferences.Pinned = !Preferences.Pinned; SavePreferences(); }
    internal void ToggleTopmost() { Preferences.AlwaysOnTop = !Preferences.AlwaysOnTop; Topmost = Preferences.AlwaysOnTop; ApplyPinAppearance(); SavePreferences(); }

    private void ApplyPinAppearance()
    {
        PinButton.Background = Preferences.AlwaysOnTop ? new SolidColorBrush(Color.FromArgb(45, 82, 163, 245)) : Brushes.Transparent;
        PinButton.BorderBrush = Preferences.AlwaysOnTop ? new SolidColorBrush(Color.FromRgb(85, 167, 245)) : Brushes.Transparent;
        PinButton.Foreground = Preferences.AlwaysOnTop ? new SolidColorBrush(Color.FromRgb(103, 185, 255)) : new SolidColorBrush(Color.FromRgb(183, 195, 214));
        PinButton.ToolTip = Preferences.AlwaysOnTop ? "Always on top: on" : "Always on top: off";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, (string)PinButton.ToolTip);
    }

    internal void SavePreferences()
    {
        foreach (var provider in _providers) provider.SetDisplayMode(Preferences.ShowRemaining);
        foreach (var provider in _providers) provider.SetResetDetails(Preferences.ShowResetDetails);
        foreach (var provider in _providers) provider.SetPace(Preferences.ShowPaceMarkers, Preferences.DailyUsagePercent);
        Preferences.Save(); UpdateFooter(); UpdateCountdown();
        if (_ready && IsLoaded) EnsureOnScreen();
        PreferencesChanged?.Invoke();
    }

    private void SavePosition()
    {
        if (!_ready || !IsLoaded || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        Preferences.Left = Left; Preferences.Top = Top;
        Preferences.WindowWidth = Width; Preferences.WindowHeight = Height; Preferences.WidgetPreferredHeight = _preferredHeight; SavePreferences();
    }

    private void PlaceWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = Preferences.Left ?? area.Right - ActualWidth - 12;
        Top = Preferences.Top ?? area.Bottom - ActualHeight - 12;
        EnsureOnScreen();
    }

    private void RequestContentFit()
    {
        _contentSizeDirty = true;
        if (!_ready || !IsLoaded || _resizing || _exiting) return;
        _pendingContentFit ??= Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _pendingContentFit = null;
            if (!_resizing && !_exiting) EnsureOnScreen();
        }));
    }

    private void EnsureOnScreen()
    {
        if (_fittingContent) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        _fittingContent = true;
        try
        {
            // Measure compact content independently from expanded spacing and text.
            if ((_contentSizeDirty && !_resizing) || _baseContentHeight <= 0)
            {
                _layoutScale = 1;
                UpdatePresentation(WidgetGeometry.DesignWidth, 0);
                var fullHeight = MeasureNaturalHeight();
                UpdatePresentation(WidgetGeometry.MinimumWidth, 1);
                var compactHeight = MeasureNaturalHeight();
                _baseContentHeight = Math.Max(fullHeight, compactHeight * WidgetGeometry.DesignWidth / WidgetGeometry.MinimumWidth);
                _contentSizeDirty = false;
            }
            var ratio = _baseContentHeight / WidgetGeometry.DesignWidth;
            var maximumWidth = Math.Min(WidgetGeometry.MaximumWidth, bottomRight.X - topLeft.X);
            MinWidth = Math.Min(WidgetGeometry.MinimumWidth, maximumWidth);
            MinHeight = 1;
            MaxWidth = maximumWidth;
            MaxHeight = Math.Min(WidgetGeometry.MaximumHeight, bottomRight.Y - topLeft.Y);
            Width = Math.Clamp(Width, MinWidth, MaxWidth);
            var canvasWidth = Math.Max(Math.Min(Width, WidgetGeometry.DesignWidth), Width / 1.3);
            var renderScale = Width / canvasWidth;
            var fittedHeight = Math.Min(MaxHeight, Width * ratio);
            var targetHeight = Math.Clamp(Math.Max(fittedHeight, _preferredHeight ?? 0), 1, MaxHeight);
            var canvasHeight = targetHeight / renderScale;
            var progress = Math.Clamp((WidgetGeometry.DesignWidth - canvasWidth) / (WidgetGeometry.DesignWidth - WidgetGeometry.MinimumWidth), 0, 1);
            _layoutScale = 1;
            UpdatePresentation(canvasWidth, progress);
            // Collapse only as much detail as necessary to fit; never squeeze the usage values.
            var naturalHeight = MeasureNaturalHeight();
            if (naturalHeight > canvasHeight + .1)
            {
                var captionCount = Preferences.ShowResetDetails ? _displayProviders.Sum(p => p.Rows.Count) : 0;
                var fixedHeight = naturalHeight - _displayProviders.Count * ProviderHeaderHeight(progress) - captionCount * 17 * (1 - progress);
                var low = progress; var high = 1d;
                // Solve the continuous height curve numerically, without repeatedly laying out WPF controls.
                for (var i = 0; i < 32; i++)
                {
                    var middle = (low + high) / 2;
                    var estimatedHeight = fixedHeight + _displayProviders.Count * ProviderHeaderHeight(middle) + captionCount * 17 * (1 - middle);
                    if (estimatedHeight > canvasHeight) low = middle; else high = middle;
                }
                UpdatePresentation(canvasWidth, high);
            }
            var verticalRoom = targetHeight / Math.Max(1, fittedHeight) - 1;
            _layoutScale = 1 + Math.Min(Math.Max(0, 1.3 / renderScale - 1), Math.Max(0, verticalRoom - .08) * .45);
            UpdatePresentation(canvasWidth, _providerProgress);
            if (MeasureNaturalHeight() > canvasHeight + 1) { _layoutScale = 1; UpdatePresentation(canvasWidth, _providerProgress); }
            WidgetCanvas.Height = canvasHeight;
            WidgetCanvas.Measure(new Size(canvasWidth, canvasHeight));
            Height = targetHeight;
            Left = Math.Clamp(Left, topLeft.X, Math.Max(topLeft.X, bottomRight.X - Width));
            Top = Math.Clamp(Top, topLeft.Y, Math.Max(topLeft.Y, bottomRight.Y - Height));
        }
        finally { _fittingContent = false; }
    }

    private double MeasureNaturalHeight()
    {
        WidgetCanvas.Height = double.NaN;
        WidgetCanvas.Measure(new Size(WidgetCanvas.Width, double.PositiveInfinity));
        WidgetCanvas.UpdateLayout();
        WidgetCanvas.Measure(new Size(WidgetCanvas.Width, double.PositiveInfinity));
        return WidgetCanvas.DesiredSize.Height;
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
    private static double Mix(double small, double large, double amount) => small + (large - small) * amount;
    private static double ProviderHeaderHeight(double progress) => 39 * (1 - Smooth((progress - .15) / .85));
    private void Resource(string name, object value)
    {
        if (!Equals(Resources[name], value)) Resources[name] = value;
    }

    private void UpdatePresentation(double width, double progress)
    {
        WidgetCanvas.Width = width;
        var size = Smooth((width - WidgetGeometry.MinimumWidth) / (WidgetGeometry.DesignWidth - WidgetGeometry.MinimumWidth));
        var title = 1 - Smooth((width - 300) / 80);
        var mini = 1 - Smooth((width - 240) / 50);
        IsCompact = progress >= .5; IsMini = mini >= .5;
        Resource("QuotaTimeWidth", new GridLength(Mix(64, 88, size)));
        Resource("QuotaPercentWidth", new GridLength(Mix(33, 45, size)));
        Resource("QuotaFontSize", Mix(12.5, 16, size) * _layoutScale);
        Resource("QuotaBarHeight", 8 * _layoutScale);
        Resource("GroupFontSize", Mix(10, 11, size) * _layoutScale);
        Resource("GroupMargin", new Thickness(0, Mix(2, 3, size), 0, Mix(5, 8, size)));
        var margin = Mix(10, 17, size);
        Resource("BodyMargin", new Thickness(margin, 0, margin, 0));
        Resource("HeaderMargin", new Thickness(margin, Mix(10, 16, size), Mix(8, 14, size), Mix(8, 13, size)));
        Resource("ProviderPadding", new Thickness(0, Mix(8, 14, size) * _layoutScale, 0, Mix(3, 7, size) * _layoutScale));
        Resource("QuotaMargin", new Thickness(0, 0, 0, Mix(6, 8, size) * _layoutScale));
        Resource("CompactIconColumn", new GridLength(38 * Smooth(progress / .35)));
        Resource("ProviderHeaderHeight", ProviderHeaderHeight(progress));
        _providerProgress = progress;
        UpdateProviderIdentityOpacity();
        Resource("QuotaCaptionHeight", 17 * (1 - progress));
        Resource("ProviderMessageHeight", 13 * (1 - mini));
        HeaderTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        HeaderCompactTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _titleProgress = title;
        _fullTitleWidth = HeaderTitle.DesiredSize.Width;
        _titleSlotWidth = Mix(_fullTitleWidth, HeaderCompactTitle.DesiredSize.Width, title);
        Resource("TitleSlotWidth", _titleSlotWidth);
        HeaderLogo.Width = Mix(26, 18, mini);
        HeaderLogo.Height = HeaderLogo.Width;
        HeaderLogo.Margin = new Thickness(0, 0, Mix(8, 6, mini), 0);
        var buttonWidth = Mix(26, 24, mini);
        Resource("HeaderButtonWidth", buttonWidth);
        Resource("HeaderButtonHeight", Mix(28, 26, mini));
        UpdateWholeLabels();
    }

    private void UpdateProviderIdentityOpacity()
    {
        if (!_ready) return;
        var rest = IdentityRestProgress;
        // Keep full names if their existing header has enough room; otherwise use the compact logo.
        // Only opacity changes when a drag settles, so the smooth geometry stays exactly where it is.
        var restShowsHeader = ProviderHeaderHeight(_providerProgress) >= 30;
        var movingHeaderOpacity = restShowsHeader ? 1 - Smooth(_providerProgress / .45) : 0;
        Resource("ProviderHeaderOpacity", Mix(movingHeaderOpacity, restShowsHeader ? 1 : 0, rest));
        Resource("ProviderIconOpacity", Mix(Smooth((_providerProgress - .3) / .35), restShowsHeader ? 0 : 1, rest));
        var headerActive = rest >= .5 ? restShowsHeader : _providerProgress < .3;
        Resource("ProviderHeaderHitTest", headerActive);
        Resource("ProviderIconHitTest", !headerActive);
        Resource("QuotaCaptionOpacity", Mix(1 - Smooth(_providerProgress / .35), _providerProgress < .01 ? 1 : 0, rest));
        var mini = 1 - Smooth((WidgetCanvas.Width - 240) / 50);
        Resource("ProviderMessageOpacity", Mix(1 - Smooth(mini / .5), mini < .01 ? 1 : 0, rest));
        UpdateWholeLabels();
    }

    private void UpdateWholeLabels()
    {
        if (!_ready || _fullTitleWidth <= 0) return;
        var rest = IdentityRestProgress;
        // Scale whole words into the continuously sized slot. Never crop letters to abbreviate them.
        // Sequential fades avoid drawing two different words over one another mid-transition.
        var fullTitleFits = _titleSlotWidth >= _fullTitleWidth * .8;
        var titlePhase = Mix(_titleProgress, fullTitleFits ? 0 : 1, rest);
        Resource("HeaderFullTitleOpacity", 1 - Smooth(titlePhase * 2));
        Resource("HeaderCompactTitleOpacity", Smooth((titlePhase - .5) * 2));

    }

    private void AnimateIdentityRest(bool resting)
    {
        if (!resting && _identityMoving) return;
        _identityMoving = !resting;
        BeginAnimation(IdentityRestProgressProperty, new DoubleAnimation(resting ? 1 : 0, TimeSpan.FromMilliseconds(resting ? 140 : 70))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    internal void OpenSettings(string? focusProvider = null)
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        try
        {
            var connectionsBefore = ConnectionKey();
            var dialog = new SettingsWindow(Preferences, Demo, _providers.Where(p => Preferences.ProviderOrder.Contains(p.Id)).Select(p => p.Snapshot),
                CheckProviderAsync) { Owner = this };
            if (focusProvider is not null) dialog.Loaded += (_, _) => dialog.FocusProvider(focusProvider);
            if (dialog.ShowDialog() == true)
            {
                Topmost = Preferences.AlwaysOnTop;
                ApplyPinAppearance(); SavePreferences();
                SyncProviderOrder();
                Dispatcher.BeginInvoke(EnsureOnScreen, DispatcherPriority.Loaded);
                foreach (var provider in _providers.Where(p => !IsProviderEnabled(p.Id)))
                    provider.Set(UsageSnapshot.Empty(provider.Id, ConnectionState.Disabled, "Paused in Settings."));
                var connectionsAfter = ConnectionKey();
                if (!Demo && (connectionsBefore != connectionsAfter || dialog.ClaudeSignInCompleted)) _ = RefreshAsync();
            }
        }
        finally { _settingsOpen = false; }
    }

    private string ConnectionKey() => string.Join(",", Preferences.ProviderOrder.Select(id => id + ":" + IsProviderEnabled(id))) + "|" + Preferences.CodexExecutable;

    internal static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { System.Windows.MessageBox.Show("Windows could not open your browser.", "TokenWatch"); }
    }

    private void OpenProviderDetails(string id, string usageUrl)
    {
        if (_providers.First(p => p.Id == id).Snapshot.State == ConnectionState.Connected && !Demo) OpenUrl(usageUrl);
        else OpenSettings(id);
    }

    internal void Exit()
    {
        SavePosition(); _exiting = true; StopMonitoring(); Close();
    }
    internal void StopMonitoring()
    {
        _lifetime.Cancel(); _clock.Stop(); _resizeIdle.Stop();
        BeginAnimation(IdentityRestProgressProperty, null);
        _pendingResize?.Abort(); _pendingContentFit?.Abort();
    }

    internal void Capture(string path)
        => CaptureWindow(this, path);

    internal static void CaptureWindow(Window window, string path)
        => CaptureElement(window, path);

    internal static void CaptureElement(FrameworkElement window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(window) { Stretch = Stretch.Fill }, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private void LoadSampleData()
    {
        var now = DateTimeOffset.UtcNow;
        _providers[0].Set(new("claude", ConnectionState.Connected, "Sample data", [new("5 hour", 64, now.AddHours(2).AddMinutes(14)), new("Weekly", 38, now.AddDays(2))], now));
        _providers[1].Set(new("codex", ConnectionState.Connected, "Sample data", [new("5 hour", 28, now.AddHours(3).AddMinutes(32)), new("Weekly", 52, now.AddDays(3))], now));
        _providers[2].Set(new("antigravity", ConnectionState.Connected, "Sample data", [new("5 hour", 83, now.AddHours(1).AddMinutes(7), Key: "gemini-5h", Group: "Gemini Models"),
            new("Weekly", 61, now.AddDays(4), Key: "gemini-weekly", Group: "Gemini Models"),
            new("Weekly", 42, now.AddDays(3), Key: "gemini-argon-wtu-weekly", Group: "Gemini 4 Argon")], now));
        _providers.First(p => p.Id == "copilot").Set(new("copilot", ConnectionState.Connected, "Sample data", [new("AI credits", 34, now.AddDays(12), Key: "premium_interactions")], now));
        _providers.First(p => p.Id == "cursor").Set(new("cursor", ConnectionState.Connected, "Sample data", [new("Included usage", 47, now.AddDays(9), Key: "included"), new("On-demand budget", 12, now.AddDays(9), Key: "on-demand")], now));
        UpdateFooter(); UpdateCountdown();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element)
            for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is System.Windows.Controls.Primitives.ButtonBase) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); SavePosition(); } catch (InvalidOperationException) { }
    }
    internal void ApplyResize(ResizeEdges edges, double dx, double dy)
    {
        if (!_ready || !IsLoaded) return;
        ApplyResize(new(Left, Top, Width, Height), edges, dx, dy);
    }
    private void ApplyResize(WidgetRectangle origin, ResizeEdges edges, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
        if (edges.HasFlag(ResizeEdges.Left) || edges.HasFlag(ResizeEdges.Right))
            Width = Math.Clamp(origin.Width + (edges.HasFlag(ResizeEdges.Left) ? -dx : dx), MinWidth, MaxWidth);
        if (edges.HasFlag(ResizeEdges.Top) || edges.HasFlag(ResizeEdges.Bottom))
            _preferredHeight = Math.Clamp(origin.Height + (edges.HasFlag(ResizeEdges.Top) ? -dy : dy), 1, MaxHeight);
        EnsureOnScreen();
        if (edges.HasFlag(ResizeEdges.Left)) Left = origin.Left + origin.Width - Width;
        if (edges.HasFlag(ResizeEdges.Top)) Top = origin.Top + origin.Height - Height;
    }
    private void Resize_Started(object sender, DragStartedEventArgs e)
    {
        if (sender is not FrameworkElement element || !Enum.TryParse(element.Tag?.ToString(), out _resizeEdges)) return;
        _resizePointerOrigin = _resizePointer = ReadResizePointer();
        if (!double.IsFinite(_resizePointer.X) || !double.IsFinite(_resizePointer.Y)) return;
        _resizing = true;
        _resizeOrigin = new(Left, Top, Width, Height);
        _resizePreferredOrigin = _preferredHeight;
        _lastResizePointer = null;
        _resizeDeviceToLogical = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        AnimateIdentityRest(false);
        _resizeIdle.Stop(); _resizeIdle.Start();
    }
    private void Resize_Delta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizing) return;
        var pointer = ReadResizePointer();
        if (!double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y)) return;
        if (pointer != _resizePointer)
        {
            AnimateIdentityRest(false);
            _resizeIdle.Stop(); _resizeIdle.Start();
        }
        _resizePointer = pointer;
        // Thumb deltas are relative to the moving handle. Reflow moves that handle even
        // when the pointer barely moves, so always use screen displacement from drag start.
        // Coalesce rapid input before rendering instead of repainting for every event.
        _pendingResize ??= Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FlushResize));
        e.Handled = true;
    }
    private void FlushResize()
    {
        _pendingResize = null;
        if (!_resizing || _lastResizePointer == _resizePointer) return;
        _lastResizePointer = _resizePointer;
        var delta = _resizeDeviceToLogical.Transform(_resizePointer - _resizePointerOrigin);
        ApplyResize(_resizeOrigin, _resizeEdges, delta.X, delta.Y);
    }
    private void Resize_Completed(object sender, DragCompletedEventArgs e)
    {
        if (!_resizing) return;
        _pendingResize?.Abort();
        _pendingResize = null;
        if (e.Canceled) { _preferredHeight = _resizePreferredOrigin; Width = _resizeOrigin.Width; Height = _resizeOrigin.Height; Left = _resizeOrigin.Left; Top = _resizeOrigin.Top; }
        else
        {
            var pointer = ReadResizePointer();
            if (double.IsFinite(pointer.X) && double.IsFinite(pointer.Y)) _resizePointer = pointer;
            FlushResize();
        }
        _resizing = false;
        _resizeIdle.Stop();
        EnsureOnScreen();
        SavePosition();
        AnimateIdentityRest(true);
        if (!IsActive) HandleDeactivation();
    }
    internal void FitPreferredHeight(double? height) { _preferredHeight = height; EnsureOnScreen(); SavePosition(); }
    internal void HandleDeactivation() { if (_ready && !Preferences.Pinned && !_settingsOpen && !_countdownMenuOpen && !_resizing) HideWidget(); }
    private void Window_Deactivated(object? sender, EventArgs e) => HandleDeactivation();
    private void Pin_Click(object sender, RoutedEventArgs e) => ToggleTopmost();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void Hide_Click(object sender, RoutedEventArgs e) => HideWidget();
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideWidget(); e.Handled = true; }
        if (e.Key == Key.F5) { _ = RefreshAsync(); e.Handled = true; }
    }
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (Demo || !_trayAvailable) return;
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, -20); SetWindowLong(handle, -20, style | 0x80); // WS_EX_TOOLWINDOW
    }
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    private static Point ReadCursorPosition() => GetCursorPos(out var point) ? new(point.X, point.Y) : new(double.NaN, double.NaN);
}
