using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TokenWatch.Core;

namespace TokenWatch;

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
}

internal sealed class QuotaRow(QuotaWindow quota, string color, bool stale, bool showRemaining = false, string? groupHeading = null, bool showResetDetails = true, bool highlight = false, bool active = false) : Observable
{
    private bool _showPace = true;
    private double _dailyPercent = 20;
    private bool _connected = !stale;
    public bool Highlight { get; private set; } = highlight;
    public bool Active { get; private set; } = active;
    public (string? Key, string? Group, string Label) Identity => (quota.Key, quota.Group, quota.Label);
    public Brush HighlightBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public void ClearHighlight() => Highlight = false;
    public string? GroupHeading => groupHeading;
    public string? CompactGroupHeading => groupHeading is null ? null : groupHeading.Contains("Argon", StringComparison.OrdinalIgnoreCase) ? "Argon" : "Gemini";
    public Visibility HeadingVisibility => groupHeading is null ? Visibility.Collapsed : Visibility.Visible;
    public string Label => quota.Label;
    public string PrimaryText => showResetDetails ? quota.ResetSummaryText(DateTimeOffset.Now) : quota.Label;
    public string Percent => quota.DisplayText(showRemaining);
    public double Value => quota.DisplayPercent(showRemaining) ?? 0;
    public double Opacity => stale ? .5 : 1;
    public Brush Accent => new SolidColorBrush((Color)ColorConverter.ConvertFromString(quota.IsExhausted ? "#F87575" : quota.IsWarning ? "#FFCC67" : color));
    public Brush PercentBrush => quota.IsWarning ? Accent : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F4FA"));
    public string Reset => quota.ResetDetailsText(DateTimeOffset.Now);
    public Visibility ResetVisibility => showResetDetails ? Visibility.Visible : Visibility.Collapsed;
    private WeeklyPace? Pace => _connected && _showPace ? WeeklyPace.Calculate(quota, DateTimeOffset.Now, dailyPercent: _dailyPercent) : null;
    public double? PacePercent => Pace?.DisplayPercent(showRemaining);
    public Visibility PaceVisibility => Pace is null ? Visibility.Collapsed : Visibility.Visible;
    public string Description => $"{quota.Label}: {(quota.UsedPercent is null ? quota.Note ?? "unavailable" : Percent + (showRemaining ? " remaining" : " used"))}. {Reset}" + (Pace is { } pace ? "\n" + pace.Explanation(showRemaining) : "");
    public void Tick() { Changed(nameof(Reset)); Changed(nameof(PrimaryText)); Changed(nameof(Description)); Changed(nameof(PacePercent)); Changed(nameof(PaceVisibility)); }
    internal void Update(QuotaWindow next, bool isStale, bool remaining, string? heading, bool reset, bool pending, bool isActive, bool showPace, double budget, bool connected)
    {
        var oldValue = Value; var oldPercent = Percent;
        var activityChanged = Highlight != pending || Active != isActive;
        quota = next; stale = isStale; showRemaining = remaining; groupHeading = heading; showResetDetails = reset;
        Highlight = pending; Active = isActive; _showPace = showPace; _dailyPercent = budget; _connected = connected;
        if (oldValue != Value) Changed(nameof(Value));
        if (oldPercent != Percent) Changed(nameof(Percent));
        foreach (var name in new[] { nameof(Label), nameof(GroupHeading), nameof(CompactGroupHeading), nameof(HeadingVisibility), nameof(Opacity), nameof(Accent), nameof(PercentBrush), nameof(ResetVisibility) }) Changed(name);
        if (activityChanged) Changed(nameof(HighlightBrush));
        Tick();
    }
}

internal sealed class ProviderViewModel : Observable
{
    public string Id { get; }
    public string Name { get; }
    public ImageSource Icon { get; }
    public string Color { get; }
    public ObservableCollection<QuotaRow> Rows { get; } = [];
    public UsageSnapshot Snapshot { get; private set; }
    public string Message => Snapshot.State == ConnectionState.Stale && Snapshot.UpdatedAt is { } observed
        ? $"Last updated {observed.LocalDateTime:g}. {Snapshot.Message}" : Snapshot.Message;
    public string Status => Snapshot.Message == "Sample data" ? "Sample" : Snapshot.RetryAt > DateTimeOffset.UtcNow ? "Cooldown" : Snapshot.State switch { ConnectionState.Connected => "Live", ConnectionState.Stale => "Stale",
        ConnectionState.Loading => "Connecting", ConnectionState.SignInRequired => "Sign in", ConnectionState.Disabled => "Paused",
        ConnectionState.NotRunning => "Not running", ConnectionState.RateLimited => "Cooldown", _ => "Unavailable" };
    public Brush StatusBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Snapshot.State switch
    { ConnectionState.Connected => "#7EE2BC", ConnectionState.Stale or ConnectionState.SignInRequired or ConnectionState.RateLimited => "#E9BB72", _ => "#94A3B8" }));
    public Visibility MessageVisibility => Snapshot.State == ConnectionState.Connected ? Visibility.Collapsed : Visibility.Visible;
    public string ActionHint => Snapshot.State == ConnectionState.Connected && Id != "antigravity" ? "Open usage page" : "Connection details";
    public string DetailsHint => $"{Name} · {Status}\n{(Snapshot.State == ConnectionState.Connected ? Snapshot.UpdatedAt is { } time ? $"Updated {time.LocalDateTime:g}\n" : "" : Message + "\n")}{ActionHint}";
    private bool _showRemaining;
    private bool _showResetDetails = true;
    private bool _showPace = true;
    private double _dailyPercent = 20;
    public void SetPace(bool enabled, double dailyPercent)
    {
        if (_showPace == enabled && _dailyPercent == dailyPercent) return;
        _showPace = enabled; _dailyPercent = dailyPercent; RebuildRows();
    }
    public void SetResetDetails(bool showResetDetails)
    {
        if (_showResetDetails == showResetDetails) return;
        _showResetDetails = showResetDetails;
        RebuildRows();
    }
    public void SetDisplayMode(bool showRemaining)
    {
        if (_showRemaining == showRemaining) return;
        _showRemaining = showRemaining;
        RebuildRows();
    }
    public ICommand OpenUsageCommand { get; }

    public ProviderViewModel(string id, string name, string color, Action openUsage)
    {
        Id = id; Name = name; Color = color; Icon = ProviderIcon(id, color);
        OpenUsageCommand = new RelayCommand(openUsage);
        Snapshot = UsageSnapshot.Empty(id, ConnectionState.Loading, "Connecting to your local sign-in…");
        RebuildRows();
    }

    private static ImageSource ProviderIcon(string id, string color) => new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/Assets/{id}.png"));

    public void Set(UsageSnapshot snapshot)
    {
        var previous = Snapshot;
        Snapshot = snapshot;
        RebuildRows(previous);
        foreach (var property in new[] { nameof(Message), nameof(Status), nameof(StatusBrush), nameof(MessageVisibility), nameof(ActionHint), nameof(DetailsHint) }) Changed(property);
    }

    private void RebuildRows(UsageSnapshot? previous = null)
    {
        var pending = Rows.Where(row => row.Highlight).Select(row => row.Identity).ToHashSet();
        var active = Rows.Where(row => row.Active).Select(row => row.Identity).ToHashSet();
        var previousWindows = previous is null ? null : Id == "antigravity" ? AntigravityPresentation.VisibleWindows(previous.Windows) : previous.Windows;
        var desired = new List<QuotaRow>();
        string? previousHeading = null;
        foreach (var window in Id == "antigravity" ? AntigravityPresentation.VisibleWindows(Snapshot.Windows) : Snapshot.Windows)
        {
            var heading = Id == "antigravity" && window.Group != previousHeading ? window.Group : null;
            var oldWindow = previousWindows?.FirstOrDefault(w => w.Key == window.Key && w.Group == window.Group && w.Label == window.Label);
            var highlight = Snapshot.State == ConnectionState.Connected && oldWindow?.UsedPercent is not null && window.UsedPercent is not null
                && oldWindow.DisplayText(_showRemaining) != window.DisplayText(_showRemaining);
            var identity = (window.Key, window.Group, window.Label);
            var comparable = Snapshot.State == ConnectionState.Connected && oldWindow?.UsedPercent is not null && window.UsedPercent is not null;
            var isActive = comparable ? highlight : Snapshot.State != ConnectionState.Disabled && active.Contains(identity);
            var showHighlight = highlight || (!comparable && pending.Contains(identity));
            var row = Rows.FirstOrDefault(r => r.Identity == identity && !desired.Contains(r)) ?? new(window, Color, Snapshot.State == ConnectionState.Stale);
            row.Update(window, Snapshot.State == ConnectionState.Stale, _showRemaining, heading, _showResetDetails, showHighlight, isActive, _showPace, _dailyPercent, Snapshot.State == ConnectionState.Connected);
            desired.Add(row);
            previousHeading = window.Group;
        }
        for (var i = Rows.Count - 1; i >= 0; i--) if (!desired.Contains(Rows[i])) Rows.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var old = Rows.IndexOf(desired[i]);
            if (old < 0) Rows.Insert(i, desired[i]); else if (old != i) Rows.Move(old, i);
        }
    }
}
