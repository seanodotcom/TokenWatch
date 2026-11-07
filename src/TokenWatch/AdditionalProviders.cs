using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TokenWatch.Core;

namespace TokenWatch;

internal sealed class AdditionalProvider : IUsageProvider
{
    public string Id { get; }
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string?>> _credential;
    private readonly Settings _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly RetryGate _retry;
    private readonly SemaphoreSlim _fetch = new(1, 1);
    internal AdditionalProvider(string id, Settings settings, HttpClient? http = null,
        Func<CancellationToken, Task<string?>>? credential = null, Func<DateTimeOffset>? now = null)
    {
        Id = id; _settings = settings;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _credential = credential ?? (id == "copilot" ? LocalProviderCredentials.CopilotAsync : LocalProviderCredentials.CursorAsync);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _retry = new(id == "copilot" ? settings.CopilotRetryNotBefore : settings.CursorRetryNotBefore);
    }
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        await _fetch.WaitAsync(cancellationToken);
        try { return await FetchCoreAsync(cancellationToken); }
        finally { _fetch.Release(); }
    }
    private async Task<UsageSnapshot> FetchCoreAsync(CancellationToken cancellationToken)
    {
        if (_retry.IsBlocked(_now())) return Cooldown();
        try
        {
            var token = await _credential(cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired,
                Id == "copilot" ? "Sign in with GitHub CLI (gh auth login), then refresh. Connection details are in Settings." : "Sign in in Cursor Desktop, then refresh. Connection details are in Settings.");
            using var request = new HttpRequestMessage(Id == "copilot" ? HttpMethod.Get : HttpMethod.Post,
                Id == "copilot" ? "https://api.github.com/copilot_internal/user" : "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("TokenWatch/" + AppVersion.Display.TrimStart('v'));
            request.Headers.Accept.ParseAdd("application/json");
            if (Id == "copilot")
            {
                request.Headers.Add("Editor-Version", "vscode/1.96.2");
                request.Headers.Add("Editor-Plugin-Version", "copilot-chat/0.26.7");
            }
            else
            {
                request.Headers.Add("Connect-Protocol-Version", "1");
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }
            using var response = await _http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 429)
            {
                _retry.RecordRateLimit(_now(), response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null);
                SaveRetry(); return Cooldown();
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, Id == "copilot"
                    ? "GitHub did not accept this sign-in for Copilot usage. Check your Copilot subscription and sign in again with GitHub CLI. Some account policies may restrict access."
                    : "Cursor sign-in expired or access was denied. Open Cursor Desktop and sign in again, then refresh.");
            if (!response.IsSuccessStatusCode) return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "The provider usage service is unavailable. Retrying on the next refresh.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var snapshot = Id == "copilot" ? AdditionalUsageParser.Copilot(json.RootElement, _now()) : AdditionalUsageParser.Cursor(json.RootElement, _now());
            _retry.RecordSuccess(); SaveRetry();
            return snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Usage request timed out. Retrying on the next refresh."); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or FormatException)
        { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Cannot read usage. Check the provider sign-in in Settings and retry."); }
    }
    private UsageSnapshot Cooldown() => UsageSnapshot.Empty(Id, ConnectionState.RateLimited, "The provider has limited usage checks. Waiting until the allowed retry time.") with { RetryAt = _retry.NotBefore };
    private void SaveRetry()
    {
        if (Id == "copilot") _settings.CopilotRetryNotBefore = _retry.NotBefore; else _settings.CursorRetryNotBefore = _retry.NotBefore;
        _settings.Save();
    }
}

internal static class LocalProviderCredentials
{
    // Read only the explicit Copilot token variable, legacy Copilot OAuth files, or GitHub CLI's selected github.com account.
    // No arbitrary credential enumeration, shell commands, browser cookies or credential copies.
    internal static async Task<string?> CopilotAsync(CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var paths = new[] { Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg, "github-copilot", "apps.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot", "apps.json") };
        foreach (var path in paths.Distinct())
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                if (json.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var entry in json.RootElement.EnumerateObject())
                    if ((entry.Name == "github.com" || entry.Name.StartsWith("github.com:", StringComparison.OrdinalIgnoreCase)) && entry.Value.ValueKind == JsonValueKind.Object &&
                        entry.Value.TryGetProperty("oauth_token", out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(token.GetString())) return token.GetString();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        var executable = FindGitHubCli();
        if (executable is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process { StartInfo = new(executable, "auth token --hostname github.com")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        try
        {
            process.Start();
            using var stop = timeout.Token.Register(() => CodexProvider.StopOwnedProcess(process));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var token = (await output).Trim(); await errors;
            return process.ExitCode == 0 && token.Length > 0 ? token : null;
        }
        finally { CodexProvider.StopOwnedProcess(process); }
    }
    internal static string? FindGitHubCli()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p.Trim('"'), "gh.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
    internal static Task<string?> CursorAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");
        return ReadCursorToken(path);
    }, cancellationToken);

    internal static string? ReadCursorToken(string path)
    {
        if (!File.Exists(path)) return null;
        IntPtr database = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            // SQLITE_OPEN_READONLY; Windows' bundled SQLite reads the live WAL without copying or modifying credentials.
            if (sqlite3_open_v2(path, out database, 1, IntPtr.Zero) != 0) return null;
            sqlite3_busy_timeout(database, 500);
            if (sqlite3_prepare_v2(database, "SELECT value FROM ItemTable WHERE key = 'cursorAuth/accessToken' LIMIT 1", -1, out statement, IntPtr.Zero) != 0) return null;
            return sqlite3_step(statement) == 100 ? Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0)) : null;
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (database != IntPtr.Zero) sqlite3_close(database);
        }
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr database);
}
