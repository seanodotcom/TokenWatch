using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TokenWatch.Core;

namespace TokenWatch;

internal static class ProviderManagementSmoke
{
    internal static async Task RunAsync(MainWindow window, string reportPath, Action<string, bool> check)
    {
        // All HTTP responses and credentials below are synthetic. No provider discovery or account calls.
        foreach (var id in new[] { "copilot", "cursor" })
        {
            var clock = DateTimeOffset.UtcNow;
            var settings = new Settings();
            using var handler = new FixtureHandler(id);
            using var http = new HttpClient(handler);
            var adapter = new AdditionalProvider(id, settings, http, _ => Task.FromResult<string?>("synthetic-token"), () => clock);
            handler.Status = HttpStatusCode.TooManyRequests;
            var limited = await adapter.FetchAsync(CancellationToken.None);
            var again = await adapter.FetchAsync(CancellationToken.None);
            check(id + " persists cooldown and blocks manual checks", limited.State == ConnectionState.RateLimited && again.RetryAt == limited.RetryAt && handler.Requests == 1 &&
                (id == "copilot" ? settings.CopilotRetryNotBefore : settings.CursorRetryNotBefore) == limited.RetryAt);
            var restarted = new AdditionalProvider(id, settings, http, _ => throw new InvalidOperationException("Credentials must not be read during cooldown"), () => clock);
            check(id + " cooldown survives restart before credential access", (await restarted.FetchAsync(CancellationToken.None)).State == ConnectionState.RateLimited && handler.Requests == 1);
            clock = clock.AddHours(2); handler.Status = HttpStatusCode.OK;
            var good = await adapter.FetchAsync(CancellationToken.None);
            check(id + " sends an authenticated usage-only request", handler.ValidRequest && good.State == ConnectionState.Connected && good.Windows[0].UsedPercent == 25);
            handler.Status = HttpStatusCode.Unauthorized;
            check(id + " rejected credentials require sign-in", (await adapter.FetchAsync(CancellationToken.None)).State == ConnectionState.SignInRequired);
            handler.Status = HttpStatusCode.OK; handler.Payload = "{}";
            check(id + " unsupported schema is unavailable", (await adapter.FetchAsync(CancellationToken.None)).State == ConnectionState.Unavailable);
            var missing = new AdditionalProvider(id, settings, http, _ => Task.FromResult<string?>(null));
            var requests = handler.Requests;
            check(id + " missing credentials never contact the provider", (await missing.FetchAsync(CancellationToken.None)).State == ConnectionState.SignInRequired && handler.Requests == requests);
        }
        var databasePath = Path.ChangeExtension(reportPath, ".cursor-fixture.db");
        if (File.Exists(databasePath)) File.Delete(databasePath);
        IntPtr database = IntPtr.Zero;
        try
        {
            var result = sqlite3_open_v2(databasePath, out database, 6, IntPtr.Zero);
            if (result == 0) result = sqlite3_exec(database, "CREATE TABLE ItemTable(key TEXT PRIMARY KEY, value TEXT); INSERT INTO ItemTable VALUES('cursorAuth/accessToken','synthetic-cursor-token'); INSERT INTO ItemTable VALUES('unrelated-secret','never-read');", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            check("Cursor synthetic credential database can be created", result == 0);
        }
        finally { if (database != IntPtr.Zero) sqlite3_close(database); }
        var before = File.ReadAllBytes(databasePath);
        check("Cursor reads only its access-token entry", LocalProviderCredentials.ReadCursorToken(databasePath) == "synthetic-cursor-token");
        check("Cursor credential database stays unchanged", File.ReadAllBytes(databasePath).SequenceEqual(before));
        check("Cursor absent sign-in is safe", LocalProviderCredentials.ReadCursorToken(databasePath + ".missing") is null);

        var originalOrder = window.Preferences.ProviderOrder.ToArray();
        var originalClaude = window.Preferences.ClaudeEnabled;
        var settingsDialog = new SettingsWindow(window.Preferences, true) { Owner = window };
        settingsDialog.Loaded += (_, _) =>
        {
            check("Provider catalog initially offers Copilot and Cursor", ((ComboBox)settingsDialog.FindName("ProviderCatalogPicker")).Items.Cast<ProviderDefinition>().Select(p => p.Id).Order().SequenceEqual(new[] { "copilot", "cursor" }));
            settingsDialog.AddProvider("copilot"); settingsDialog.AddProvider("cursor"); settingsDialog.AddProvider("cursor");
            check("Adding providers has no duplicates or premature settings changes", settingsDialog.Providers.Count == 5 && window.Preferences.ProviderOrder.SequenceEqual(originalOrder));
            settingsDialog.MoveProvider(-1);
            check("Provider move updates display order", settingsDialog.Providers[3].Id == "cursor");
            settingsDialog.FocusProvider("copilot"); settingsDialog.RemoveSelectedProvider();
            check("Removing a provider returns it to the catalog", settingsDialog.Providers.Count == 4 && ((ComboBox)settingsDialog.FindName("ProviderCatalogPicker")).Items.Cast<ProviderDefinition>().Any(p => p.Id == "copilot"));
            settingsDialog.AddProvider("copilot"); settingsDialog.Providers.First(p => p.Id == "cursor").Enabled = false;
            settingsDialog.FocusProvider("copilot"); settingsDialog.UpdateLayout();
            MainWindow.CaptureWindow(settingsDialog, Path.ChangeExtension(reportPath, ".providers.png"));
            ((Button)settingsDialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        check("Provider management saves successfully", settingsDialog.ShowDialog() == true);
        window.SavePreferences(); window.SyncProviderOrder();
        check("Provider order and paused state survive restart", Settings.Load().ProviderOrder.SequenceEqual(new[] { "claude", "codex", "antigravity", "cursor", "copilot" }) && !Settings.Load().CursorEnabled);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var list = (ItemsControl)window.FindName("ProviderList");
        check("Widget binds enabled providers in saved order", list.Items.Cast<ProviderViewModel>().Select(p => p.Id).SequenceEqual(window.Preferences.ProviderOrder.Where(window.Preferences.IsProviderEnabled)));
        var bars = Descendants<System.Windows.Controls.ProgressBar>(window).Where(p => p.IsVisible).ToArray();
        check("Enabled provider layout fits its progress bars", bars.Length == 8 && bars.All(p => p.TransformToAncestor(window).TransformBounds(new Rect(0, 0, p.ActualWidth, p.ActualHeight)).Bottom <= window.ActualHeight + 1));
        window.Capture(Path.ChangeExtension(reportPath, ".five-providers.png"));

        var canceled = new SettingsWindow(window.Preferences, true) { Owner = window };
        canceled.Loaded += (_, _) => { canceled.FocusProvider("cursor"); canceled.RemoveSelectedProvider(); canceled.DialogResult = false; };
        canceled.ShowDialog();
        check("Cancel discards provider removals", window.Preferences.ProviderOrder.Contains("cursor"));
        window.Preferences.ProviderOrder = []; window.SyncProviderOrder();
        check("Empty provider list shows onboarding", list.Items.Count == 0 && ((TextBlock)window.FindName("EmptyWidgetText")).Visibility == Visibility.Visible);
        window.Preferences.ProviderOrder = originalOrder; window.Preferences.ClaudeEnabled = originalClaude;
        window.SavePreferences(); window.SyncProviderOrder();
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class FixtureHandler(string id) : HttpMessageHandler
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal int Requests { get; private set; }
        internal bool ValidRequest { get; private set; }
        internal string Payload { get; set; } = id == "copilot" ? "{\"quota_snapshots\":{\"premium_interactions\":{\"percent_remaining\":75}}}" : "{\"planUsage\":{\"limit\":100,\"includedSpend\":25}}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            ValidRequest = request.Headers.Authorization?.Parameter == "synthetic-token" && (id == "copilot"
                ? request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == "https://api.github.com/copilot_internal/user"
                : request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage" && await request.Content!.ReadAsStringAsync(cancellationToken) == "{}");
            var response = new HttpResponseMessage(Status) { Content = new StringContent(Payload) };
            if (Status == HttpStatusCode.TooManyRequests) response.Headers.Add("Retry-After", "3600");
            return response;
        }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr database);
}
