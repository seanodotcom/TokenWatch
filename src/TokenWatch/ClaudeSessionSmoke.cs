using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TokenWatch.Core;

namespace TokenWatch;

internal static class ClaudeSessionSmoke
{
    internal static async Task RunAsync(Action<string, bool> check, string artifacts)
    {
        var directory = Path.Combine(artifacts, "claude-session-fixture");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ".credentials.json");
        var clock = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        Task WriteAsync(int minutes = -1) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            otherCredential = "preserved", claudeAiOauth = new
            {
                accessToken = "expired-fixture", refreshToken = "refresh-fixture",
                expiresAt = clock.AddMinutes(minutes).ToUnixTimeMilliseconds(),
                subscriptionType = "fixture-plan", scopes = new[] { "user:profile" }
            }
        }));
        await WriteAsync();
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        var provider = new ClaudeProvider(client, () => path, () => clock);
        var renewed = await provider.FetchAsync(CancellationToken.None);
        check("Expired Claude session renews silently before reading usage", renewed.State == ConnectionState.Connected && handler.Refreshes == 1 && handler.UsageRequests == 1 && handler.UsedRenewedToken);
        using (var stored = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            var oauth = stored.RootElement.GetProperty("claudeAiOauth");
            check("Refresh rotation persists without losing other credential fields", oauth.GetProperty("refreshToken").GetString() == "rotated-fixture" && oauth.GetProperty("subscriptionType").GetString() == "fixture-plan" && stored.RootElement.GetProperty("otherCredential").GetString() == "preserved");
        }
        var restarted = new ClaudeProvider(client, () => path, () => clock);
        await restarted.FetchAsync(CancellationToken.None);
        check("Restart reuses renewed credentials without another exchange", handler.Refreshes == 1 && handler.UsageRequests == 2);
        await WriteAsync(4);
        await restarted.FetchAsync(CancellationToken.None);
        check("Claude connection renews ahead of expiry", handler.Refreshes == 2);

        await WriteAsync();
        handler.Status = HttpStatusCode.ServiceUnavailable;
        provider = new ClaudeProvider(client, () => path, () => clock);
        var transient = await provider.FetchAsync(CancellationToken.None);
        var count = handler.Refreshes;
        await provider.FetchAsync(CancellationToken.None);
        check("Temporary renewal failure waits and does not demand sign-in", transient.State == ConnectionState.Unavailable && handler.Refreshes == count);
        clock = clock.AddMinutes(2);
        handler.Status = HttpStatusCode.OK;
        var recovered = await provider.FetchAsync(CancellationToken.None);
        check("Claude renewal retries and recovers automatically", recovered.State == ConnectionState.Connected && handler.Refreshes == count + 1);

        await WriteAsync();
        handler.Status = HttpStatusCode.BadRequest;
        provider = new ClaudeProvider(client, () => path, () => clock);
        var rejected = await provider.FetchAsync(CancellationToken.None);
        count = handler.Refreshes;
        clock = clock.AddMinutes(2);
        await provider.FetchAsync(CancellationToken.None);
        check("Rejected refresh token requires sign-in without repeated exchanges", rejected.State == ConnectionState.SignInRequired && handler.Refreshes == count);

        await WriteAsync();
        handler.Status = HttpStatusCode.OK;
        var original = await File.ReadAllTextAsync(path);
        Directory.CreateDirectory(directory + ".lock");
        try
        {
            count = handler.Refreshes;
            var blocked = await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
            check("Claude Code refresh lock prevents concurrent credential rotation", blocked.State == ConnectionState.Unavailable && handler.Refreshes == count && await File.ReadAllTextAsync(path) == original);
        }
        finally { Directory.Delete(directory + ".lock"); }
        check("Refresh releases both current and legacy locks", !Directory.Exists(Path.Combine(directory, ".oauth_refresh.lock")) && !Directory.Exists(directory + ".lock"));

        Directory.CreateDirectory(Path.Combine(directory, ".oauth_refresh.lock"));
        try
        {
            count = handler.Refreshes;
            await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
            check("Current Claude Code refresh lock is respected", handler.Refreshes == count);
        }
        finally { Directory.Delete(Path.Combine(directory, ".oauth_refresh.lock")); }

        foreach (var abandoned in new[] { Path.Combine(directory, ".oauth_refresh.lock"), directory + ".lock" })
        {
            await WriteAsync();
            Directory.CreateDirectory(abandoned); Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-1));
            var result = await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
            check("Abandoned empty lock recovers: " + Path.GetFileName(abandoned), result.State == ConnectionState.Connected && !Directory.Exists(abandoned));
        }
        await WriteAsync();
        var occupied = Path.Combine(directory, ".oauth_refresh.lock"); Directory.CreateDirectory(occupied);
        var ownerFile = Path.Combine(occupied, "foreign-owner"); await File.WriteAllTextAsync(ownerFile, "synthetic owner");
        Directory.SetLastWriteTimeUtc(occupied, DateTime.UtcNow.AddDays(-1));
        count = handler.Refreshes;
        await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
        check("Stale lock with unexpected owner files is never removed", handler.Refreshes == count && File.Exists(ownerFile));
        File.Delete(ownerFile); Directory.Delete(occupied);

        handler.Status = HttpStatusCode.TooManyRequests;
        provider = new ClaudeProvider(client, () => path, () => clock);
        await provider.FetchAsync(CancellationToken.None);
        count = handler.Refreshes;
        clock = clock.AddMinutes(2);
        await provider.FetchAsync(CancellationToken.None);
        check("OAuth renewal honors server Retry-After", handler.Refreshes == count);
        clock = clock.AddHours(1);
        handler.Status = HttpStatusCode.OK;
        check("OAuth renewal recovers after its server cooldown", (await provider.FetchAsync(CancellationToken.None)).State == ConnectionState.Connected);

        await WriteAsync();
        handler.BeforeRefreshResponse = () => File.WriteAllText(path, "{\"claudeAiOauth\":{\"accessToken\":\"new-browser-login\",\"expiresAt\":" + clock.AddHours(1).ToUnixTimeMilliseconds() + "}}");
        await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
        check("Concurrent browser login is never overwritten by renewal", (await File.ReadAllTextAsync(path)).Contains("new-browser-login"));
        handler.BeforeRefreshResponse = null;

        await WriteAsync(60);
        handler.RejectUsageOnce = true;
        var retried = await new ClaudeProvider(client, () => path, () => clock).FetchAsync(CancellationToken.None);
        check("Claude rejected access token renews and retries usage once", retried.State == ConnectionState.Connected && !handler.RejectUsageOnce);
        // Test credentials are synthetic and deliberately contain no account data.
        File.Delete(path);
        Directory.Delete(directory);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal int Refreshes;
        internal int UsageRequests;
        internal bool UsedRenewedToken;
        internal bool RejectUsageOnce;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal Action? BeforeRefreshResponse;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Refreshes++;
                BeforeRefreshResponse?.Invoke();
                var response = new HttpResponseMessage(Status)
                {
                    Content = new StringContent("{\"access_token\":\"renewed-fixture\",\"refresh_token\":\"rotated-fixture\",\"expires_in\":28800,\"scope\":\"user:profile user:inference\"}")
                };
                if (Status == HttpStatusCode.TooManyRequests) response.Headers.TryAddWithoutValidation("Retry-After", "1800");
                return Task.FromResult(response);
            }
            UsageRequests++;
            UsedRenewedToken = request.Headers.Authorization?.Parameter == "renewed-fixture";
            if (RejectUsageOnce) { RejectUsageOnce = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"five_hour\":{\"utilization\":12},\"seven_day\":{\"utilization\":34}}")
            });
        }
    }
}
