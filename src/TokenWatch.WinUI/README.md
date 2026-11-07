# TokenWatch v0.5.9 — WinUI 3

Weekly quota bars have a thin white expected-pace tick with a small filled white caret directly below it. In Settings → Appearance, **Show expected usage markers** defaults to on, and **Daily usage budget (%)** defaults to 20%. Choose a daily percentage from 1–100; changes save and apply immediately. Turning markers off retains your budget. The markers show planned usage by today's close using a Monday–Friday budget: 10% per day means 30% used by Wednesday's close. Weekends hold steady; partial reset days are prorated, and cumulative targets cap at 100%. Hover over the quota for a brief explanation. The tick follows Show used / Show remaining and is hidden for unavailable or stale readings, expired/missing resets, and nonweekly quotas. It is a planning guide, not a provider-imposed daily limit.

This is the working WinUI 3 app. Normal startup reads live usage using the existing provider connections. Sample data is available only with `--demo` or the explicit offline UI test switch.

## Run

Run `TokenWatch.exe` from the versioned release folder or portable ZIP. The self-contained Windows x64 edition includes .NET 10 and Windows App SDK 2.6.5; it extracts dependencies on first launch. It does not need an installer, Developer Mode or administrator rights.

`TokenWatch-framework-dependent.exe` needs the .NET 10 x64 runtime and all accompanying files from its ZIP. Windows App SDK dependencies are included. Keep that edition's folder contents together.

The widget keeps running in the notification area when closed or hidden with Escape. Click the donut tray icon to show it again. Its native right-click menu includes Show, Keep widget open, Always on top, Refresh, Settings and Exit. Exit removes the tray icon and stops polling. Explorer restarts trigger icon registration again. If the notification area is unavailable, the app retains a taskbar entry and minimizes rather than becoming inaccessible.

F5 refreshes usage. The header donut shows the next automatic refresh; click it to change the interval. Automatic polling continues while hidden. The header pin controls always-on-top; Keep widget open is a separate focus-loss preference. Widget position and width are saved.

## Existing sign-ins and settings

Credentials are not moved into TokenWatch. The same adapter source files as the WPF app are compiled into this application:

- Claude sign-in discovers native installs, current/user/system PATH entries, npm batch launchers, running native Claude processes, WinGet links and Claude Desktop's bundled CLI. Desktop package families, publisher suffixes and CLI versions are discovered dynamically, including MSIX package caches and current Claude-Data profiles. Desktop's Electron launcher is excluded. If no compatible helper exists, Sign in offers **Set up and sign in**, **Setup guide**, or **Cancel**. Setup requires the user's choice, runs Anthropic's official Windows installer, then continues to account login and checks the connection automatically.
- Claude uses Claude Code's existing `.claude/.credentials.json` (or `CLAUDE_CONFIG_DIR`) and preserves automatic OAuth renewal and server cooldowns.
- Codex uses the already signed-in app/CLI app-server. A custom `codex.exe` path is preserved and can be changed in provider details.
- Antigravity uses its running local language server; keep Antigravity and a workspace open.
- GitHub Copilot uses the existing Copilot token variable/legacy credential file or GitHub CLI sign-in. Cursor uses its existing Desktop credential database read-only. These two adapters remain experimental and need an accepted sign-in for their internal usage endpoints.

Provider details show the real status, last successful observation and any server retry deadline. Sign in opens the provider's app/CLI for you to complete authentication. TokenWatch makes usage requests, not inference requests. Failed checks retain the previous reading, visibly marked stale; absent quotas stay unknown.

Normal preferences are stored beside the executable as `TokenWatch.settings.json`. When this file is absent, TokenWatch looks in sibling versioned release folders for the newest existing live settings and preview preferences. It preserves the live executable override, retry deadlines, geometry and keep-open setting, then applies newer preview visibility/display choices. Source settings and credential stores are not copied or deleted. Replacing only the executable in your existing writable app folder also reuses that folder's settings.

Provider switches show/hide widget sections immediately. Removing a provider only removes its TokenWatch entry; its app sign-in remains intact. Preferences and server retry timestamps are saved, but credentials, identities, usage snapshots, prompts and histories are not.

## Build and verification

Use `./build-winui.ps1` with the .NET 10 SDK. Both x64 executable versions are checked and published to `dist/TokenWatch-v0.5.9/` with matching versioned ZIPs. Sample/offline checks use:

```powershell
TokenWatch.exe --demo --isolated --settings <isolated-settings.json> --smoke <report.json>
```

The native checks cover adaptive vertical spacing/control sizes, bounded typography, footer separation and returning to compact density, plus diagonal resizing, anchored corners, track limits, independent height, saved height, native caption boundaries and stable bounds after release, plus stable progress bars across unchanged readings and tray/clock ticks, isolated updates to changed quotas, all five providers, visibility and reorder behavior, settings migration, injected live-service responses, stale readings, serialization, polling while hidden, Windows Shell tray registration, native menu commands, close-to-tray, activation and icon cleanup. Screenshots generated by this command contain sample data only.

`--probe <status.json>` checks the enabled live providers and exits without creating windows. Its status report excludes credentials and usage values. Do not use `--demo` when probing real connections.

The app targets Windows 10 build 17763+ and Windows 11 x64. This build has been exercised on this development machine; a separate Windows 10 machine has not been tested. Drag the native window border to choose a width from 220 to 560 DIPs. Top, bottom and corner drags also choose a height, with a minimum that keeps all visible quotas readable. Extra height is preserved across usage updates and restarts. Provider spacing grows first, then text, logos, controls and bars enlarge gently within width-aware limits; the footer stays below the providers. Shrinking returns to compact density. Drag the title text to move the window using native Windows caption behavior. Native sizing constrains the proposed rectangle directly, preserving the opposite edge without competing window-resize calls.

See `THIRD-PARTY-NOTICES.md` and `licenses/` for runtime and icon licenses. Archived v0.4.x packages are visual prototypes; v0.5.0 restores live connections and tray behavior.

## New-user Claude sign-in requirement

A new TokenWatch installation should connect an existing Claude account with as few steps as possible. Reuse existing sign-ins and discover launchers automatically; offer guided helper setup when needed. Installation-specific version numbers, WindowsApps package directory names and publisher suffixes are diagnostic examples, never fixed lookup requirements.
