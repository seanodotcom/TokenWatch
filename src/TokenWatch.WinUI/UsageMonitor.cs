using Microsoft.UI.Xaml;
using TokenWatch.Core;

namespace TokenWatch.WinUI;

internal sealed class UsageMonitor : IDisposable
{
    private readonly PreviewSession _session;
    private readonly Dictionary<string, TokenWatch.IUsageProvider> _adapters;
    private readonly Dictionary<string, SemaphoreSlim> _gates = ProviderCatalog.All.ToDictionary(p => p.Id, _ => new SemaphoreSlim(1, 1));
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshing;
    private bool _pending;
    private string _connectionKey;
    private int _interval;
    internal DateTimeOffset NextRefresh { get; private set; }
    internal DateTimeOffset? LastAttempt { get; private set; }
    internal bool IsRefreshing => _refreshing;
    internal UsageMonitor(PreviewSession session, IEnumerable<TokenWatch.IUsageProvider>? adapters = null)
    {
        _session = session;
        _connectionKey = ConnectionKey(); _interval = session.RefreshMinutes;
        var settings = session.Backend;
        _adapters = (adapters ?? [new TokenWatch.ClaudeProvider(readRetry: () => settings.ClaudeRetryNotBefore,
            saveRetry: value => { settings.ClaudeRetryNotBefore = value; settings.Save(); }), new TokenWatch.CodexProvider(() => settings.CodexExecutable),
            new TokenWatch.AntigravityProvider(), new TokenWatch.AdditionalProvider("copilot", settings), new TokenWatch.AdditionalProvider("cursor", settings)]).ToDictionary(p => p.Id);
        NextRefresh = DateTimeOffset.UtcNow.AddMinutes(session.RefreshMinutes);
        _timer.Tick += async (_, _) => { if (DateTimeOffset.UtcNow >= NextRefresh) await RefreshAsync(); };
        session.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != "Preferences" || session.IsDemo) return;
            if (_interval != session.RefreshMinutes) { _interval = session.RefreshMinutes; NextRefresh = DateTimeOffset.UtcNow.AddMinutes(_interval); }
            var key = ConnectionKey();
            if (key == _connectionKey) return;
            _connectionKey = key;
            if (_timer.IsEnabled) _ = RefreshAsync();
        };
    }
    private string ConnectionKey() => string.Join(",", _session.Providers.Select(p => p.Id + ":" + p.Enabled)) + "|" + _session.CodexExecutable;
    internal void Start() { if (_session.IsDemo) return; _timer.Start(); _ = RefreshAsync(); }
    internal async Task RefreshAsync()
    {
        if (_session.IsDemo || _shutdown.IsCancellationRequested) return;
        if (_refreshing) { _pending = true; return; }
        _refreshing = true; LastAttempt = DateTimeOffset.UtcNow; NextRefresh = LastAttempt.Value.AddMinutes(_session.RefreshMinutes);
        _session.NotifyUsage();
        try { await Task.WhenAll(_session.Providers.Where(p => p.Enabled).Select(p => CheckAsync(p.Id))); }
        finally
        {
            _refreshing = false; _session.NotifyUsage();
            if (_pending) { _pending = false; _ = RefreshAsync(); }
        }
    }
    internal async Task<UsageSnapshot> CheckAsync(string id)
    {
        if (_session.IsDemo) throw new InvalidOperationException("Sample mode cannot query account providers.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var gate = _gates[id];
        UsageSnapshot snapshot;
        var entered = false;
        try
        {
            await gate.WaitAsync(timeout.Token); entered = true;
            snapshot = await _adapters[id].FetchAsync(timeout.Token);
        }
        catch (OperationCanceledException) { snapshot = UsageSnapshot.Empty(id, ConnectionState.Unavailable, "Usage check timed out. Retrying on the next refresh."); }
        catch (Exception) { snapshot = UsageSnapshot.Empty(id, ConnectionState.Unavailable, "Usage is unavailable. Check sign-in and retry."); }
        finally { if (entered) gate.Release(); }
        if (!_shutdown.IsCancellationRequested && _session.Providers.FirstOrDefault(p => p.Id == id) is { } target)
        { target.SetSnapshot(snapshot.PreserveLastGood(target.Snapshot)); _session.NotifyUsage(); }
        return snapshot;
    }
    public void Dispose() { _timer.Stop(); _shutdown.Cancel(); }
}
