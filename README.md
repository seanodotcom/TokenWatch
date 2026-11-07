# TokenWatch

A little Windows widget that answers: **how much AI quota do I have left?**

Built as a simple, free utility for the tech community. Keep Claude, Codex, Antigravity, Copilot and Cursor usage in view, without hopping between dashboards.

## Just unzip and go

1. **[Download TokenWatch](https://github.com/seanodotcom/TokenWatch/releases/latest).** Choose the standard ZIP if you're unsure.
2. **Unzip it into a writable folder and double-click the EXE.** TokenWatch is portable: there's no TokenWatch installer, admin setup or service to install.
3. **Click the gear to open Settings**, connect the providers you use, then **Save**. Already signed in to a supported app? TokenWatch can often reuse that sign-in.

**A simple community utility, provided AS IS, with no warranties of any kind, express or implied. Use at your own risk.**

<p align="center">
  <img src="docs/images/tokenwatch.png" alt="TokenWatch showing sample Claude, Codex and Antigravity quotas, reset times and weekly planning markers" width="410">
</p>

*Sample data, so you can see the layout before downloading.*

## Which download?

Same app. Same features. The difference is whether .NET comes along for the ride.

| Pick this | What's the difference? |
|---|---|
| **[Standard / self-contained](https://github.com/seanodotcom/TokenWatch/releases/download/v0.6.2/TokenWatch-v0.6.2-win-x64.zip)** | **The easy choice.** Includes its runtime, so just unzip and run `TokenWatch.exe`. About a 61 MiB download. |
| **[Framework-dependent](https://github.com/seanodotcom/TokenWatch/releases/download/v0.6.2/TokenWatch-v0.6.2-win-x64-framework-dependent.zip)** | **Tiny EXE: about 1 MB**, in a roughly 0.7 MB ZIP. Run `TokenWatch-framework-dependent.exe`. You'll need the **[.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)** installed separately; the small size comes from sharing that runtime. |

Windows x64 only. The framework edition needs the **Desktop Runtime**, not just the plain .NET Runtime. [Checksums](https://github.com/seanodotcom/TokenWatch/releases/download/v0.6.2/TokenWatch-v0.6.2-SHA256SUMS.txt) are included with every release.

## Add your providers

Open **Settings** with the gear. Select a provider that's already listed, or choose it from the dropdown and click **Add provider**. Keep its checkbox on to show it in the widget.

| Provider | What to do in Settings |
|---|---|
| **Claude** | Select Claude, then **Sign in to Claude**. Finish the sign-in window. If a helper is missing, choose **Set up and sign in** and follow the prompts. |
| **Codex** | Select Codex, then **Sign in to Codex**. Use your ChatGPT subscription account. Install Codex first if it isn't found; you can also browse to its `codex.exe`. |
| **Antigravity** | Open Antigravity, sign in there and leave a workspace open. Select Antigravity in TokenWatch and click **Check connection**. |
| **GitHub Copilot** *(experimental)* | Install [GitHub CLI](https://cli.github.com/), add GitHub Copilot, then click **Sign in with GitHub CLI**. Use an account with a Copilot subscription. |
| **Cursor** *(experimental)* | Install and sign in to Cursor Desktop. Add Cursor, click **Open Cursor** if needed, then **Check connection**. |

Finish with **Check connection → Save**. Missing an app or subscription? Skip that provider. Uncheck one to hide it; use the arrow buttons to reorder the list.

## A few handy things

- **Move it, resize it, pin it.** Drag the header, pull an edge or click the pin to keep it on top.
- **Close hides it to the tray.** Click the donut by the clock to bring it back; right-click it and choose **Exit TokenWatch** to quit.
- **Used or remaining?** Your choice in Settings. Hover over a bar for reset details. The white weekly markers are optional planning guides.
- **Refresh happens automatically**, including while hidden. Press **F5** for a manual check. A stale reading keeps its last known value; a dash means unknown.

No telemetry. No AI prompts or model calls. It reads usage through existing provider connections. [Privacy details](PRIVACY.md).

## Small utility, no guarantees

**TokenWatch is provided AS IS, without warranties of any kind, express or implied. Use it at your own risk.** Quota readings are a convenience, not a guarantee of availability or accuracy. Check the provider's own dashboard when it matters.

The [MIT License](LICENSE) includes the full warranty disclaimer and limitation of liability. Dependencies and provider artwork have their [own notices](THIRD-PARTY-NOTICES.md).

These builds are unsigned and tested on the development Windows 11 machine; a separate clean Windows 10 machine hasn't been validated. Several connections use internal provider interfaces that can change or stop working. TokenWatch isn't affiliated with or endorsed by those providers. Copilot and Cursor live-account readings are still unverified here.

## Want the details or the source?

[Widget, connection and troubleshooting details](docs/TECHNICAL.md) · [Privacy](PRIVACY.md) · [Release notes](CHANGELOG.md)

Built with WPF and .NET 10. To build and run the offline checks with the .NET 10 SDK:

```powershell
.\build.ps1 -Test
```

The [technical reference](docs/TECHNICAL.md#build-and-verification) covers packaging and the full release checks.
