using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TokenWatch;

// Shares Claude Code's credential store and both its current and legacy refresh locks.
// No prompts, model calls, or separate copies of the user's credentials.
internal sealed class ClaudeSession(HttpClient http, Func<DateTimeOffset> now)
{
    private DateTimeOffset _retryAt;
    private string? _rejectedToken;
    internal bool WasRejected(string? refreshToken) => refreshToken is not null && refreshToken == _rejectedToken;
    internal static bool NeedsRefresh(JsonElement oauth, DateTimeOffset now) =>
        oauth.ValueKind == JsonValueKind.Object && oauth.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out var milliseconds) &&
        DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) <= now.AddMinutes(5);

    internal async Task<bool> RenewAsync(string path, CancellationToken cancellationToken, bool force = false)
    {
        if (now() < _retryAt) return false;
        // A network failure must not turn every manual refresh into another token exchange.
        _retryAt = now().AddMinutes(1);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        using var refreshLock = RefreshLock.TryAcquire(directory);
        if (refreshLock is null) return false;
        var original = await File.ReadAllTextAsync(path, cancellationToken);
        var root = JsonNode.Parse(original)!.AsObject();
        if (root["claudeAiOauth"] is not JsonObject oauth) return false;
        using (var document = JsonDocument.Parse(oauth.ToJsonString()))
            if (!force && !NeedsRefresh(document.RootElement, now())) return true;
        var refreshToken = oauth["refreshToken"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken == _rejectedToken) return false;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://platform.claude.com/v1/oauth/token");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            grant_type = "refresh_token", refresh_token = refreshToken,
            client_id = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
        }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _rejectedToken = refreshToken;
            return false;
        }
        if ((int)response.StatusCode == 429)
        {
            _retryAt = new TokenWatch.Core.RetryGate().RecordRateLimit(now(), response.Headers.TryGetValues("Retry-After", out var values)
                ? System.Linq.Enumerable.FirstOrDefault(values) : null);
            return false;
        }
        if (!response.IsSuccessStatusCode) return false;
        // Finish saving a successful rotation even if the caller cancels after the exchange.
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
        var data = result.RootElement;
        if (!data.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(access.GetString()) || !data.TryGetProperty("expires_in", out var expires) ||
            !expires.TryGetInt64(out var seconds) || seconds <= 0) return false;
        oauth["accessToken"] = access.GetString();
        oauth["expiresAt"] = now().AddSeconds(seconds).ToUnixTimeMilliseconds();
        if (data.TryGetProperty("refresh_token", out var rotated) && rotated.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(rotated.GetString())) oauth["refreshToken"] = rotated.GetString();
        if (data.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String)
            oauth["scopes"] = JsonSerializer.SerializeToNode(scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // Interactive login may replace credentials without taking the refresh lock.
        if (await File.ReadAllTextAsync(path) != original) return false;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, root.ToJsonString());
            File.Replace(temporary, path, null); // Atomic replacement preserves the existing file ACL.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        _retryAt = default;
        _rejectedToken = null;
        return true;
    }

    private sealed class RefreshLock : IDisposable
    {
        private readonly string[] _paths;
        private readonly Timer _heartbeat;
        private RefreshLock(string[] paths)
        {
            _paths = paths;
            _heartbeat = new Timer(_ =>
            {
                foreach (var path in _paths)
                    try { Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

        internal static RefreshLock? TryAcquire(string directory)
        {
            string[] paths = [Path.Combine(directory, ".oauth_refresh.lock"), directory + ".lock"];
            if (!TryCreate(paths[0])) return null;
            if (TryCreate(paths[1])) return new(paths);
            Directory.Delete(paths[0]);
            return null;
        }

        private static bool TryCreate(string path)
        {
            if (CreateDirectoryW(path, IntPtr.Zero)) return true;
            // A live holder updates mtime every two seconds. Only recover empty,
            // non-reparse lock directories with no heartbeat for ten minutes.
            // Never recursively remove a lock or touch unexpected owner files.
            try
            {
                var info = new DirectoryInfo(path);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-10) ||
                    System.Linq.Enumerable.Any(info.EnumerateFileSystemInfos())) return false;
                info.Refresh();
                if (info.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-10)) return false;
                Directory.Delete(path, recursive: false);
                return CreateDirectoryW(path, IntPtr.Zero);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        }

        public void Dispose()
        {
            using var finished = new ManualResetEvent(false);
            if (_heartbeat.Dispose(finished)) finished.WaitOne();
            foreach (var path in _paths)
                try { Directory.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }
}
