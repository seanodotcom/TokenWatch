namespace TokenWatch.Core;

public sealed record ProviderDefinition(string Id, string Name, string Color, string UsageUrl, string Setup)
{
    public override string ToString() => Name;
}

public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderDefinition> All { get; } = [
        new("claude", "Claude", "#FA8C75", "https://claude.ai/settings/usage", "Experimental usage connection using Claude Code's existing sign-in. TokenWatch renews the session in Claude Code's credential store. This internal interface may change or become unavailable; it is not an officially supported Anthropic integration."),
        new("codex", "Codex", "#79D9B2", "https://chatgpt.com/codex/settings/usage", "Uses your local Codex app or CLI and its ChatGPT subscription sign-in."),
        new("antigravity", "Antigravity", "#93A4FF", "", "Open Antigravity and a workspace. Quotas are read from its local language server. This internal usage connection may change."),
        new("copilot", "GitHub Copilot", "#B7A1FF", "https://github.com/settings/copilot", "Sign in with GitHub CLI using the button below, then check the connection. Your account needs a Copilot subscription. A VS Code sign-in alone cannot be reused. Experimental connection."),
        new("cursor", "Cursor", "#D5DEE9", "https://cursor.com/dashboard?tab=usage", "Sign in in Cursor Desktop, then refresh here. TokenWatch reads the local Cursor sign-in without changing it. If it expires, reopen Cursor. This usage interface is experimental.")
    ];
    public static string[] DefaultOrder => ["claude", "codex", "antigravity"];
    public static string[] Normalize(IEnumerable<string>? ids) => (ids ?? DefaultOrder)
        .Where(id => All.Any(p => p.Id == id)).Distinct(StringComparer.Ordinal).ToArray();
}
