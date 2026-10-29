# External References

Public sources consulted for Phase 2 (reference research) and Phase 3 (architecture). Accessed
2026-10-27 unless noted. "Use" says what each source supports; platform facts from Microsoft are
used for *our* design, not as evidence about Nusa's internals.

## 1. Reference product (Nusa)

| Source | Use | Notes |
|---|---|---|
| https://rama2555.github.io/nusa/ | Website audit, live interaction run | Text, CSS, script read as data; preview is a mock-up |
| https://api.github.com/repos/rama2555/nusa | Repository metadata (no license, creation/push dates) | Unauthenticated REST API |
| https://api.github.com/repos/rama2555/nusa/releases | v1.0.0 / v1.1.0 release notes and published installer hashes | Assets **not** downloaded |
| https://api.github.com/repos/rama2555/nusa/contents/ | Repository contents listing | |
| https://lrclib.net (and its `/docs` page) | Lyrics service referenced by Nusa | `/docs` is client-rendered; the fetch returned no API text. **API terms and lyrics licensing remain unverified** |
| https://github.com/tranxuanthang/lrclib | LRCLIB server project README | States the server is MIT-licensed; no lyrics-data terms or API rules in the README |

## 2. Microsoft platform documentation

| Source | Use |
|---|---|
| [.NET and .NET Core support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | .NET 10 LTS (EOS 2028-11-14); .NET 9 STS and .NET 8 LTS both EOS **2026-11-10** |
| [Windows App SDK release channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels) | WinAppSDK 2.0 current (2.5.1, servicing to 2027-04-29); 1.8 ended 2026-09-24; stable majors ≤ every 6 months |
| [Notification listener](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener) | `UserNotificationListener` needs the User Notification Listener capability in `Package.appxmanifest` and user consent via `RequestAccessAsync`; revocation fails silently |
| [GlobalSystemMediaTransportControlsSessionManager](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager) | Windows 10 1809+, UniversalApiContract v7; lists app capability `globalMediaControl`; events `CurrentSessionChanged`, `SessionsChanged` |
| [SHQueryUserNotificationState](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) | Presentation/quiet states; **no notification when a full-screen app starts or stops** |
| [Power setting GUIDs](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids) | `GUID_ACDC_POWER_SOURCE`, `GUID_BATTERY_PERCENTAGE_REMAINING`, `GUID_POWER_SAVING_STATUS`, `GUID_ENERGY_SAVER_STATUS` (pre-release), `GUID_SESSION_DISPLAY_STATUS`, `GUID_SESSION_USER_PRESENCE` |
| [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) | Store MSIX re-signed free worldwide; Azure Artifact Signing ~$9.99/month, individuals USA/Canada only; OV $150–300/year; EV no longer bypasses SmartScreen; SignPath Foundation for OSS |

## 3. Secondary sources (lower confidence)

| Source | Use | Caveat |
|---|---|---|
| Search results on WPF layered-window performance (Microsoft archive blogs by the WPF team, 2006–2008; community posts) | Background for the WPF `AllowsTransparency` risk | Dated; current behaviour must be measured (spike S1) |
| Search results on WinUI 3 transparent windows (WinUI Gallery 2.9 / WinAppSDK 2.0 announcement, Microsoft Q&A threads, WinUIEx notes) | Background for WinUI 3 transparency maturity | Not primary documentation; treated as indicative only |
| [Azure Artifact Signing pricing page](https://azure.microsoft.com/pricing/details/artifact-signing/) | Signing cost cross-check | Page showed placeholder values; cost taken from the Microsoft Learn page above |

## 4. Attempted but not loaded

| Source | Reason | Impact |
|---|---|---|
| Apple Human Interface Guidelines — Live Activities | Page is client-rendered; fetch returned only the title | Apple design guidance is **not** cited as evidence; the compact/minimal/expanded vocabulary in our docs is our own design language |
| https://lrclib.net/docs | Client-rendered | LRCLIB API terms OPEN (blocks public lyrics feature, see SECURITY_AND_PRIVACY_DESIGN) |
| Windows App SDK 2.x minimum .NET version | Not found in searched pages | Treated as OPEN; irrelevant to the recommended stack |

## 5. Local tools used as sources

| Tool | Version | Use |
|---|---|---|
| REA (`rea-agents`, MIT) | 6.8.0 | PE parsers, browser scenario capture |
| Microsoft Edge (headless, via REA/Playwright) | Chromium 156.0.4314.42 | Website observation |
