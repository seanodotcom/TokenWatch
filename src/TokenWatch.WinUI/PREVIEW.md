# TokenWatch v0.4.1 — WinUI 3 design preview

This is a separate, interactive sample-data prototype. The WPF v0.3.0 release remains the working live-provider app.

Run `TokenWatch.exe` for the self-contained x64 preview. It bundles .NET 10 and Windows App SDK 2.6.5 and extracts its dependencies on first launch. No installer, Developer Mode or administrator rights are required. To open both views immediately, run `TokenWatch.exe --settings-window`.

`TokenWatch-framework-dependent.exe` requires the .NET 10 x64 runtime and all supplied support files alongside it. Windows App SDK dependencies are included in this edition too. Keep the framework-dependent ZIP contents together; this edition is a folder-based application.

## What to try

- Resize the widget horizontally. Provider names, quota captions, percentages and reset times remain visible; its height fits the selected providers.
- Click the donut to change the sample refresh interval. Pin toggles the native always-on-top behavior. Close exits the prototype.
- Open Settings with the gear. Switch providers on or off to show or hide them immediately, drag to reorder, use the keyboard-accessible arrow buttons, remove a provider and add it again.
- Select a provider to inspect its connection and allowance details. Check connection / Reconnect simulates a successful check; it does not sign in to a service.
- Appearance changes used/remaining values, reset visibility, pinning and the interval immediately.
- Preview states lets you try a stale reading, sign-in required, or server cooldown. Stale readings stay dimmed; disconnected values stay unknown; cooldowns prevent a manual sample check.

All usage is sample data. There is no provider polling, credential lookup, inference, telemetry or tray integration in this prototype. The Usage page button opens the real provider website only when you choose it.

Only provider choices/order, on/off state and appearance preferences are saved, in `TokenWatch.WinUI-preview.settings.json` beside the executable. This file is separate from WPF's `TokenWatch.settings.json`. Connection-state examples are not persisted. Delete the preview folder to uninstall it.

## Build and checks

Use the installed .NET 10 SDK and `./build-winui.ps1`. Source lives in `src/TokenWatch.WinUI` and shares the provider catalog and quota formatting with `TokenWatch.Core`. This prototype does not modify the WPF project or its release settings.

Both x64 editions are published to `dist/TokenWatch-v0.4.1`. The packaging script verifies their executable versions and produces `dist/TokenWatch-v0.4.1-win-x64.zip` and `dist/TokenWatch-v0.4.1-win-x64-framework-dependent.zip`. The portable ZIP contains the self-contained executable; the framework-dependent ZIP includes its supporting files.

Offline native UI checks: `TokenWatch.exe --settings <isolated-json-path> --smoke <report-json-path>`. This exercises the actual WinUI windows and writes PNG screenshots beside the report. Always use an isolated settings path for this check. Native interaction testing also covered opening Providers and Appearance through the real desktop UI.

Prototype limitations: vertical size fits content; changing width uses the native resize border. Full parity with WPF's locked-aspect resizing, hide-to-tray, quota-change shimmer and live providers is future migration work. Windows 10 compatibility is targeted at build 17763+, but this preview has only been run on this development machine.

Microsoft's unpackaged distribution guidance: https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app

Dependencies are governed by their accompanying Microsoft and .NET notices. See `THIRD-PARTY-NOTICES.md` and `licenses/`.
