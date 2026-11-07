# TokenWatch privacy

TokenWatch runs locally. It has no hosted backend, analytics, telemetry, advertising SDK or account-registration service. It polls allowance/status information; it does not submit prompts or make model inference calls.

## Credential access and destinations

- **Claude:** reads Claude Code's `.credentials.json` under the signed-in Windows user's profile, or `CLAUDE_CONFIG_DIR`. Usage requests go to `https://api.anthropic.com/api/oauth/usage`. Session renewal sends the existing refresh token to `https://platform.claude.com/v1/oauth/token` and atomically updates Claude Code's existing credential file, preserving unrelated fields and its ACL. A short-lived temporary file in the same directory is removed after replacement. Refresh lock directories coordinate renewal. No separate TokenWatch credential cache is created. This is an experimental internal integration; availability and provider permission are separate matters.
- **Codex:** starts its own hidden local `codex app-server`, reads subscription rate limits, and terminates its owned subprocess when complete or timed out. Codex owns authentication and any network access it performs. `CODEX_HOME` is inherited. No conversation or inference request is made.
- **Antigravity:** discovers the running language server and its CSRF token through Windows process metadata and reads quota status only from loopback ports owned by that process. Self-signed certificates are accepted only for that loopback connection. Redirects and proxies are disabled for the local handler. Remote HTTPS validation is never relaxed.
- **GitHub Copilot:** reads the explicit `COPILOT_GITHUB_TOKEN`, legacy Copilot OAuth files, or the selected `github.com` account token through GitHub CLI. The token is sent only to `https://api.github.com/copilot_internal/user`. TokenWatch does not enumerate Windows credential vaults or browser cookies.
- **Cursor:** opens Cursor's local `state.vscdb` read-only and selects only `cursorAuth/accessToken`. The token is sent to `https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage`. TokenWatch does not modify or copy the database and does not read browser cookies or rotate Cursor credentials.

Tokens and raw usage responses stay in memory except for Claude's described update to its existing credential store. TokenWatch does not save identities, prompts, conversations or quota history. Provider apps and services have their own privacy policies.

## Files and preferences

`TokenWatch.settings.json` beside the executable contains provider selections, appearance, geometry, daily planning budget, optional Codex executable path and provider retry deadlines. Corrupt settings may be preserved in a sibling `.invalid-<timestamp>-<id>` file. That backup has the same content as the original settings; treat manually edited settings as potentially sensitive.

Explicit `--probe` output contains provider names, connection states, window labels and counts, but no quota percentages, account identifiers, credentials or raw responses. Explicit demo/UI tests write only synthetic fixture data, reports and sample screenshots to the requested test paths. Normal operation does not write usage logs.

User-selected sign-in opens the provider's official app/CLI or browser. Guided Claude helper installation runs Anthropic's official Windows installer only after choosing **Set up and sign in**; opening the guide or cancelling does not install anything. Helper software and provider sign-ins remain owned by their respective apps.

Delete TokenWatch's application folder to remove its preferences and executables. This does not uninstall other apps or erase their sign-ins. Never share provider credential files when reporting a problem.
