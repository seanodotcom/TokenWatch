namespace TokenWatch.Core;

public static class AntigravityPresentation
{
    public static IReadOnlyList<QuotaWindow> VisibleWindows(IReadOnlyList<QuotaWindow> source)
    {
        var selected = source.Where(w => Matches(w, "gemini") || Matches(w, "argon"))
            .OrderBy(w => Matches(w, "argon") ? 1 : 0)
            .ThenBy(w => w.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => ShortLabel(w) == "5 hour" ? 0 : ShortLabel(w) == "Weekly" ? 1 : 2)
            .Select(w => w with { Label = ShortLabel(w) }).ToList();
        if (!selected.Any(w => !Matches(w, "argon")))
        {
            selected.InsertRange(0, [new("5 hour", null, Note: "Not reported by Antigravity", Group: "Gemini Models"),
                new("Weekly", null, Note: "Not reported by Antigravity", Group: "Gemini Models")]);
        }
        if (!selected.Any(w => Matches(w, "argon")))
            selected.Add(new("Quota", null, Note: "Not reported by Antigravity", Group: "Argon"));
        return selected;
    }

    private static bool Matches(QuotaWindow window, string name) =>
        (window.Group ?? "").Contains(name, StringComparison.OrdinalIgnoreCase) ||
        (window.Key ?? "").Contains(name, StringComparison.OrdinalIgnoreCase) ||
        window.Label.Contains(name, StringComparison.OrdinalIgnoreCase);

    private static string ShortLabel(QuotaWindow window)
    {
        var text = window.Label + " " + window.Key;
        if (text.Contains("weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
        if (text.Contains("five hour", StringComparison.OrdinalIgnoreCase) || text.Contains("five-hour", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("5h", StringComparison.OrdinalIgnoreCase) || text.Contains("5 hour", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("five_hour", StringComparison.OrdinalIgnoreCase)) return "5 hour";
        return window.Label;
    }
}
