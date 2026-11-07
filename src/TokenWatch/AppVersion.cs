namespace TokenWatch;

internal static class AppVersion
{
    internal static string Number => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    internal static string Display => "v" + Number;
}
