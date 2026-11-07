using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ProviderItem(ProviderDefinition definition, bool demo = true) : Observable
{
    public ProviderDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public override string ToString() => Name;
    public string Logo => $"ms-appx:///Assets/{Id}.png";
    public ImageSource LogoImage => new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Logo));
    public SolidColorBrush Accent => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(Definition.Color.Substring(1, 2), 16), Convert.ToByte(Definition.Color.Substring(3, 2), 16), Convert.ToByte(Definition.Color.Substring(5, 2), 16)));
    private bool _enabled = true;
    private UsageSnapshot _snapshot = demo ? new(definition.Id, ConnectionState.Connected, "Sample data", SampleQuotas(definition.Id), DateTimeOffset.UtcNow)
        : UsageSnapshot.Empty(definition.Id, ConnectionState.Loading, "Connecting to your existing sign-in…");
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; Changed(); Changed(nameof(Status)); } }
    public ConnectionState Connection
    {
        get => _snapshot.State;
        set => SetSnapshot(IsDemo && value == ConnectionState.Connected ? new(Id, value, "Sample data", SampleQuotas(Id), DateTimeOffset.UtcNow)
            : IsDemo && value == ConnectionState.SignInRequired ? _snapshot with { State = value, Windows = SampleQuotas(Id).Select(q => q with { UsedPercent = null }).ToArray() }
            : _snapshot with { State = value });
    }
    public UsageSnapshot Snapshot => _snapshot;
    public bool IsDemo { get; } = demo;
    public void SetSnapshot(UsageSnapshot snapshot) { _snapshot = snapshot; Changed(nameof(Connection)); Changed(nameof(Snapshot)); Changed(nameof(Quotas)); Changed(nameof(Status)); }
    public string Status => !Enabled ? "Off · hidden from widget" : Connection switch { ConnectionState.Loading => "Connecting…", ConnectionState.Stale => "Stale · last known reading", ConnectionState.SignInRequired => "Sign in needed", ConnectionState.RateLimited => "Server cooldown", ConnectionState.NotRunning => "Not running", ConnectionState.Connected => IsDemo ? "Connected · sample" : "Connected", _ => "Unavailable" };
    public IReadOnlyList<QuotaWindow> Quotas => Id == "antigravity" ? AntigravityPresentation.VisibleWindows(_snapshot.Windows) : _snapshot.Windows;
    private static QuotaWindow[] SampleQuotas(string id)
    {
        var now = DateTimeOffset.Now;
        return id switch
        {
            "claude" => [new("5 hour", 64, now.AddHours(2), Key: "5h"), new("Weekly", 38, now.AddDays(2), Key: "weekly")],
            "codex" => [new("5 hour", 28, now.AddHours(3), Key: "5h"), new("Weekly", 52, now.AddDays(3), Key: "weekly")],
            "antigravity" => [new("5 hour", 83, now.AddHours(1), Key: "gemini-5h", Group: "Gemini"), new("Weekly", 61, now.AddDays(4), Key: "gemini-weekly", Group: "Gemini"), new("Weekly", 42, now.AddDays(3), Key: "argon-weekly", Group: "Argon")],
            "copilot" => [new("AI credits", 34, now.AddDays(12), Key: "credits")],
            _ => [new("Included", 47, now.AddDays(9), Key: "included"), new("Budget", 12, now.AddDays(9), Key: "budget")]
        };
    }
}

public sealed class PreviewSession : Observable
{
    public ObservableCollection<ProviderItem> Providers { get; } = [];
    public string SettingsPath { get; }
    public string? PersistenceError => TokenWatch.Settings.PersistenceError;
    public bool IsDemo { get; }
    internal TokenWatch.Settings Backend { get; }
    private bool _loading;
    private bool _showRemaining = true;
    private bool _showReset = true;
    private bool _showPaceMarkers = true;
    private double _dailyUsagePercent = 20;
    private bool _topmost;
    private int _refreshMinutes = 2;
    private bool _keepOpen = true;
    public bool KeepOpen { get => _keepOpen; set { if (_keepOpen == value) return; _keepOpen = value; Update(); } }
    public string? CodexExecutable { get => Backend.CodexExecutable; set { if (Backend.CodexExecutable == value) return; Backend.CodexExecutable = value; Update(); } }
    public bool ShowRemaining { get => _showRemaining; set { if (_showRemaining == value) return; _showRemaining = value; Update(); } }
    public bool ShowReset { get => _showReset; set { if (_showReset == value) return; _showReset = value; Update(); } }
    public bool ShowPaceMarkers { get => _showPaceMarkers; set { if (_showPaceMarkers == value) return; _showPaceMarkers = value; Update(); } }
    public double DailyUsagePercent { get => _dailyUsagePercent; set { var normalized = WeeklyPace.NormalizeDailyPercent(value); if (_dailyUsagePercent == normalized) return; _dailyUsagePercent = normalized; Update(); } }
    public bool Topmost { get => _topmost; set { if (_topmost == value) return; _topmost = value; Update(); } }
    public int RefreshMinutes { get => _refreshMinutes; set { if (_refreshMinutes == value) return; _refreshMinutes = Math.Clamp(value, 1, 60); Update(); } }
    private PreviewSession(string path, bool demo, TokenWatch.Settings backend) { SettingsPath = path; IsDemo = demo; Backend = backend; }
    public static PreviewSession Load(string path, bool demo = true)
    {
        var settings = SettingsMigration.Load(path, demo);
        var session = new PreviewSession(path, demo, settings) { _loading = true };
        session._showRemaining = settings.ShowRemaining; session._showReset = settings.ShowResetDetails;
        session._showPaceMarkers = settings.ShowPaceMarkers; session._dailyUsagePercent = WeeklyPace.NormalizeDailyPercent(settings.DailyUsagePercent);
        session._topmost = settings.AlwaysOnTop; session._refreshMinutes = settings.RefreshMinutes; session._keepOpen = settings.Pinned;
        foreach (var id in ProviderCatalog.Normalize(settings.ProviderOrder))
        {
            var item = new ProviderItem(ProviderCatalog.All.First(p => p.Id == id), demo) { Enabled = settings.IsProviderEnabled(id) };
            session.Observe(item); session.Providers.Add(item);
        }
        session.Providers.CollectionChanged += (_, _) => session.Update();
        session._loading = false; return session;
    }
    private void Observe(ProviderItem item) => item.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ProviderItem.Enabled)) Update(); else if (args.PropertyName == nameof(ProviderItem.Connection)) NotifyUsage(); };
    public void Add(string id)
    {
        if (Providers.Any(p => p.Id == id) || ProviderCatalog.All.FirstOrDefault(p => p.Id == id) is not { } definition) return;
        var item = new ProviderItem(definition, IsDemo); Observe(item); Providers.Add(item);
    }
    public void Move(ProviderItem item, int direction)
    {
        var index = Providers.IndexOf(item); var next = index + direction;
        if (index >= 0 && next >= 0 && next < Providers.Count) Providers.Move(index, next);
    }
    private void Update()
    {
        if (_loading) return;
        Save(); Changed("Preferences");
    }
    internal void NotifyUsage() => Changed("Usage");
    public void Save()
    {
        Backend.ProviderOrder = Providers.Select(p => p.Id).ToArray();
        Backend.ClaudeEnabled = Providers.Any(p => p.Id == "claude" && p.Enabled); Backend.CodexEnabled = Providers.Any(p => p.Id == "codex" && p.Enabled);
        Backend.AntigravityEnabled = Providers.Any(p => p.Id == "antigravity" && p.Enabled); Backend.CopilotEnabled = Providers.Any(p => p.Id == "copilot" && p.Enabled); Backend.CursorEnabled = Providers.Any(p => p.Id == "cursor" && p.Enabled);
        Backend.ShowRemaining = ShowRemaining; Backend.ShowResetDetails = ShowReset; Backend.AlwaysOnTop = Topmost; Backend.Pinned = KeepOpen; Backend.RefreshMinutes = RefreshMinutes;
        Backend.ShowPaceMarkers = ShowPaceMarkers; Backend.DailyUsagePercent = DailyUsagePercent;
        TokenWatch.Settings.FilePath = SettingsPath; Backend.Save();
    }
    public sealed class SavedPreview
    {
        public string[]? Order { get; set; }
        public string[]? Paused { get; set; }
        public bool ShowRemaining { get; set; } = true;
        public bool ShowReset { get; set; } = true;
        public bool ShowPaceMarkers { get; set; } = true;
        public double DailyUsagePercent { get; set; } = 20;
        public bool Topmost { get; set; }
        public int RefreshMinutes { get; set; } = 2;
    }
}
