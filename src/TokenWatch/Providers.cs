using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TokenWatch.Core;

namespace TokenWatch;

internal interface IUsageProvider
{
    string Id { get; }
    Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken);
}

internal sealed class ClaudeProvider : IUsageProvider
{
    public string Id => "claude";
    private readonly HttpClient _http;
    private readonly Func<string> _credentialPath;
    private readonly Func<DateTimeOffset> _now;
    private readonly RetryGate _retry;
    private readonly Action<DateTimeOffset?>? _saveRetry;
    private readonly ClaudeSession _session;

    internal ClaudeProvider(HttpClient? http = null, Func<string>? credentialPath = null, Func<DateTimeOffset>? now = null,
        Func<DateTimeOffset?>? readRetry = null, Action<DateTimeOffset?>? saveRetry = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _credentialPath = credentialPath ?? DefaultCredentialPath;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _session = new(_http, _now);
        _retry = new(readRetry?.Invoke());
        _saveRetry = saveRetry;
    }

    internal static string DefaultCredentialPath()
    {
        var config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return Path.Combine(string.IsNullOrWhiteSpace(config) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude") : config, ".credentials.json");
    }

    private UsageSnapshot Cooldown(DateTimeOffset until) => UsageSnapshot.Empty(Id, ConnectionState.RateLimited,
        $"Claude usage is rate-limited until {until.LocalDateTime:t}. Waiting before the next request.") with { RetryAt = until };

    public Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken) => FetchAsync(cancellationToken, false);

    private async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken, bool retried)
    {
        var path = _credentialPath();
        if (!File.Exists(path)) return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Sign in to Claude Code to connect.");
        try
        {
            if (_retry.IsBlocked(_now())) return Cooldown(_retry.NotBefore!.Value);
            using (var before = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken)))
            {
                if (before.RootElement.TryGetProperty("claudeAiOauth", out var stored) && ClaudeSession.NeedsRefresh(stored, _now()))
                    await _session.RenewAsync(path, cancellationToken);
            }
            using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            if (!credentials.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var access) || access.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(access.GetString()))
                return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Sign in to Claude Code with a subscription.");
            if (oauth.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out var milliseconds) &&
                DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) <= _now().AddSeconds(30))
            {
                var refreshToken = oauth.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String ? refresh.GetString() : null;
                return string.IsNullOrWhiteSpace(refreshToken) || _session.WasRejected(refreshToken)
                    ? UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Claude sign-in needs renewal. Use Sign in to Claude in Settings.")
                    : UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Claude session renewal is temporarily unavailable. TokenWatch will retry automatically.");
            }
            if (_retry.IsBlocked(_now())) return Cooldown(_retry.NotBefore!.Value);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.GetString());
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("TokenWatch/" + AppVersion.Number);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (!retried && await _session.RenewAsync(path, cancellationToken, force: true))
                    return await FetchAsync(cancellationToken, true);
                var refreshToken = oauth.TryGetProperty("refreshToken", out var refresh) && refresh.ValueKind == JsonValueKind.String ? refresh.GetString() : null;
                if (!retried && !string.IsNullOrWhiteSpace(refreshToken) && !_session.WasRejected(refreshToken))
                    return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Claude session renewal is temporarily unavailable. TokenWatch will retry automatically.");
                return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Claude sign-in is not valid for usage. Use Sign in to Claude in Settings.");
            }
            if ((int)response.StatusCode == 429)
            {
                var header = response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
                var until = _retry.RecordRateLimit(_now(), header);
                _saveRetry?.Invoke(until);
                return Cooldown(until);
            }
            if (!response.IsSuccessStatusCode) return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Claude usage is temporarily unavailable.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var snapshot = UsageParser.Claude(json.RootElement, _now());
            if (snapshot.State == ConnectionState.Connected) { _retry.RecordSuccess(); _saveRetry?.Invoke(null); }
            return snapshot;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Claude usage request timed out."); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or ArgumentOutOfRangeException)
        { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Cannot read Claude usage. Check your connection and Claude Code sign-in."); }
    }
}

internal sealed class CodexProvider(Func<string?> executableOverride) : IUsageProvider
{
    public string Id => "codex";
    public static string? FindExecutable(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>();
        var desktop = Path.Combine(local, "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktop)) candidates.AddRange(Directory.GetDirectories(desktop).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d => Path.Combine(d, "codex.exe")));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(directory)) candidates.Add(Path.Combine(directory.Trim('"'), "codex.exe"));
        }
        var extensions = Path.Combine(home, ".vscode", "extensions");
        if (Directory.Exists(extensions)) candidates.AddRange(Directory.GetDirectories(extensions, "openai.chatgpt-*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe")));
        candidates.Add(Path.Combine(home, ".local", "bin", "codex.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var executable = FindExecutable(executableOverride());
        if (executable is null) return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Install or sign in to Codex. A custom executable can be selected in Settings.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var process = new Process { StartInfo = new ProcessStartInfo(executable, "app-server")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)! } };
        try
        {
            process.Start();
            var discardErrors = process.StandardError.ReadToEndAsync(timeout.Token);
            using var killRegistration = timeout.Token.Register(() => StopOwnedProcess(process));
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "tokenwatch", title = "TokenWatch", version = AppVersion.Number } } }));
            var initialized = await ReadReplyAsync(process, 1, timeout.Token);
            if (initialized.TryGetProperty("error", out _)) return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Codex initialization failed. Update the Codex app or CLI.");
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            var reply = await ReadReplyAsync(process, 2, timeout.Token);
            if (reply.TryGetProperty("error", out _)) return UsageSnapshot.Empty(Id, ConnectionState.SignInRequired, "Sign in to Codex with your ChatGPT subscription, then refresh.");
            return reply.TryGetProperty("result", out var result) ? UsageParser.Codex(result, DateTimeOffset.UtcNow)
                : UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Codex returned no usage data.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Codex usage request timed out."); }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Cannot connect to Codex. Check its sign-in or executable in Settings."); }
        finally { StopOwnedProcess(process); }
    }

    private static async Task<JsonElement> ReadReplyAsync(Process process, int id, CancellationToken cancellationToken)
    {
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || line.Length > 2_000_000) continue;
            try
            {
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n == id)
                    return json.RootElement.Clone();
            }
            catch (JsonException) { /* Ignore startup banners and non-protocol output. */ }
        }
        throw new IOException("Codex closed its connection.");
    }

    internal static void StopOwnedProcess(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
}

internal sealed class AntigravityProvider : IUsageProvider
{
    public string Id => "antigravity";
    private sealed record LocalServer(int ProcessId, string Csrf, int? ExtensionPort);

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var servers = await Task.Run(FindServers, cancellationToken);
            if (servers.Count == 0) return UsageSnapshot.Empty(Id, ConnectionState.NotRunning, "Open Antigravity and a workspace to connect.");
            var listeners = await ListeningPortsAsync(cancellationToken);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false,
                // Antigravity's local language server uses its own certificate. No remote requests use this handler.
                ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri?.IsLoopback == true };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            foreach (var server in servers)
            {
                var ports = listeners.Where(p => p.Pid == server.ProcessId).Select(p => p.Port).Distinct().Take(8).ToList();
                // Only ports actually owned by the language server are queried; extension ports can belong to other apps.
                foreach (var port in ports)
                foreach (var scheme in new[] { "https", "http" })
                {
                    foreach (var method in new[] { "RetrieveUserQuotaSummary", "GetUserStatus" })
                    {
                        try
                        {
                            using var request = new HttpRequestMessage(HttpMethod.Post,
                                $"{scheme}://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/{method}");
                            request.Headers.Add("X-Codeium-Csrf-Token", server.Csrf);
                            request.Headers.Add("Connect-Protocol-Version", "1");
                            request.Content = new StringContent(method == "RetrieveUserQuotaSummary" ? "{\"forceRefresh\":true}" :
                                "{\"metadata\":{\"ideName\":\"antigravity\",\"extensionName\":\"antigravity\",\"ideVersion\":\"unknown\",\"locale\":\"en\"}}", Encoding.UTF8, "application/json");
                            using var response = await http.SendAsync(request, cancellationToken);
                            if (!response.IsSuccessStatusCode) continue;
                            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                            var snapshot = UsageParser.Antigravity(json.RootElement, DateTimeOffset.UtcNow);
                            if (snapshot.State == ConnectionState.Connected) return snapshot;
                        }
                        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException) { }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    }
                }
            }
            return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Antigravity is running but its quota service is unavailable. Open a workspace and retry.");
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return UsageSnapshot.Empty(Id, ConnectionState.Unavailable, "Cannot detect Antigravity. Open it under the same Windows account and retry."); }
    }

    private static List<LocalServer> FindServers()
    {
        var servers = new List<LocalServer>();
        using var search = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'language_server%'");
        search.Options.Timeout = TimeSpan.FromSeconds(5);
        using var results = search.Get();
        foreach (ManagementObject process in results)
        {
            using (process)
            {
                var command = process["CommandLine"] as string ?? "";
                if (!command.Contains("antigravity", StringComparison.OrdinalIgnoreCase)) continue;
                var csrf = Regex.Match(command, "--csrf_token(?:=|\\s+)[\"']?([^\\s\"']+)");
                if (!csrf.Success) continue;
                servers.Add(new(Convert.ToInt32(process["ProcessId"]), csrf.Groups[1].Value, null));
            }
        }
        return servers;
    }

    private static async Task<List<(int Pid, int Port)>> ListeningPortsAsync(CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netstat.exe"), "-ano -p TCP")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } };
        process.Start();
        using var registration = token.Register(() => CodexProvider.StopOwnedProcess(process));
        var output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var ports = new List<(int, int)>();
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || fields[0] != "TCP" || fields[3] != "LISTENING") continue;
            if (int.TryParse(fields[4], out var pid) && int.TryParse(fields[1][(fields[1].LastIndexOf(':') + 1)..], out var port)) ports.Add((pid, port));
        }
        return ports;
    }
}
