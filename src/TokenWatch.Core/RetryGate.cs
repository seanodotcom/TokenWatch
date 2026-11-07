using System.Globalization;

namespace TokenWatch.Core;

/// <summary>Honors server retry deadlines for both automatic and manual refreshes.</summary>
public sealed class RetryGate(DateTimeOffset? notBefore = null)
{
    public DateTimeOffset? NotBefore { get; private set; } = notBefore;
    private int _failures;
    public bool IsBlocked(DateTimeOffset now) => NotBefore is { } until && until > now;

    public DateTimeOffset RecordRateLimit(DateTimeOffset now, string? retryAfter)
    {
        _failures = Math.Min(_failures + 1, 5);
        var fallback = now.AddMinutes(Math.Min(60, 5 * Math.Pow(2, _failures - 1)));
        var deadline = fallback;
        if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0)
            deadline = now.AddSeconds(Math.Min(seconds, (DateTimeOffset.MaxValue - now).TotalSeconds - 1));
        else if (DateTimeOffset.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date > now)
            deadline = date;
        if (deadline < now.AddMinutes(1)) deadline = now.AddMinutes(1);
        // A repeated response must never shorten a deadline already supplied by the server.
        NotBefore = NotBefore is { } previous && previous > deadline ? previous : deadline;
        return NotBefore.Value;
    }

    public void RecordSuccess() { NotBefore = null; _failures = 0; }
}
