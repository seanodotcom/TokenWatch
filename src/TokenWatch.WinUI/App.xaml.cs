using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TokenWatch.WinUI;

public partial class App : Application
{
    internal static PreviewSession Session { get; private set; } = null!;
    internal static WidgetWindow Widget { get; private set; } = null!;
    internal static UsageMonitor Monitor { get; private set; } = null!;
    internal static TrayIcon? Tray { get; private set; }
    internal static bool IsExiting { get; private set; }
    internal static bool MenuOpen { get; private set; }
    internal static bool ManagerOpen => _manager is not null;
    private static ManagerWindow? _manager;
    private Mutex? _instance;
    private EventWaitHandle? _show;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _trayRetry = new() { Interval = TimeSpan.FromSeconds(5) };
    internal static string[] Arguments => Environment.GetCommandLineArgs();
    internal static string PortableDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            try { File.WriteAllText(Path.Combine(PortableDirectory, "TokenWatch-error.txt"), Arguments.Contains("--demo") ? args.Exception.ToString() : "TokenWatch encountered an unexpected error. Restart the app. Error type: " + args.Exception.GetType().Name); } catch { }
            Shutdown();
        };
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settingsIndex = Array.IndexOf(Arguments, "--settings");
        var demo = Arguments.Contains("--demo") || Arguments.Contains("--smoke");
        var settingsPath = settingsIndex >= 0 && settingsIndex + 1 < Arguments.Length ? Path.GetFullPath(Arguments[settingsIndex + 1])
            : Path.Combine(PortableDirectory, demo ? "TokenWatch.demo.settings.json" : "TokenWatch.settings.json");
        Session = PreviewSession.Load(settingsPath, demo);
        Monitor = new UsageMonitor(Session);
        var probeIndex = Array.IndexOf(Arguments, "--probe");
        if (probeIndex >= 0 && probeIndex + 1 < Arguments.Length)
        {
            await Monitor.RefreshAsync();
            var reportPath = Path.GetFullPath(Arguments[probeIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(Session.Providers.Select(p => new { Provider = p.Id, p.Enabled, State = p.Connection.ToString(), p.Snapshot.Message,
                KnownWindowCount = p.Quotas.Count(q => q.UsedPercent is not null), Windows = p.Quotas.Select(q => q.Label).ToArray() }), new JsonSerializerOptions { WriteIndented = true }));
            Monitor.Dispose(); Exit(); return;
        }
        if (!Arguments.Contains("--isolated") && !demo && !AcquireInstance()) { Monitor.Dispose(); Exit(); return; }
        Session.Save();
        Widget = new WidgetWindow();
        Widget.Closed += (_, _) => Shutdown();
        Widget.Activate();
        try
        {
            Tray = new TrayIcon();
            Tray.Activated += ShowWidget; Tray.ContextRequested += ShowTrayMenu;
            Widget.AppWindow.IsShownInSwitchers = !Tray.IsRegistered;
            _trayRetry.Tick += (_, _) =>
            {
                var registered = Tray?.IsRegistered == true;
                Tray?.RetryRegistration();
                if (Tray?.IsRegistered == true) Widget.AppWindow.IsShownInSwitchers = false;
                if (registered != (Tray?.IsRegistered == true)) Session.NotifyUsage();
            };
            _trayRetry.Start();
        }
        catch (System.ComponentModel.Win32Exception) { Widget.AppWindow.IsShownInSwitchers = true; }
        if (Arguments.Contains("--settings-window")) ShowManager();
        var smokeIndex = Array.IndexOf(Arguments, "--smoke");
        if (smokeIndex >= 0 && smokeIndex + 1 < Arguments.Length)
            Widget.DispatcherQueue.TryEnqueue(async () => await PreviewSmoke.RunAsync(Path.GetFullPath(Arguments[smokeIndex + 1])));
    }
    private bool AcquireInstance()
    {
        const string name = "Local\\TokenWatch.Widget.v1";
        _instance = new Mutex(true, name, out var first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting(name + ".Show"); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            _instance.Dispose(); _instance = null; return false;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        var queue = DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() => { while (WaitHandle.WaitAny([_show, _shutdown.Token.WaitHandle]) == 0) queue.TryEnqueue(ShowWidget); });
        return true;
    }
    internal static ManagerWindow ShowManager()
    {
        if (_manager is null) { _manager = new ManagerWindow(); _manager.Closed += (_, _) => _manager = null; }
        _manager.Activate(); return _manager;
    }
    internal static void ShowWidget() { if (IsExiting) return; Widget.AppWindow.Show(); Widget.Activate(); }
    internal static void HideWidget(bool force = false)
    {
        if (IsExiting) return;
        Widget.SaveGeometry();
        if (force || Tray?.IsRegistered == true) Widget.AppWindow.Hide();
        else { Widget.AppWindow.IsShownInSwitchers = true; ((OverlappedPresenter)Widget.AppWindow.Presenter).Minimize(); }
    }
    private static void ShowTrayMenu(int x, int y)
    {
        if (Tray is null || MenuOpen) return; MenuOpen = true;
        try
        {
            using var menu = new TokenWatch.NativeTrayMenu(Session.KeepOpen, Session.Topmost, !Session.IsDemo);
            ExecuteTrayCommand(menu.Show(Tray.Handle, x, y)); Tray.RestoreFocus();
        }
        finally { MenuOpen = false; }
    }
    internal static void ExecuteTrayCommand(TokenWatch.TrayCommand command)
    {
        switch (command)
        {
            case TokenWatch.TrayCommand.Show: ShowWidget(); break;
            case TokenWatch.TrayCommand.Pin: Session.KeepOpen = !Session.KeepOpen; break;
            case TokenWatch.TrayCommand.Topmost: Session.Topmost = !Session.Topmost; break;
            case TokenWatch.TrayCommand.Refresh: _ = Monitor.RefreshAsync(); break;
            case TokenWatch.TrayCommand.Settings: ShowManager(); break;
            case TokenWatch.TrayCommand.Exit: Shutdown(); break;
        }
    }
    internal static void Shutdown()
    {
        if (IsExiting) return; IsExiting = true;
        Widget?.SaveGeometry(); Widget?.PrepareShutdown(); _manager?.PrepareShutdown();
        ((App)Current)._trayRetry.Stop(); Monitor?.Dispose(); Tray?.Dispose();
        ((App)Current)._shutdown.Cancel();
        ((App)Current)._instance?.Dispose();
        Current.Exit();
    }
}
