namespace TokenWatch.Core;

/// <summary>A planning guide for a seven-day quota, distributed over local weekdays.</summary>
public sealed record WeeklyPace(double ExpectedUsedPercent, double FullDayBudgetPercent)
{
    public double DisplayPercent(bool showRemaining) => showRemaining ? 100 - ExpectedUsedPercent : ExpectedUsedPercent;

    public string Explanation(bool showRemaining) =>
        $"White tick: expected by today's close — {DisplayPercent(showRemaining):0.#}% {(showRemaining ? "remaining" : "used")}.\n"
        + $"Budget: {FullDayBudgetPercent:0.#}% per full weekday; weekends excluded. Based on the weekly reset, with partial days prorated.";

    public static double NormalizeDailyPercent(double value) => double.IsFinite(value) ? Math.Clamp(value, 1, 100) : 20;

    public static WeeklyPace? Calculate(QuotaWindow quota, DateTimeOffset now, TimeZoneInfo? timeZone = null, double dailyPercent = 20)
    {
        if (!string.Equals(quota.Label, "Weekly", StringComparison.OrdinalIgnoreCase)
            || quota.UsedPercent is not double used || !double.IsFinite(used)
            || quota.ResetsAt is not { } reset || reset <= now || now < reset.AddDays(-7)) return null;

        timeZone ??= TimeZoneInfo.Local;
        var start = TimeZoneInfo.ConvertTime(reset.AddDays(-7), timeZone).DateTime;
        var end = TimeZoneInfo.ConvertTime(reset, timeZone).DateTime;
        var todayClose = TimeZoneInfo.ConvertTime(now, timeZone).Date.AddDays(1);
        var target = todayClose < end ? todayClose : end;
        dailyPercent = NormalizeDailyPercent(dailyPercent);
        return new(Math.Clamp(dailyPercent * BusinessHours(start, target) / 24, 0, 100), dailyPercent);
    }

    private static double BusinessHours(DateTime start, DateTime end)
    {
        double hours = 0;
        for (var day = start.Date; day < end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var from = day > start ? day : start;
            var to = day.AddDays(1) < end ? day.AddDays(1) : end;
            if (to > from) hours += (to - from).TotalHours;
        }
        return hours;
    }
}
