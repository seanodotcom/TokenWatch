using System.Text.Json;
using TokenWatch.Core;

var passed = 0;
var failed = 0;
var now = DateTimeOffset.Parse("2026-10-28T16:00:00Z");
void Check(string label, bool condition)
{
    if (condition) { Console.WriteLine("PASS " + label); passed++; }
    else { Console.WriteLine("FAIL " + label); failed++; }
}
JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

var claude = UsageParser.Claude(Json("""{"five_hour":{"utilization":64,"resets_at":"2026-10-28T18:14:00Z"},"seven_day":{"utilization":38,"resets_at":"2026-10-30T16:00:00Z"}}"""), now);
Check("Claude parses both windows", claude.Windows.Count == 2 && claude.Windows[0].UsedPercent == 64 && claude.Windows[1].UsedPercent == 38);
Check("Claude reset is timezone aware", claude.Windows[0].ResetsAt == now.AddHours(2).AddMinutes(14));
Check("Countdown uses current time", claude.Windows[0].ResetText(now) == "Resets in 2h 14m");
Check("Reset due retains usage rather than pretending zero", new QuotaWindow("5 hour", 90, now.AddSeconds(-1)).ResetText(now).Contains("Reset due"));
var missing = UsageParser.Claude(Json("""{"five_hour":null,"seven_day":{"utilization":0}}"""), now);
Check("Null Claude window remains unknown", missing.Windows[0].UsedPercent is null);
Check("Zero usage is known, not missing", missing.Windows[1].UsedPercent == 0 && missing.State == ConnectionState.Connected);
Check("Missing fields are unavailable", UsageParser.Claude(Json("{}"), now).State == ConnectionState.Unavailable);
Check("Wrong percent type is unavailable", UsageParser.Claude(Json("""{"five_hour":{"utilization":"unknown"}}"""), now).State == ConnectionState.Unavailable);

var codex = UsageParser.Codex(Json("""{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":52,"windowDurationMins":10080,"resetsAt":1793462400},"secondary":{"usedPercent":28,"windowDurationMins":300}}}}"""), now);
Check("Codex prefers named bucket over legacy view", codex.Windows[0].UsedPercent == 28);
Check("Codex maps weekly primary by duration", codex.Windows[1].Label == "Weekly" && codex.Windows[1].UsedPercent == 52);
var weeklyOnly = UsageParser.Codex(Json("""{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":10080},"secondary":null}}"""), now);
Check("Weekly-only Codex plan does not invent five-hour quota", weeklyOnly.Windows[0].UsedPercent is null && weeklyOnly.Windows[1].UsedPercent == 12);
var other = UsageParser.Codex(Json("""{"rateLimits":{"primary":{"usedPercent":32,"windowDurationMins":15}}}"""), now);
Check("Non-five-hour quota keeps actual duration", other.Windows[2].Label == "15 min" && other.Windows[0].UsedPercent is null);
Check("Absent Codex bucket is unavailable", UsageParser.Codex(Json("{}"), now).State == ConnectionState.Unavailable);
Check("Null Codex primary is unavailable", UsageParser.Codex(Json("""{"rateLimits":{"primary":null,"secondary":null}}"""), now).State == ConnectionState.Unavailable);

var antigravity = UsageParser.Antigravity(Json("""{"userStatus":{"cascadeModelConfigData":{"clientModelConfigs":[{"label":"Gemini Pro","modelOrAlias":{"model":"gemini-pro"},"quotaInfo":{"remainingFraction":0.17,"resetTime":"2026-10-28T17:07:00Z"}},{"label":"Claude","quotaInfo":{"remainingFraction":1}}]}}}"""), now);
Check("Antigravity converts remaining fraction to used", Math.Abs(antigravity.Windows[0].UsedPercent!.Value - 83) < .001);
Check("Legacy Antigravity does not invent time window", antigravity.Windows[0].Label == "Model quota" && antigravity.Windows[0].Group == "Gemini Pro");
Check("Antigravity keeps model groups separate", antigravity.Windows[1].Group == "Claude" && antigravity.Windows[1].UsedPercent == 0);
Check("Missing Antigravity quotas stay unavailable", UsageParser.Antigravity(Json("""{"userStatus":{"cascadeModelConfigData":{"clientModelConfigs":[{"label":"Gemini"}]}}}"""), now).State == ConnectionState.Unavailable);
var summary = UsageParser.Antigravity(Json("""{"response":{"groups":[{"displayName":"Gemini","buckets":[{"bucketId":"five_hour","displayName":"5 hour","remainingFraction":0.45,"resetTime":"2026-10-28T18:00:00Z"},{"bucketId":"weekly","displayName":"Weekly","remaining":{"case":"remainingFraction","value":0.8}},{"bucketId":"disabled","disabled":true,"remainingFraction":0.1}]}]}}"""), now);
Check("Antigravity summary parses supplied windows", summary.State == ConnectionState.Connected && summary.Windows.Count == 2);
Check("Antigravity nested fraction is supported", Math.Abs(summary.Windows[1].UsedPercent!.Value - 20) < .001);
Check("Antigravity disabled buckets are excluded", summary.Windows.All(w => w.Key != "disabled"));
Check("Antigravity summary retains group name", summary.Windows[0].Group == "Gemini");

var stale = UsageSnapshot.Empty("claude", ConnectionState.Unavailable, "Offline").PreserveLastGood(claude);
Check("Failure retains last good values and timestamp", stale.State == ConnectionState.Stale && stale.Windows[0].UsedPercent == 64 && stale.UpdatedAt == now);
Check("Stale status clearly states last known usage", stale.Message.Contains("Last known"));
var stillStale = UsageSnapshot.Empty("claude", ConnectionState.Unavailable, "Offline again").PreserveLastGood(stale);
Check("Repeated failure preserves original observation time", stillStale.UpdatedAt == now && stillStale.Windows[0].UsedPercent == 64);
Check("No successful sample means no stale percentages", UsageSnapshot.Empty("claude", ConnectionState.Unavailable, "Offline").PreserveLastGood(null).Windows.All(w => w.UsedPercent is null));
Check("Warning threshold", new QuotaWindow("5 hour", 80).IsWarning && !new QuotaWindow("5 hour", 79).IsWarning);
Check("Progress fraction clamps safely", new QuotaWindow("5 hour", 140).Fraction == 1 && new QuotaWindow("5 hour", -20).Fraction == 0);
Check("Unknown is never presented as zero percent", new QuotaWindow("Weekly", null).PercentText == "—");
Check("Minute countdown rounds up", new QuotaWindow("5 hour", 0, now.AddSeconds(20)).ResetText(now) == "Resets in 1m");
Check("44 percent used displays as 56 remaining", new QuotaWindow("5 hour", 44).DisplayText(true) == "56%");
Check("7 percent used displays as 93 remaining", new QuotaWindow("Weekly", 7).DisplayText(true) == "93%");
Check("Used display preserves provider value", new QuotaWindow("5 hour", 44).DisplayText(false) == "44%");
Check("Unknown remaining stays unknown", new QuotaWindow("Weekly", null).DisplayPercent(true) is null && new QuotaWindow("Weekly", null).DisplayText(true) == "—");
Check("Unused quota has all capacity remaining", new QuotaWindow("5 hour", 0).DisplayPercent(true) == 100);
Check("Exhausted quota has no capacity remaining", new QuotaWindow("5 hour", 100).DisplayPercent(true) == 0);
Check("Remaining display clamps out-of-range usage", new QuotaWindow("5 hour", 140).DisplayPercent(true) == 0 && new QuotaWindow("5 hour", -20).DisplayPercent(true) == 100);
var retryGate = new RetryGate();
var retryUntil = retryGate.RecordRateLimit(now, "2854");
Check("Claude honors a server cooldown longer than polling interval", retryUntil == now.AddSeconds(2854));
Check("Manual refresh cannot bypass server cooldown", retryGate.IsBlocked(now.AddMinutes(5)));
Check("Saved cooldown survives restart", new RetryGate(retryUntil).IsBlocked(now.AddMinutes(40)));
Check("Cooldown permits requests after deadline", !retryGate.IsBlocked(now.AddSeconds(2855)));
retryGate.RecordSuccess();
Check("Successful response clears cooldown", retryGate.NotBefore is null);
var retryDate = now.AddHours(2).ToString("R");
Check("HTTP-date Retry-After is supported", new RetryGate().RecordRateLimit(now, retryDate) == now.AddHours(2));
Check("Missing retry deadline uses a safe delay", new RetryGate().RecordRateLimit(now, null) == now.AddMinutes(5));
Check("Retry-After zero does not cause a tight loop", new RetryGate().RecordRateLimit(now, "0") >= now.AddMinutes(1));
Check("Invalid retry value does not cause a tight loop", new RetryGate().RecordRateLimit(now, "invalid") >= now.AddMinutes(1));
var repeated = new RetryGate(); repeated.RecordRateLimit(now, null);
Check("Repeated rate limits back off", repeated.RecordRateLimit(now.AddMinutes(5), null) == now.AddMinutes(15));
var unchanged = new RetryGate(now.AddHours(2));
Check("A repeated response cannot shorten an existing cooldown", unchanged.RecordRateLimit(now, "60") == now.AddHours(2));
var agVisible = AntigravityPresentation.VisibleWindows([
    new("Weekly Limit Remaining", 21, Key: "gemini-weekly", Group: "Gemini Models"),
    new("Five Hour Limit Remaining", 33, Key: "gemini-5h", Group: "Gemini Models"),
    new("Weekly Limit Remaining", 12, Key: "3p-weekly", Group: "Claude and GPT models"),
    new("Five Hour Limit Remaining", 17, Key: "3p-5h", Group: "Claude and GPT models"),
    new("Gemini 4 Argon Weekly Limit", 44, Key: "gemini-argon-wtu-weekly", Group: "Gemini 4 Argon")]);
Check("Gemini and Argon are visible together", agVisible.Count == 3 && agVisible.Any(w => w.Group == "Gemini Models") && agVisible.Any(w => w.Group == "Gemini 4 Argon"));
Check("Other Antigravity groups are excluded", agVisible.All(w => w.Group != "Claude and GPT models"));
Check("Antigravity window labels do not contradict display mode", agVisible[0].Label == "5 hour" && agVisible[1].Label == "Weekly" && agVisible[2].Label == "Weekly");
Check("Antigravity presentation preserves server usage values", agVisible[0].UsedPercent == 33 && agVisible[2].UsedPercent == 44);
var agMissing = AntigravityPresentation.VisibleWindows([]);
Check("Missing Gemini and Argon remain visible as unknown", agMissing.Count == 3 && agMissing.All(w => w.UsedPercent is null));
Check("Missing Antigravity is not shown as 100 percent remaining", agMissing.All(w => w.DisplayText(true) == "—"));
var fiveHourReset = new QuotaWindow("5 hour", 44, now.AddHours(2).AddMinutes(14));
Check("Five-hour reset shows the local clock time", fiveHourReset.ResetDetailsText(now) == "Resets at " + now.AddHours(2).AddMinutes(14).LocalDateTime.ToString("h:mm tt"));
var weeklySoon = new QuotaWindow("Weekly", 7, now.AddHours(2));
Check("Weekly reset today shows only the time in details", weeklySoon.ResetDetailsText(now) == "Resets at " + now.AddHours(2).LocalDateTime.ToString("h:mm tt"));
Check("Weekly reset includes a clock time", weeklySoon.ResetDetailsText(now).Contains(now.AddHours(2).LocalDateTime.ToString("h:mm tt")));
var late = new DateTimeOffset(now.LocalDateTime.Date.AddHours(23));
Check("Five-hour reset across midnight identifies the next day", new QuotaWindow("5 hour", 44, late.AddHours(2)).ResetDetailsText(late).Contains(late.AddHours(2).LocalDateTime.ToString("ddd")));
Check("Missing reset metadata never invents a date", new QuotaWindow("Weekly", null, Note: "Not supplied by provider").ResetDetailsText(now) == "Not supplied by provider");
Check("Reset-details display retains the reset-due state", new QuotaWindow("Weekly", 44, now.AddMinutes(-1)).ResetDetailsText(now).Contains("Reset due"));
Check("Prominent five-hour label is the clock time without a prefix", fiveHourReset.ResetSummaryText(now) == now.AddHours(2).AddMinutes(14).LocalDateTime.ToString("h:mm tt"));
Check("Weekly reset today shows a time", weeklySoon.ResetSummaryText(now) == now.AddHours(2).LocalDateTime.ToString("h:mm tt"));
var tomorrowReset = new QuotaWindow("5 hour", 44, late.AddHours(2));
Check("Reset tomorrow shows a date even within 24 hours", tomorrowReset.ResetSummaryText(late) == late.AddHours(2).LocalDateTime.ToString("MMM d"));
Check("Reset label changes from date to time at local midnight", tomorrowReset.ResetSummaryText(late.AddHours(1)) == late.AddHours(2).LocalDateTime.ToString("h:mm tt"));
var weeklyLater = new QuotaWindow("Weekly", 7, now.AddDays(2));
Check("Later weekly reset shows a date", weeklyLater.ResetSummaryText(now) == now.AddDays(2).LocalDateTime.ToString("MMM d"));
Check("Missing reset summary stays unknown", new QuotaWindow("5 hour", 44).ResetSummaryText(now) == "—");
Check("Past reset summary is marked due without fabricating new data", new QuotaWindow("5 hour", 44, now.AddMinutes(-1)).ResetSummaryText(now) == "Due");
bool Near(double a, double b) => Math.Abs(a - b) < .00001;
var fullWidget = new WidgetRectangle(100, 200, 410, 715);
var compactWidget = WidgetGeometry.Resize(fullWidget, ResizeEdges.BottomRight, -500, -500);
Check("Resize cannot shrink below readable minimum", Near(compactWidget.Width, 220) && Near(compactWidget.Height, 715d * 220 / 410));
var oversizedWidget = WidgetGeometry.Resize(fullWidget, ResizeEdges.BottomRight, 500, 500);
Check("Expanded maximum is respected", Near(oversizedWidget.Width, 560) && oversizedWidget.Height <= 1000);
var fromLeft = WidgetGeometry.Resize(fullWidget, ResizeEdges.Left, 25, 0);
Check("Left resize scales both dimensions and preserves right edge", Near(fromLeft.Left + fromLeft.Width, 510) && Near(fromLeft.Width, 385) && Near(fromLeft.Height / fromLeft.Width, 715d / 410));
var fromTop = WidgetGeometry.Resize(fullWidget, ResizeEdges.Top, 0, 75);
Check("Top resize scales both dimensions and preserves bottom edge", Near(fromTop.Top + fromTop.Height, 915) && Near(fromTop.Height, 640) && Near(fromTop.Width / fromTop.Height, 410d / 715));
var fromTopLeft = WidgetGeometry.Resize(fullWidget, ResizeEdges.TopLeft, 1000, 1000);
Check("Top-left minimum clamp preserves opposite corner", Near(fromTopLeft.Left + fromTopLeft.Width, 510) && Near(fromTopLeft.Top + fromTopLeft.Height, 915) && Near(fromTopLeft.Width, 220));
var fromTopRight = WidgetGeometry.Resize(fullWidget, ResizeEdges.TopRight, -20, 20);
Check("Corner follows stronger drag axis without distortion", Near(fromTopRight.Width, 390) && Near(fromTopRight.Height, 715d * 390 / 410) && fromTopRight.Left == 100);
var fromBottomLeft = WidgetGeometry.Resize(fullWidget, ResizeEdges.BottomLeft, 20, -20);
Check("Bottom-left resize preserves top and right edges", Near(fromBottomLeft.Width, 390) && Near(fromBottomLeft.Left + fromBottomLeft.Width, 510) && fromBottomLeft.Top == 200);
Check("Invalid drag delta leaves geometry unchanged", WidgetGeometry.Resize(fullWidget, ResizeEdges.Right, double.NaN, 0) == fullWidget);
Check("Saved dimensions are clamped safely", WidgetGeometry.ClampWidth(-1) == 220 && WidgetGeometry.ClampHeight(9999) == 1000 && WidgetGeometry.ClampWidth(double.NaN) == 410);
var smallerMonitor = WidgetGeometry.Resize(fullWidget, ResizeEdges.BottomRight, 10, 10, 410, 600);
Check("Smaller monitor limits preserve proportions", Near(smallerMonitor.Height, 600) && Near(smallerMonitor.Width / smallerMonitor.Height, 410d / 715));
var compactContent = new WidgetRectangle(100, 200, 410, 610);
var compactContentResize = WidgetGeometry.Resize(compactContent, ResizeEdges.Bottom, 0, -80, 410, 610);
Check("Content-fit widgets retain their own aspect ratio", Near(compactContentResize.Height / compactContentResize.Width, 610d / 410) && compactContentResize.Width < 410 && compactContentResize.Width >= 220);
var copilotQuota = AdditionalUsageParser.Copilot(JsonDocument.Parse("""
{"quota_reset_date_utc":"2026-12-01T00:00:00Z","token_based_billing":true,"quota_snapshots":{"premium_interactions":{"percent_remaining":66,"entitlement":"1000"},"completions":{"unlimited":true,"percent_remaining":100}}}
""").RootElement, now);
Check("Copilot credits use reported remaining percentage", copilotQuota.State == ConnectionState.Connected && copilotQuota.Windows[0].UsedPercent == 34 && copilotQuota.Windows[0].Label == "AI credits");
Check("Copilot unlimited allowance is unknown rather than zero", copilotQuota.Windows[1].UsedPercent is null && copilotQuota.Windows[1].Note == "Unlimited allowance");
Check("Copilot preserves provider billing reset", copilotQuota.Windows[0].ResetsAt == DateTimeOffset.Parse("2026-12-01T00:00:00Z"));
var copilotLegacy = AdditionalUsageParser.Copilot(JsonDocument.Parse("""{"monthly_quotas":{"chat":500,"completions":4000},"limited_user_quotas":{"chat":400,"completions":1000}}""").RootElement, now);
Check("Copilot free allowances are remaining counts", copilotLegacy.Windows[0].UsedPercent == 20 && copilotLegacy.Windows[1].UsedPercent == 75);
var copilotCounts = AdditionalUsageParser.Copilot(JsonDocument.Parse("""{"quota_snapshots":{"premium_interactions":{"entitlement":"300","quota_remaining":240,"quota_reset_at":1796083200}}}""").RootElement, now);
Check("Copilot count fallback supports string entitlements", copilotCounts.Windows[0].UsedPercent == 20 && copilotCounts.Windows[0].ResetsAt is not null);
Check("Copilot missing allowances stay unavailable", AdditionalUsageParser.Copilot(JsonDocument.Parse("{}").RootElement, now).State == ConnectionState.Unavailable);
var cursorQuota = AdditionalUsageParser.Cursor(JsonDocument.Parse("""
{"billingCycleEnd":"1796083200000","planUsage":{"limit":2000,"includedSpend":500,"totalSpend":1500,"totalPercentUsed":75},"spendLimitUsage":{"individualLimit":1000,"individualUsed":200}}
""").RootElement, now);
Check("Cursor included usage excludes bonus spend", cursorQuota.Windows[0].UsedPercent == 25);
Check("Cursor on-demand cap is a separately labeled window", cursorQuota.Windows[1].UsedPercent == 20 && cursorQuota.Windows[1].Label == "On-demand budget");
Check("Cursor handles millisecond billing-cycle reset", cursorQuota.Windows[0].ResetsAt == DateTimeOffset.FromUnixTimeMilliseconds(1796083200000));
Check("Cursor no supplied cap creates no invented budget", AdditionalUsageParser.Cursor(JsonDocument.Parse("""{"planUsage":{"totalPercentUsed":12},"spendLimitUsage":{"totalSpend":100}}""").RootElement, now).Windows.Count == 1);
Check("Cursor absent plan values stay unavailable", AdditionalUsageParser.Cursor(JsonDocument.Parse("""{"planUsage":{}}""").RootElement, now).State == ConnectionState.Unavailable);
Check("Cursor zero allowance never divides by zero", AdditionalUsageParser.Cursor(JsonDocument.Parse("""{"planUsage":{"limit":0,"includedSpend":0}}""").RootElement, now).State == ConnectionState.Unavailable);
Check("Cursor invalid percentage stays unknown", AdditionalUsageParser.Cursor(JsonDocument.Parse("""{"planUsage":{"totalPercentUsed":-1}}""").RootElement, now).State == ConnectionState.Unavailable);
Check("Malformed provider roots do not throw", AdditionalUsageParser.Cursor(JsonDocument.Parse("[]").RootElement, now).State == ConnectionState.Unavailable && AdditionalUsageParser.Copilot(JsonDocument.Parse("null").RootElement, now).State == ConnectionState.Unavailable);
Check("New providers have no invented five-hour or weekly windows", UsageSnapshot.Empty("copilot", ConnectionState.Unavailable, "Offline").Windows.Single().Label == "Allowance" && UsageSnapshot.Empty("cursor", ConnectionState.Unavailable, "Offline").Windows.Single().Label == "Included usage");
Check("Provider migration keeps the existing three defaults", ProviderCatalog.Normalize(null).SequenceEqual(new[] { "claude", "codex", "antigravity" }));
Check("Provider order preserves choices and removes unknowns and duplicates", ProviderCatalog.Normalize(new[] { "cursor", "unknown", "copilot", "cursor" }).SequenceEqual(new[] { "cursor", "copilot" }));
Check("Removing every provider preserves an empty selection", ProviderCatalog.Normalize(Array.Empty<string>()).Length == 0);
var mondayReset = DateTimeOffset.Parse("2026-11-09T00:00:00Z");
var paceQuota = new QuotaWindow("Weekly", 44, mondayReset);
WeeklyPace? PaceAt(string date) => WeeklyPace.Calculate(paceQuota, DateTimeOffset.Parse(date), TimeZoneInfo.Utc);
Check("Weekly pace allocates twenty percent to a full business day", Near(PaceAt("2026-11-02T12:00:00Z")!.ExpectedUsedPercent, 20) && Near(PaceAt("2026-11-02T12:00:00Z")!.FullDayBudgetPercent, 20));
var wednesdayPace = PaceAt("2026-11-04T12:00:00Z")!;
Check("Wednesday close expects sixty used or forty remaining", Near(wednesdayPace.DisplayPercent(false), 60) && Near(wednesdayPace.DisplayPercent(true), 40));
Check("Pace is an end-of-day guide rather than an hourly allowance", PaceAt("2026-11-04T00:01:00Z") == PaceAt("2026-11-04T23:59:00Z"));
Check("Weekend pace stays flat", PaceAt("2026-11-06T12:00:00Z") == PaceAt("2026-11-07T12:00:00Z") && PaceAt("2026-11-07T12:00:00Z") == PaceAt("2026-11-08T12:00:00Z"));
Check("Reset expiry hides pace until the next observation", PaceAt("2026-11-09T00:00:00Z") is null);
Check("Reset outside the current weekly cycle has no marker", PaceAt("2026-11-01T23:59:00Z") is null);
Check("Unknown usage or reset never invents a marker", WeeklyPace.Calculate(paceQuota with { UsedPercent = null }, now) is null && WeeklyPace.Calculate(paceQuota with { ResetsAt = null }, now) is null);
Check("Short and monthly windows never receive weekly markers", WeeklyPace.Calculate(paceQuota with { Label = "5 hour" }, mondayReset.AddDays(-2)) is null && WeeklyPace.Calculate(paceQuota with { Label = "Included usage" }, mondayReset.AddDays(-2)) is null);
var middayQuota = new QuotaWindow("Weekly", 44, DateTimeOffset.Parse("2026-11-06T12:00:00Z"));
var middayPace = WeeklyPace.Calculate(middayQuota, DateTimeOffset.Parse("2026-10-30T15:00:00Z"), TimeZoneInfo.Utc)!;
Check("Midday reset prorates the first business day", Near(middayPace.ExpectedUsedPercent, 10) && Near(middayPace.FullDayBudgetPercent, 20));
Check("Last partial day caps pace at the reset", Near(WeeklyPace.Calculate(middayQuota, DateTimeOffset.Parse("2026-11-06T09:00:00Z"), TimeZoneInfo.Utc)!.ExpectedUsedPercent, 100));
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
var easternQuota = new QuotaWindow("Weekly", 44, DateTimeOffset.Parse("2026-11-09T05:00:00Z"));
Check("Pace uses the local date rather than the UTC date", Near(WeeklyPace.Calculate(easternQuota, DateTimeOffset.Parse("2026-11-05T02:00:00Z"), eastern)!.ExpectedUsedPercent, 60));
var dstQuota = paceQuota with { ResetsAt = DateTimeOffset.Parse("2026-11-02T05:00:00Z") };
var dstPace = WeeklyPace.Calculate(dstQuota, DateTimeOffset.Parse("2026-10-29T02:00:00Z"), eastern)!;
Check("Fall daylight-saving transition prorates the actual seven-day cycle", Near(dstPace.ExpectedUsedPercent, 20d * 71 / 24) && Near(dstPace.FullDayBudgetPercent, 20));
var customPace = WeeklyPace.Calculate(paceQuota, DateTimeOffset.Parse("2026-11-04T12:00:00Z"), TimeZoneInfo.Utc, 10)!;
Check("Custom daily budget changes the cumulative weekday target", Near(customPace.ExpectedUsedPercent, 30) && Near(customPace.FullDayBudgetPercent, 10) && Near(customPace.DisplayPercent(true), 70));
Check("Custom daily budget does not stretch to consume the entire allowance", Near(WeeklyPace.Calculate(paceQuota, DateTimeOffset.Parse("2026-11-06T12:00:00Z"), TimeZoneInfo.Utc, 10)!.ExpectedUsedPercent, 50));
Check("High daily budgets cap the marker at one hundred percent", Near(WeeklyPace.Calculate(paceQuota, DateTimeOffset.Parse("2026-11-04T12:00:00Z"), TimeZoneInfo.Utc, 50)!.ExpectedUsedPercent, 100));
Check("Invalid daily budgets use safe defaults or bounds", WeeklyPace.NormalizeDailyPercent(double.NaN) == 20 && WeeklyPace.NormalizeDailyPercent(double.PositiveInfinity) == 20 && WeeklyPace.NormalizeDailyPercent(0) == 1 && WeeklyPace.NormalizeDailyPercent(200) == 100);
Check("Pace explanation identifies display mode and weekday budget", wednesdayPace.Explanation(true).Contains("40% remaining") && wednesdayPace.Explanation(false).Contains("60% used") && wednesdayPace.Explanation(true).Contains("20% per full weekday"));
var discoveryRoot = Path.Combine(AppContext.BaseDirectory, "claude-discovery-fixtures", Guid.NewGuid().ToString("N"));
string CliFixture(string scenario, string relative, string content = "")
{
    var path = Path.GetFullPath(Path.Combine(discoveryRoot, scenario, relative));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
}
string? Discover(string scenario, string[]? directories = null, string[]? running = null)
{
    var root = Path.Combine(discoveryRoot, scenario);
    return TokenWatch.ClaudeSignIn.FindExecutable(Path.Combine(root, "home"), Path.Combine(root, "local"), Path.Combine(root, "roaming"), directories ?? [], running);
}
var nativeCli = CliFixture("native", "home/.local/bin/claude.exe");
Check("Claude sign-in retains native installer discovery", Discover("native") == nativeCli);
var npmCli = CliFixture("npm", "roaming/npm/claude.cmd");
Check("Claude sign-in detects the npm launcher without PATH", Discover("npm") == npmCli);
var customCli = CliFixture("path", "custom install/claude.exe");
Check("Claude sign-in searches quoted PATH directories", Discover("path", ["\"" + Path.GetDirectoryName(customCli) + "\""]) == customCli);
var customNpm = CliFixture("custom-npm", "custom prefix/claude.cmd");
Check("Claude sign-in detects custom npm prefixes on PATH", Discover("custom-npm", [Path.GetDirectoryName(customNpm)!]) == customNpm);
var runningCli = CliFixture("running", "running install/claude.exe");
Check("Claude sign-in can use an already-running native CLI", Discover("running", running: [runningCli]) == runningCli);
var desktopCli = CliFixture("desktop", "local/Claude-Data/claude_profile/claude-code/1.2.3/bin/claude.exe");
Check("Claude Desktop bundled CLI remains a fallback", Discover("desktop") == desktopCli);
var wingetCli = CliFixture("winget", "local/Microsoft/WinGet/Links/claude.exe");
Check("Claude sign-in detects WinGet links without PATH", Discover("winget") == wingetCli);
var desktopApp = CliFixture("desktop-app", "install/Claude.exe");
CliFixture("desktop-app", "install/resources/app.asar");
Check("Claude Desktop's Electron app is never mistaken for the CLI", Discover("desktop-app", running: [desktopApp]) is null);
var resourceCli = CliFixture("desktop-app", "install/resources/app.asar.unpacked/cli/claude.exe");
var desktopRoot = Path.Combine(discoveryRoot, "desktop-app");
Check("Running Claude Desktop install resources can supply the actual CLI", TokenWatch.ClaudeSignIn.FindExecutable(Path.Combine(desktopRoot, "home"), Path.Combine(desktopRoot, "local"), Path.Combine(desktopRoot, "roaming"), [], [desktopApp], [Path.GetDirectoryName(desktopApp)!]) == resourceCli);
var packagedCli = CliFixture("msix", "local/Packages/Claude_examplepublisher/LocalCache/Roaming/Claude/claude-code/7.8.9/claude.exe");
Check("Packaged Claude Desktop discovery ignores publisher and version specifics", Discover("msix") == packagedCli);
var alternatePackageCli = CliFixture("alternate-msix", "local/Packages/AnthropicPBC.Claude_otherpublisher/LocalCache/Roaming/Claude/claude-code/99.1.0/claude.exe");
Check("Alternate Claude Desktop package families are discovered dynamically", Discover("alternate-msix") == alternatePackageCli);
var roamingCli = CliFixture("roaming", "roaming/Claude/claude-code/8.7.6/claude.exe");
Check("Unpackaged Claude Desktop roaming data remains supported", Discover("roaming") == roamingCli);
var setupInfo = TokenWatch.ClaudeSignIn.CreateSetupStartInfo();
Check("First-run setup uses the official installer without fixed Claude version paths", setupInfo.Arguments.Contains("https://claude.ai/install.ps1") && setupInfo.UseShellExecute && !setupInfo.Arguments.Contains("4.23118") && setupInfo.FileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase));
Check("Claude discovery skips relative and missing search paths", Discover("missing", [".", "relative", Path.Combine(discoveryRoot, "absent")]) is null);
var nativeLogin = TokenWatch.ClaudeSignIn.CreateStartInfo(nativeCli);
Check("Native Claude login uses the auth login command", nativeLogin.FileName == nativeCli && nativeLogin.Arguments == "auth login" && nativeLogin.UseShellExecute);
var batchCli = CliFixture("batch", "npm prefix & spaces %literal%!/claude.cmd", "@echo off\r\n> \"%TOKENWATCH_TEST_OUTPUT%\" echo %1 %2\r\nexit /b 0\r\n");
var loginInfo = TokenWatch.ClaudeSignIn.CreateStartInfo(batchCli);
var output = Path.Combine(discoveryRoot, "batch-arguments.txt");
loginInfo.Environment["TOKENWATCH_TEST_OUTPUT"] = output;
loginInfo.CreateNoWindow = true; loginInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
using (var loginTest = System.Diagnostics.Process.Start(loginInfo))
{
    var exited = loginTest?.WaitForExit(10000) == true;
    if (!exited) loginTest?.Kill();
    Check("npm Claude login handles spaces and shell characters with exact auth arguments", exited && loginTest!.ExitCode == 0 && File.Exists(output) && File.ReadAllText(output).Trim() == "auth login");
}
var originalSettingsPath = TokenWatch.Settings.FilePath;
var settingsRoot = Path.Combine(discoveryRoot, "settings-recovery"); Directory.CreateDirectory(settingsRoot);
TokenWatch.Settings.FilePath = Path.Combine(settingsRoot, "TokenWatch.settings.json");
foreach (var invalid in new[] { "[]", "null", "42", "{broken", "{\"ProviderOrder\":false}" })
{
    File.WriteAllText(TokenWatch.Settings.FilePath, invalid);
    var recoveredSettings = TokenWatch.Settings.Load();
    Check("Malformed settings recover with defaults: " + invalid, recoveredSettings.RefreshMinutes == 2 && recoveredSettings.ProviderOrder.Length == 3 && TokenWatch.Settings.PersistenceError is not null);
    Check("Malformed settings are preserved before replacement: " + invalid, Directory.GetFiles(settingsRoot, "*.invalid-*").Any(p => File.ReadAllText(p) == invalid) && recoveredSettings.Save());
}
File.WriteAllText(TokenWatch.Settings.FilePath, "{\"RefreshMinutes\":999,\"DailyUsagePercent\":0,\"ProviderOrder\":[\"cursor\",\"cursor\",\"unknown\"]}");
var normalizedSettings = TokenWatch.Settings.Load();
Check("Settings normalize intervals budgets and provider selections", normalizedSettings.RefreshMinutes == 60 && normalizedSettings.DailyUsagePercent == 1 && normalizedSettings.ProviderOrder.SequenceEqual(new[] { "cursor" }));
var previousRelease = Path.Combine(settingsRoot, "TokenWatch-v0.5.9"); var futureRelease = Path.Combine(settingsRoot, "TokenWatch-v0.6.0");
Directory.CreateDirectory(previousRelease); Directory.CreateDirectory(futureRelease);
var previousSettings = Path.Combine(previousRelease, "TokenWatch.settings.json");
File.WriteAllText(previousSettings, "{\"DailyUsagePercent\":10,\"ShowPaceMarkers\":false,\"WidgetPreferredHeight\":800,\"CodexExecutable\":\"C:\\\\fixture\\\\codex.exe\",\"ProviderOrder\":[\"cursor\"]}");
var previousText = File.ReadAllText(previousSettings);
TokenWatch.Settings.FilePath = Path.Combine(futureRelease, "TokenWatch.settings.json");
Check("Isolated settings do not import another release", TokenWatch.Settings.Load().ProviderOrder.Length == 3);
var migrated = TokenWatch.Settings.Load(migrate: true);
Check("WPF imports WinUI provider pace and independent-height preferences", migrated.ProviderOrder.SequenceEqual(new[] { "cursor" }) && migrated.DailyUsagePercent == 10 && !migrated.ShowPaceMarkers && migrated.WidgetPreferredHeight == 800 && migrated.CodexExecutable is not null);
Check("Migration never changes the source preferences", migrated.Save() && File.ReadAllText(previousSettings) == previousText);
TokenWatch.Settings.FilePath = originalSettingsPath;
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
