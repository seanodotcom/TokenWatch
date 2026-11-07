# Release notes

## v0.6.2 — A simpler welcome

- Lead the README with a friendly quick start, a plain explanation of both downloads and the roughly 1 MB framework-dependent executable, and short setup steps for each provider.
- Move detailed protocol, troubleshooting and build notes into a separate reference.
- State the existing MIT no-warranty terms in plain language in the README and Settings: provided AS IS, without warranties of any kind, express or implied; use at your own risk.

## v0.6.1 — MIT licensing

- License TokenWatch's original code under MIT and include the complete license in both release ZIPs and the versioned executable folder.
- Retain the separate license and attribution notices for dependencies and provider artwork. Require the project license during release packaging and verify the extracted copy.

## v0.6.0 — WPF release on .NET 10

- Make WPF the current release with weekly expected-usage ticks/carets, configurable daily budgets, independent saved height, adaptive vertical spacing, official Copilot/Cursor icons and retained quota controls. Preserve Save/Cancel settings.
- Add guided Claude helper setup and Codex/Cursor sign-in actions, hide paused providers, show connection activity/retry deadlines, and migrate live preferences from sibling WinUI/WPF releases.
- Recover corrupt settings with preserved backups, protect originals if backup fails, recover abandoned empty refresh locks conservatively, keep a taskbar path when the tray is unavailable, and constrain restored geometry to available monitors.
- Move application, core and tests to .NET 10. Gate releases on core/provider/native tests, a dependency audit, executable version checks and native tests of both freshly extracted ZIPs. Publish SHA-256 checksums and support certificate signing.
- Live Copilot/Cursor accounts, a separate clean Windows 10 machine, and external provider permission are not established by offline checks. Packages remain unsigned unless a code-signing certificate is supplied.


## v0.5.4 — Adaptive vertical layout

- Use extra window height for provider spacing and gently larger typography, logos, connection controls and quota bars. Cap text growth at 30 percent and reduce the cap at narrow widths to preserve label space.
- Distribute the provider layout through the available height and keep the footer at the bottom. Shrinking restores compact density.
- Measure compact content independently from rendered expansion, so larger controls never increase the native minimum height or reintroduce resize feedback. Retain the existing quota controls and unchanged readings throughout resizing.
- Add ten native checks for expansion, visible indicator thickness, bounded typography, three-provider layouts, footer separation, unchanged readings, and returning to compact height. Exercise tall/narrow and vertical contraction with real pointer drags using sample data.

## v0.5.3 — Independent height and native title dragging

- Let top, bottom and corner drags select a taller or shorter window while keeping sufficient height for visible quotas. Retain the chosen height across usage updates, width changes and restarts using a separate saved preference.
- Replace the synthetic WM_NCLBUTTONDOWN title handler with a native caption region. Windows now owns pointer tracking and release, with the header buttons kept outside the drag region.
- Save geometry after move/resize completion. Preserve the native resize constraints and the progress-bar stability fixes.
- Validate independent height, settings reload, caption boundaries and stable move completion in the native suite. A real pointer title drag matched its movement exactly, and the pin button worked immediately after release.

## v0.5.2 — Smooth native widget resizing

- Constrain the native resize rectangle before Windows commits it, preserving the opposite corner and enforcing the existing 220–560 DIP width range.
- Remove the SizeChanged/ResizeClient feedback loop, repeated root measurement and continuously changing padding. Measure the provider region for fitted height and defer content-driven window fitting until the drag finishes.
- Use compact header visibility that releases unused title space. Keep quota controls attached and all provider/tray behavior intact.
- Add eight native resize checks covering diagonal drags, edge anchoring, width tracking limits, updates during a drag and stable bounds after release. Exercise real side/corner drags with sample data.

## v0.5.1 — Stable quota progress bars

- Stop the five-second tray watchdog from broadcasting unchanged usage.
- Retain provider sections and quota controls across observations, changing a bar's value only when its reading changes. Keep reset labels and connection status current without restarting animations.
- Add native regression checks for repeated notifications, identical readings, a single changed quota and tray/clock ticks.

## v0.5.0 — Live WinUI app and tray restoration

- Replace normal sample startup with live polling through the existing WPF provider adapter sources, including Claude renewal, retry deadlines, stale readings and Codex executable detection.
- Migrate existing live settings and newer preview visibility/display choices without moving credentials or modifying source settings. Keep provider connections in their original app-owned stores.
- Restore the native donut tray icon and Windows menu, click/keyboard activation, close/Escape to hide, hidden polling, keep-open versus always-on-top, Explorer restart recovery and explicit Exit cleanup.
- Add live connection checks, app/CLI sign-in actions, custom Codex executable selection, activity information and F5 refresh. Sample data and state examples are restricted to explicit demo mode.
- Preserve Copilot/Cursor icons and off/hide behavior. Validate migrated Claude, Codex and Antigravity connections with status-only probes that omit credentials and quota values.

## v0.4.1 — WinUI preview corrections

- Replace the GitHub Copilot and Cursor monogram placeholders with the Copilot product icon from GitHub's Octicons and Cursor's official app icon, in both the widget and provider list.
- Switching a provider off now hides its section immediately and refits the widget. Its entry and saved settings remain available in Settings; switching it on restores it in the same order.
- Add coverage for turning both new providers off, restoring them, and switching all providers off. Preserve v0.4.0 visibility preferences when carrying the local preview settings forward.

## v0.4.0 — WinUI 3 design preview

- Add a separate native WinUI 3 prototype with all five providers and ten sample quota windows. The WPF v0.3.0 live app remains available separately.
- Use tighter widget sections, aligned percentages and reset times, persistent compact quota labels, a countdown donut, and native pinning/resizing.
- Introduce a Providers page with native switches, drag-to-reorder plus arrow controls, add/remove actions and connection details. Appearance and sample connection-state pages complete the preview.
- Save preview preferences separately; make no provider, credential or inference requests. Connection checks and reconnects are simulated and labeled as sample data.
- Publish portable self-contained and folder-based .NET 10 framework-dependent editions, with verified executable versions and versioned ZIPs.
- Validation: offline native checks cover both editions, including standalone single-EXE launch. Providers, Appearance and widget views were also reviewed through native desktop UI automation. Windows 10 minimum is targeted but has not been tested on a separate Windows 10 device.

## v0.3.0

- Add GitHub Copilot and Cursor as opt-in experimental usage providers using existing local sign-ins.
- Add a provider catalog: add, remove, pause, reorder, inspect status and check connections before saving.
- Preserve existing providers and pause states when upgrading. Selected providers determine widget order and fitted height; an empty widget offers setup guidance.
- Show Copilot request/AI-credit allowances and Cursor included usage plus supplied on-demand spending caps with their actual billing resets. Missing or unlimited quotas remain unknown.
- Persist server cooldowns for the new providers. Credentials and raw responses are never saved by TokenWatch.
- Package both executable editions in the versioned release folder and verify their versions automatically.
- Verification: core and synthetic provider/management checks pass. Live Copilot and Cursor quota readings require signed-in accounts; neither was available in the build environment. Four existing UI smoke failures remain (three Shell/tray checks and compact stale-value accessibility).

## v0.2.14

- Show the local reset time for limits expiring today and the date for later days.

## v0.2.13

- Make the header pin toggle always on top, and remove the footer and header refresh button.

## v0.2.12

- Brighten the active-bar shimmer slightly and confine it to the filled portion of the progress bar.

## v0.2.11

- Fix countdown menu contrast and detect changes in Antigravity's normalized quota labels.
- Start bar effects after refreshed controls are ready, including when the widget is already visible.

## v0.2.10

- Make the entire countdown logo respond to right-clicks, including its center, and make its final countdown pulse more visible.
- Show active-bar shimmer across the full track, including when the filled portion is empty.

## v0.2.9

- Keep changed quota bars visibly pulsing until the next unchanged successful refresh.
- Add refresh interval choices to the countdown's right-click menu and pulse the countdown in the last five seconds before an update.

## v0.2.8

- Add a subtle shimmer to a quota bar when its displayed percentage changes, respecting Windows animation preferences.

## v0.2.7

- Align the countdown ring and track as a single donut.

## v0.2.6

- Make changed quota highlights brighter and longer, and retain them while the widget is hidden until it is shown.

## v0.2.5

- Make the countdown advance clockwise around the header donut.

## v0.2.4

- Turn the header donut into a countdown to the next automatic refresh, with the remaining time in its tooltip.

## v0.2.3

- Reduce the portable executable by omitting unused framework translations, with English fallback. Preserve Windows regional date and number formatting.
- Add a separate, much smaller framework-dependent build for computers with the .NET 8 Desktop Runtime (Windows x64) installed. Both editions retain the same features; the portable build remains the default.

## v0.2.2

- Default to a two-minute refresh and showing remaining usage.
- Briefly highlight a quota row in its provider's color when its displayed percentage changes.

## v0.2.1

- Keep Claude connected by silently renewing its existing OAuth session near expiry and after an access-token rejection. Persist rotated credentials in Claude Code's store and coordinate with its current and legacy refresh locks.
- Retry temporary renewal failures automatically, retain stale readings, and require sign-in only when credentials cannot be renewed. Add synthetic coverage for expiry, rotation, restart, lock contention, rejected refresh tokens, and recovery.

## v0.2.0

- Replace the WinForms tray menu with a native Windows popup menu. Windows draws the labels, checkmarks, selection states and disabled commands; existing Show, Pin, Always on top, Refresh, Settings and Exit actions are preserved.
- Use the Shell's version-4 notification interface for mouse and keyboard activation. Return focus to the notification area after dismissing the menu, and restore the icon if Explorer restarts.
- Add the approved blue/light-gray donut logo to the widget header, window icons, tray icon and executable. Include nine icon sizes from 16 to 256 pixels, plus reproducible PNG/SVG artwork.
- Keep reset captions fully readable at high Windows display scaling, and make resize checks respect the current monitor's work area and pixel rounding.
- Preserve existing settings and provider integrations.

The native menu lifecycle follows [Microsoft's notification-menu guidance](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenu); Shell activation and focus handling follow [Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw).
