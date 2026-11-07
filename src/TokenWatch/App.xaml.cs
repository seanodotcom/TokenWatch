using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TokenWatch.Core;

namespace TokenWatch;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _show;
    private readonly CancellationTokenSource _shutdown = new();
    private NativeTrayIcon? _tray;
    private bool _menuOpen;
    private MainWindow? _widget;
    private bool _handlingError;
    private readonly DispatcherTimer _trayRetry = new() { Interval = TimeSpan.FromSeconds(5) };

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (_handlingError) { Shutdown(1); return; }
            _handlingError = true;
            if (e.Args.Contains("--demo"))
            {
                // Demo mode never contacts accounts; developer diagnostics are safe to capture here.
                var capture = Array.IndexOf(e.Args, "--capture");
                if (capture >= 0 && capture + 1 < e.Args.Length)
                    File.WriteAllText(e.Args[capture + 1] + ".error.txt", args.Exception.ToString());
            }
            else MessageBox.Show("TokenWatch encountered an unexpected error. Please restart the utility.", "TokenWatch", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
        };
        var settingsIndex = Array.IndexOf(e.Args, "--settings");
        Settings.FilePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            e.Args.Contains("--demo") ? "TokenWatch.demo.settings.json" : "TokenWatch.settings.json");
        if (settingsIndex >= 0 && settingsIndex + 1 < e.Args.Length) Settings.FilePath = Path.GetFullPath(e.Args[settingsIndex + 1]);
        var probeIndex = Array.IndexOf(e.Args, "--probe");
        if (probeIndex >= 0 && probeIndex + 1 < e.Args.Length)
        {
            await ProbeAsync(e.Args[probeIndex + 1]); Shutdown(); return;
        }
        var isolated = e.Args.Contains("--isolated");
        if (!isolated)
        {
            const string instanceName = "Local\\TokenWatch.Widget.v1";
            _instance = new Mutex(true, instanceName, out var first);
            if (!first)
            {
                try { using var signal = EventWaitHandle.OpenExisting(instanceName + ".Show"); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            _show = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Show");
            _ = Task.Run(() =>
            {
                while (WaitHandle.WaitAny([_show, _shutdown.Token.WaitHandle]) == 0)
                    Dispatcher.BeginInvoke(() => _widget?.ShowWidget());
            });
        }
        _widget = new MainWindow(Settings.Load(migrate: !isolated && !e.Args.Contains("--demo")), e.Args.Contains("--demo"));
        MainWindow = _widget;
        CreateTray();
        _widget.Show();
        var smokeIndex = Array.IndexOf(e.Args, "--ui-smoke");
        if (smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length && e.Args.Contains("--demo") && settingsIndex >= 0)
        {
            var path = Path.GetFullPath(e.Args[smokeIndex + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var passed = false;
            try { passed = await UiSmoke.RunAsync(_widget, path); }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Passed = 0, Failed = 1, Checks = new[] { new { Name = "Unexpected offline smoke exception", Passed = false, Error = error.ToString() } } }, new JsonSerializerOptions { WriteIndented = true }));
            }
            ShutdownApp(passed ? 0 : 1); return;
        }
        var captureIndex = Array.IndexOf(e.Args, "--capture");
        if (captureIndex >= 0 && captureIndex + 1 < e.Args.Length)
        {
            await Task.Delay(900);
            _widget.Capture(Path.GetFullPath(e.Args[captureIndex + 1]));
            ShutdownApp();
        }
    }

    private void CreateTray()
    {
        _tray = new NativeTrayIcon(_widget!.Demo ? "TokenWatch · Sample preview" : $"TokenWatch {AppVersion.Display} · AI usage");
        _tray.Activated += () => _widget.ShowWidget();
        _tray.ContextRequested += ShowTrayMenu;
        _widget.SetTrayAvailable(_tray.IsRegistered);
        _trayRetry.Tick += (_, _) => { _tray?.RetryRegistration(); _widget?.SetTrayAvailable(_tray?.IsRegistered == true); };
        _trayRetry.Start();
    }

    internal NativeTrayMenu BuildTrayMenu() => new(_widget!.Preferences.Pinned, _widget.Preferences.AlwaysOnTop, !_widget.Demo);
    internal bool HasNativeTrayHost => _tray is not null && _tray.Handle != IntPtr.Zero;
    internal IntPtr NativeTrayHandle => _tray?.Handle ?? IntPtr.Zero;
    internal bool HasRegisteredTrayIcon => _tray?.IsRegistered == true;
    internal bool HasKeyboardTraySupport => _tray?.UsesVersion4 == true;

    private void ShowTrayMenu(int x, int y)
    {
        if (_tray is null || _menuOpen) return;
        _menuOpen = true;
        try
        {
            TrayCommand command;
            using (var menu = BuildTrayMenu()) command = menu.Show(_tray.Handle, x, y);
            _tray.RestoreFocus();
            ExecuteTrayCommand(command);
        }
        finally { _menuOpen = false; }
    }

    internal void ExecuteTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Show: _widget!.ShowWidget(); break;
            case TrayCommand.Pin: _widget!.TogglePin(); break;
            case TrayCommand.Topmost: _widget!.ToggleTopmost(); break;
            case TrayCommand.Refresh: _ = _widget!.RefreshAsync(); break;
            case TrayCommand.Settings: _widget!.ShowWidget(); _widget.OpenSettings(); break;
            case TrayCommand.Exit: ShutdownApp(); break;
        }
    }

    private void ShutdownApp(int exitCode = 0)
    {
        _shutdown.Cancel(); _tray?.Dispose(); _tray = null; _widget?.Exit();
        _trayRetry.Stop(); Shutdown(exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _widget?.StopMonitoring();
        _trayRetry.Stop();
        _shutdown.Cancel(); _tray?.Dispose(); _show?.Dispose(); _instance?.Dispose();
        base.OnExit(e);
    }

    private static async Task ProbeAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var settings = Settings.Load();
        IUsageProvider[] adapters = [new ClaudeProvider(readRetry: () => settings.ClaudeRetryNotBefore), new CodexProvider(() => settings.CodexExecutable), new AntigravityProvider(), new AdditionalProvider("copilot", settings), new AdditionalProvider("cursor", settings)];
        var results = await Task.WhenAll(adapters.Select(async provider =>
        {
            try
            {
                var snapshot = settings.IsProviderEnabled(provider.Id) ? await provider.FetchAsync(timeout.Token)
                    : UsageSnapshot.Empty(provider.Id, ConnectionState.Disabled, "Not added or paused in Settings.");
                var visible = provider.Id == "antigravity" ? AntigravityPresentation.VisibleWindows(snapshot.Windows) : snapshot.Windows;
                return new { Provider = provider.Id, State = snapshot.State.ToString(), snapshot.Message,
                    Windows = visible.Select(w => w.Label).ToArray(), KnownWindowCount = visible.Count(w => w.UsedPercent is not null),
                    Groups = visible.Select(w => w.Group).Where(g => g is not null).Select(g => g!).Distinct().ToArray() };
            }
            catch (Exception) { return new { Provider = provider.Id, State = "Unavailable", Message = "Usage check failed or timed out.", Windows = Array.Empty<string>(), KnownWindowCount = 0, Groups = Array.Empty<string>() }; }
        }));
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Deliberately excludes percentages, account identifiers, credentials, and raw responses.
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
