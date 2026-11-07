namespace TokenWatch;

// Shared provider sources obtain their version from the executable hosting them.
internal static class AppVersion
{
    internal static string Number => typeof(WinUI.App).Assembly.GetName().Version!.ToString(3);
    internal static string Display => "v" + Number;
}
