using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

// Stable control instances are retained across watchdog ticks and provider observations.
// Only actual numeric changes write ProgressBar.Value, preserving the native animation lifecycle.
internal sealed class ProviderVisual
{
    internal StackPanel Section { get; } = new() { Spacing = 6, Padding = new(0, 10, 0, 9), BorderBrush = Ui.Brush("#353941"), BorderThickness = new(0, 1, 0, 0) };
    private readonly Button _status;
    private readonly TextBlock _statusText;
    private readonly TextBlock _name;
    private readonly FrameworkElement _logo;
    private double _scale = 1, _space, _outer;
    private readonly Dictionary<(string? Key, string? Group, string Label, int Occurrence), QuotaVisual> _rows = [];
    private readonly SolidColorBrush _accent;
    private readonly SolidColorBrush _warning = Ui.Brush("#E6BE7D");
    private readonly SolidColorBrush _white = Ui.Brush("#F2F3F7");
    internal int QuotaCount => _rows.Count;
    internal ProviderVisual(ProviderItem provider)
    {
        _accent = provider.Accent;
        var header = new Grid { ColumnSpacing = 8, Margin = new(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _logo = Ui.Logo(provider, 22); header.Children.Add(_logo);
        var name = Ui.Text(provider.Name, 14); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; name.TextTrimming = TextTrimming.CharacterEllipsis;
        _name = name;
        Grid.SetColumn(name, 1); header.Children.Add(name);
        _statusText = Ui.Text("", 10);
        _status = new Button { Content = _statusText, Style = (Style)Application.Current.Resources["QuietButton"], Padding = new(4, 0, 0, 0), MinHeight = 22 };
        AutomationProperties.SetName(_status, $"{provider.Name} connection details");
        var id = provider.Id;
        _status.Click += (_, _) => App.ShowManager().SelectProvider(id);
        Grid.SetColumn(_status, 2); header.Children.Add(_status); Section.Children.Add(header);
    }
    internal void Update(ProviderItem provider, bool showRemaining, bool showReset, bool showPaceMarkers, double dailyUsagePercent)
    {
        UpdateStatus(provider);
        var occurrences = new Dictionary<(string? Key, string? Group, string Label), int>();
        var desired = new List<UIElement> { Section.Children[0] };
        var active = new HashSet<(string? Key, string? Group, string Label, int Occurrence)>();
        foreach (var quota in provider.Quotas)
        {
            var identity = (quota.Key, quota.Group, quota.Label);
            occurrences.TryGetValue(identity, out var occurrence); occurrences[identity] = occurrence + 1;
            var key = (quota.Key, quota.Group, quota.Label, occurrence); active.Add(key);
            if (!_rows.TryGetValue(key, out var row)) { row = new(); _rows.Add(key, row); }
            var unknown = quota.UsedPercent is null;
            var warning = !unknown && quota.IsWarning;
            row.Row.Opacity = provider.Connection == ConnectionState.Connected ? 1 : .58;
            row.Label.Text = quota.Group is null ? quota.Label : $"{quota.Group} · {(quota.Label == "5 hour" ? "5h" : quota.Label)}";
            row.Percent.Text = unknown ? "—" : quota.DisplayText(showRemaining);
            row.Percent.Foreground = warning ? _warning : _white;
            row.Bar.Foreground = warning ? _warning : _accent;
            var value = unknown ? 0 : quota.DisplayPercent(showRemaining) ?? 0;
            if (row.Bar.Value != value) row.Bar.Value = value;
            row.Quota = quota; row.ShowReset = showReset;
            row.ShowRemaining = showRemaining; row.ProviderName = provider.Name;
            row.ShowPaceMarkers = showPaceMarkers; row.DailyUsagePercent = dailyUsagePercent;
            row.IsConnected = provider.Connection == ConnectionState.Connected;
            row.ApplyLayout(_scale, _space);
            row.UpdateReset();
            desired.Add(row.Row);
        }
        foreach (var key in _rows.Keys.Where(k => !active.Contains(k)).ToArray()) _rows.Remove(key);
        Reconcile(Section, desired);
    }
    internal double MeasureHeight(double width, double scale = 1)
    {
        // This independent measurement always starts from compact metrics; enlarging
        // the rendered controls must never raise the native minimum window height.
        return 20 + 22 * scale + 2 + _rows.Count * 6
            + _rows.Values.Sum(row => row.MeasureHeight(width, scale));
    }
    internal void ApplyLayout(double scale, double space, double outer)
    {
        if (_scale == scale && _space == space && _outer == outer) return;
        _scale = scale; _space = space; _outer = outer;
        _name.FontSize = 14 * scale; _statusText.FontSize = 10 * scale;
        _logo.Width = _logo.Height = 22 * scale; _status.MinHeight = 22 * scale;
        Section.Spacing = 6 + space;
        Section.Padding = new(0, 10 + space + outer, 0, 9 + space + outer);
        foreach (var row in _rows.Values) row.ApplyLayout(scale, space);
    }
    internal void Tick(ProviderItem provider)
    {
        UpdateStatus(provider);
        foreach (var row in _rows.Values) row.UpdateReset();
    }
    private void UpdateStatus(ProviderItem provider)
    {
        var text = provider.Snapshot.RetryAt > DateTimeOffset.UtcNow ? "Cooldown" : provider.Connection switch
        { ConnectionState.Stale => "Stale", ConnectionState.RateLimited => "Cooldown", ConnectionState.SignInRequired => "Sign in", ConnectionState.Loading => "Connecting", ConnectionState.NotRunning => "Open app", ConnectionState.Connected => provider.IsDemo ? "Sample" : "Live", _ => "Offline" };
        if (_statusText.Text != text) _statusText.Text = text;
        _statusText.Foreground = provider.Connection == ConnectionState.Connected ? Ui.Brush("#91CBB0") : Ui.Brush("#E2BA7C");
        ToolTipService.SetToolTip(_status, provider.Status + " · Open provider details");
    }
    internal static void Reconcile(Panel panel, IReadOnlyList<UIElement> desired)
    {
        for (var index = panel.Children.Count - 1; index >= 0; index--)
            if (!desired.Contains(panel.Children[index])) panel.Children.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], desired[index])) continue;
            var previous = panel.Children.IndexOf(desired[index]);
            if (previous >= 0) panel.Children.RemoveAt(previous);
            panel.Children.Insert(index, desired[index]);
        }
    }
    private sealed class QuotaVisual
    {
        internal StackPanel Row { get; } = new() { Spacing = 4, Margin = new(0, 3, 0, 0) };
        internal TextBlock Label { get; } = Ui.Text("", 11.5, "#B4BAC6");
        internal TextBlock Percent { get; } = Ui.Text("", 15);
        internal TextBlock Reset { get; } = Ui.Text("", 12.5, "#C9CED8");
        internal ColumnDefinition ResetColumn { get; } = new() { Width = new(76) };
        private readonly ColumnDefinition _percentColumn = new() { Width = new(50) };
        private readonly TextBlock _measureLabel = Ui.Text("", 11.5);
        private readonly TextBlock _measurePercent = Ui.Text("100%", 15);
        private readonly TextBlock _measureReset = Ui.Text("12:00 PM", 12.5);
        private (string Text, double Width, double Scale, bool Reset, bool Pace)? _measurement;
        private double _measuredHeight;
        private double _scale = 1;
        internal ProgressBar Bar { get; } = new() { Minimum = 0, Maximum = 100, Height = 6, MinHeight = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Background = Ui.Brush("#3A3F49"), IsTabStop = false };
        private readonly Grid _barSurface = new() { Height = 6 };
        private readonly Border _paceMarker = new() { Name = "WeeklyPaceMarker", Width = 2, Height = 6, CornerRadius = new(1), Background = Ui.Brush("#F2F3F7"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private readonly Microsoft.UI.Xaml.Shapes.Polygon _paceCaret = new()
        {
            Name = "WeeklyPaceCaret", Width = 7, Height = 4,
            Points = new() { new(0, 4), new(3.5, 0), new(7, 4) },
            Stretch = Stretch.Fill, Fill = Ui.Brush("#F2F3F7"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        private WeeklyPace? _pace;
        internal QuotaWindow? Quota { get; set; }
        internal bool ShowReset { get; set; }
        internal bool ShowRemaining { get; set; }
        internal bool ShowPaceMarkers { get; set; }
        internal double DailyUsagePercent { get; set; } = 20;
        internal bool IsConnected { get; set; }
        internal string ProviderName { get; set; } = "";
        internal QuotaVisual()
        {
            var labels = new Grid { ColumnSpacing = 8 };
            labels.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); labels.ColumnDefinitions.Add(_percentColumn); labels.ColumnDefinitions.Add(ResetColumn);
            Label.TextWrapping = TextWrapping.Wrap;
            Percent.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; Percent.HorizontalAlignment = HorizontalAlignment.Right;
            Reset.HorizontalAlignment = HorizontalAlignment.Right;
            labels.Children.Add(Label); Grid.SetColumn(Percent, 1); labels.Children.Add(Percent); Grid.SetColumn(Reset, 2); labels.Children.Add(Reset);
            Bar.VerticalAlignment = VerticalAlignment.Top;
            _barSurface.Children.Add(Bar); _barSurface.Children.Add(_paceMarker); _barSurface.Children.Add(_paceCaret);
            _barSurface.SizeChanged += (_, _) => PositionMarker();
            Row.Children.Add(labels); Row.Children.Add(_barSurface);
            _measureLabel.TextWrapping = TextWrapping.Wrap;
            _measurePercent.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        internal double MeasureHeight(double width, double scale)
        {
            var key = (Label.Text, width, scale, ShowReset, _pace is not null);
            if (_measurement == key) return _measuredHeight;
            _measurement = key;
            _measureLabel.Text = Label.Text; _measureLabel.FontSize = 11.5 * scale;
            _measurePercent.FontSize = 15 * scale; _measureReset.FontSize = 12.5 * scale;
            var labelWidth = Math.Max(1, width - 16 - 50 * scale - (ShowReset ? 76 * scale : 0));
            _measureLabel.Measure(new(labelWidth, double.PositiveInfinity));
            _measurePercent.Measure(new(50 * scale, double.PositiveInfinity));
            _measureReset.Measure(new(76 * scale, double.PositiveInfinity));
            _measuredHeight = 3 + Math.Max(_measureLabel.DesiredSize.Height,
                Math.Max(_measurePercent.DesiredSize.Height, ShowReset ? _measureReset.DesiredSize.Height : 0)) + 4 + (6 + (_pace is null ? 0 : 8)) * scale;
            return _measuredHeight;
        }
        internal void ApplyLayout(double scale, double space)
        {
            _scale = scale;
            Label.FontSize = 11.5 * scale; Percent.FontSize = 15 * scale; Reset.FontSize = 12.5 * scale;
            _percentColumn.Width = new(50 * scale); ResetColumn.Width = new(ShowReset ? 76 * scale : 0);
            // WinUI's indicator binds to MinHeight, rather than the outer Height.
            Bar.MinHeight = Bar.Height = 6 * scale;
            _barSurface.Height = Bar.Height + (_pace is null ? 0 : 8 * scale);
            _paceMarker.Height = Bar.Height;
            _paceCaret.Width = 7 * scale; _paceCaret.Height = 4 * scale;
            PositionMarker();
            Row.Spacing = 4 + space * .5; Row.Margin = new(0, 3 + space * .5, 0, 0);
        }
        internal void UpdateReset()
        {
            var now = DateTimeOffset.Now;
            var text = ShowReset && Quota?.UsedPercent is not null ? Quota.ResetSummaryText(now) : "";
            if (Reset.Text != text) Reset.Text = text;
            if (Quota is not { } quota) return;
            _pace = IsConnected && ShowPaceMarkers ? WeeklyPace.Calculate(quota, now, dailyPercent: DailyUsagePercent) : null;
            _paceMarker.Visibility = _pace is null ? Visibility.Collapsed : Visibility.Visible;
            _paceCaret.Visibility = _paceMarker.Visibility;
            _barSurface.Height = Bar.Height + (_pace is null ? 0 : 8 * _scale);
            PositionMarker();
            var description = $"{ProviderName}, {quota.Group} {quota.Label}, {(quota.UsedPercent is null ? "unavailable" : quota.DisplayText(ShowRemaining) + (ShowRemaining ? " remaining" : " used"))}, {quota.ResetDetailsText(now)}";
            if (_pace is not null) description += "\n" + _pace.Explanation(ShowRemaining);
            if (AutomationProperties.GetName(Bar) != description)
            {
                AutomationProperties.SetName(Bar, description);
                ToolTipService.SetToolTip(Row, description);
            }
        }
        private void PositionMarker()
        {
            if (_pace is null) return;
            var percent = _pace.DisplayPercent(ShowRemaining);
            _paceMarker.Tag = percent;
            // Keep the caret centered on its tick, including at the quota endpoints.
            var center = Math.Clamp(_barSurface.ActualWidth * percent / 100, _paceCaret.Width / 2, Math.Max(_paceCaret.Width / 2, _barSurface.ActualWidth - _paceCaret.Width / 2));
            var left = center - _paceMarker.Width / 2;
            _paceMarker.Margin = new(left, 0, 0, 0);
            _paceCaret.Margin = new(center - _paceCaret.Width / 2, Bar.Height + 2 * _scale, 0, 0);
        }
    }
}
