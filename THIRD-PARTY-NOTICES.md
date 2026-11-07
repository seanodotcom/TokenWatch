# Third-party notices

TokenWatch's original code is licensed under the MIT License in the root `LICENSE` file. Third-party libraries and artwork remain subject to their own licenses and trademark rights described below.

Claude, Codex, OpenAI, Antigravity, Google, and their logos are trademarks of their respective owners. TokenWatch is an independent local utility and is not endorsed by those providers.

Logo assets are used to identify their corresponding products:

- Claude starburst: the icon linked by https://claude.com, downloaded from https://assets.claude.com/95a868946ac8a31e5ff832e2899f294aa368b836.png.
- Codex cloud/terminal: the `codex-app-ga-logo` asset included with the installed official OpenAI VS Code extension.
- Antigravity icon: https://antigravity.google/press and https://antigravity.google/assets/image/brand/antigravity-icon__full-color.png.

The self-contained WPF distribution includes the .NET 10 runtime, Windows Desktop runtime, and System.Management 8.0.0. The framework-dependent edition includes application dependencies and uses the installed .NET 10 Desktop Runtime. Runtime versions are resolved by the release script. Accompanying license and notice files are copied from those exact package versions into `licenses/`, including `DOTNET-LICENSE.TXT`, `DOTNET-THIRD-PARTY-NOTICES.TXT`, `WINDOWS-DESKTOP-LICENSE`, and the System.Management `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`. The .NET license is available at https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and third-party runtime notices at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.

The GitHub Copilot icon comes from GitHub's Primer Octicons (`https://github.com/primer/octicons/blob/main/icons/copilot-24.svg`), under the MIT license included as `licenses/OCTICONS-LICENSE.txt`. The Cursor icon comes from the official asset pack linked by `https://cursor.com/brand`. These product names and icons identify their corresponding services; they do not imply endorsement or affiliation.

The Codex protocol was implemented using official documentation at https://learn.chatgpt.com/docs/app-server. The Antigravity request/response schema was checked against public integration research in https://github.com/steipete/CodexBar/tree/main/Sources/CodexBarCore/Providers/Antigravity. TokenWatch's adapters were independently implemented; no Swift implementation source is included in the distribution.
