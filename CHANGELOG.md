### 3.0.8.4-community.4 (pending canonical release)

**Unofficial community continuation, not an upstream release. Local implementation, review, functional checks and performance audit are complete; canonical publication gates are tracked in `docs/universal-temperature-report.zh-CN.md`.**

- Automatic cross-brand temperature discovery through an embedded, pinned LibreHardwareMonitorLib 0.9.6 backend, isolated as an x64/net472 process while the main WPF/net40 executable retains C#4. CPU/GPU selection rejects non-finite/out-of-range values, hotspots, VRAM/ACPI-zone temperatures and threshold distances; each metric independently falls back to existing ASUS/NVAPI or already running LHM/OHM WMI providers.
- CPU access requires the exact trusted official standard PawnIO 2.1.0 image, valid Windows trust and device readiness on both sides. The fixed library's Intel GPU group shares that gate; NVIDIA/AMD GPU discovery remains independent. Existing shared/unknown/newer drivers are never overwritten or misreported as absent. Only an explicitly confirmed action can run the verified interactive installer; sampling, build/test, startup and updates never install drivers/runtimes or reboot the machine.
- A strict, bounded UTF-8 JSON Lines protocol, parent PID/creation identity, one serialized backend session, a kill-on-close Job, freshness/sequence checks, bounded stderr tail and 30-second failure backoff prevent old or faulty readings from surviving cancellation. Hiding the main window preserves a temperature-enabled compact strip; disabled/paused/disposed demand clears cached values without blocking UI.
- Keep the existing compact horizontal/vertical layout, click-through/no-focus behavior, saved position, themes, languages, cleanup, tray, settings, single-instance and checksum-verified update channel. The minimal settings status line and explicit driver confirmation do not add a permanent overlay frame/title or forced installation dialog.
- Same-build backend, 24-file embedded payload, main executable, full nine-analyzer safety builds and metadata/resource verification are mandatory for canonical packaging. The isolated CLR4 runner preserves the Manual/Desktop exclusions and 60-second per-test timeout. Current local safety evidence: main 551/551, backend 35/35, embedded 2/2; the latest isolated production window run records 75 passed, zero failed and one unavailable universal CPU check. This is not a claim about final CI binary performance or untested AMD/Intel hardware.
- Ship a separate hash-locked corresponding-source ZIP with PawnIO driver/API/tool, the required PawnPP gitlink contents, module 0.2.2 sources and compiler inputs; portable archives retain six full licenses and notices. Source packaging preserves upstream bytes and rejects same-length corruption without publishing a ZIP. Vendor installer-wrapper source/private signing credentials are not offered or claimed reproducible.
- Handle transient non-delete-sharing file reads during atomic cache publication with at most one second of background retry; permanent locks still fail without publishing a ready cache. Real NTFS file-lock regressions were verified RED→GREEN without relaxing hash/path/handle protection.
- Always clean up canceled backend preparation in a finally block, releasing its verified payload lease and allowing new demand to resume. Two real controlled-process regressions first failed on leaked leases, then passed without blocking the UI or launching late children.
- Preserve the selected layout and custom-position labels when switching languages by keeping fixed choice objects and translating their display content. Limit the driver confirmation to 600 DIP or the available work area so both Chinese and English prompts wrap readably; cancellation remains covered by the real window regression.
- Independently audited local performance evidence covers three 30-second warmup/60-second measurements and a 30/600-second retention run: combined normalized CPU medians about 0.096%, dispatcher p95 below 0.85 ms, and combined private-memory growth 5.266 MiB. The report pins the measured development binary and keeps final canonical-asset verification separate.
- Start the first-status timeout after the child launches, reclaim abandoned staging for the same manifest without following junctions, and send a session-bound stop with up to two seconds of background grace before terminating an unresponsive owned child. Invalid driver/installer paths fail verification without escaping as generic installation errors; the confirmed installer uses the system directory as its working directory.
- Refuse publishing from a branch other than community, and require an explicit HTTP 404 before treating the requested release tag as unoccupied.

### 3.0.8.3-community.3

**2026-10-18 (unofficial community continuation, not an upstream release)**

- Compact monitoring overlay: a horizontal strip by default (about 316×26 DIP with every item enabled) with a switchable vertical layout (about 118×78 DIP), per-item display toggles, a saved custom corner/position, mouse click-through on by default, no focus stealing, and no large frame, title bar or shadow; hiding the main window keeps an enabled strip visible
- Real CPU temperature from the ASUS ATK ACPI sensor that is already installed (DSTS GET 0x53545344, CPU sensor id 0x00120094, 1–150 °C sanity window, 30 s failure backoff) and GPU temperature from NVIDIA NVAPI; unavailable sensors show "—" instead of invented values, the LibreHardwareMonitor WMI namespace is queried only when its process is running, and no new drivers, fan, power or mode commands are involved
- Monitor-only mode hides the large shell while cleanup, tray, settings and themes stay available; process insights show one row per PID with CPU deltas and working-set/private/dedicated-GPU memory columns in Chinese and English, plus safe per-process actions (end tree, suspend, resume, priority) with a confirmation dialog
- Analyzer-clean release build: the whole source and test project now satisfy the full `AllRules.ruleset` under the pinned `LangVersion=4` (dispose patterns, culture-safe parsing, P/Invoke marshaling, collection property shapes); 413 safe tests run in CI, real-shell desktop fixtures are excluded from the headless safe suite, and both community workflows carry a 30-minute job timeout with per-test labels and a 60-second single-test timeout
- Community continuation of the monitoring work is documented in `docs/compact-monitoring-report.zh-CN.md`, `docs/monitoring-guide.zh-CN.md` and `docs/monitoring-acceptance-report.zh-CN.md`

### 3.0.8.2-community.2

**2026-10-06 (unofficial community continuation, not an upstream release)**

- Enabled the community update channel: `Constants.App.Repository` now points at the release assets of this repository (`releases/latest/download/AssemblyInfo.txt`, `.../WinMemoryCleaner.Community.exe`, `.../WinMemoryCleaner.Community.exe.sha256`) and `ReleaseChannelConfigured` is `true`, so *Auto Update* can be switched on; a download is still installed only after its published SHA-256 matched. Binaries released before this version cannot update themselves and must be replaced manually once
- Added SHA-256 verification to the self-update path: a downloaded executable is only installed after its published checksum matched. A missing checksum source, a failed download, a mismatch, or a checksum URL that is not HTTPS aborts the update, deletes the downloaded file, and logs the reason (`Helper.GetFileSha256`, `Helper.GetSha256FromChecksumText`, `Updater.IsVerifiableUpdateChannel`, `Constants.App.Repository.LatestExeHashUri`)
- `Helper.IsAutoUpdateSupported` now also requires a verifiable (HTTPS) checksum source, so a channel that publishes no checksum can never install an update
- Added a canonical release build workflow (`.github/workflows/community-release.yml`): release artifacts are produced with the same Visual Studio/Roslyn MSBuild as CI and are verified (published checksum vs. built executable, announced version vs. built source) before they are uploaded; passing a release tag also publishes them as a GitHub Release, which is what the update channel reads
- `build-community.ps1`: release packaging emits `.sha256` and `AssemblyInfo.txt` (the assets the updater consumes) plus the archive; the version is read from `AssemblyInfo.cs` instead of being duplicated; the script refuses to build when `<LangVersion>4</LangVersion>` is no longer pinned; packaging without `-MSBuildPath` warns; a locked output executable fails with the process id to stop; test reports are written to `build\test-results` inside the repository instead of `%TEMP%`
- Added 20 tests for checksum parsing (bare, `sha256sum` and `certutil` formats, invalid input), known file hashes, channel verifiability, and the guards that discard an update whose checksum is missing, unparsable, or does not match
- CI: the NUnit report is now read from the stable `build/test-results` path

### 3.0.8-community.1

**2026-10-05 (unofficial community continuation, not an upstream release)**

Baseline: PR [#204](https://github.com/IgorMundstein/WinMemoryCleaner/pull/204) (tray-notification AB-BA deadlock, monitor busy-wait, icon-render serialization) on top of upstream 3.0.8. Upstream items adopted via `cherry-pick -x`: [#205](https://github.com/IgorMundstein/WinMemoryCleaner/pull/205), [#206](https://github.com/IgorMundstein/WinMemoryCleaner/pull/206), [#207](https://github.com/IgorMundstein/WinMemoryCleaner/pull/207), [#208](https://github.com/IgorMundstein/WinMemoryCleaner/pull/208), [#209](https://github.com/IgorMundstein/WinMemoryCleaner/pull/209), [#188](https://github.com/IgorMundstein/WinMemoryCleaner/pull/188), [#186](https://github.com/IgorMundstein/WinMemoryCleaner/pull/186); two GCHandle fixes selected from [#195](https://github.com/IgorMundstein/WinMemoryCleaner/pull/195). Status of every open issue/PR: [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md).

- Community identity: `WinMemoryCleaner.Community.exe`, independent settings key `HKLM\SOFTWARE\WinMemoryCleaner.Community`, unsigned, auto-update off until a verified community channel exists; upstream settings are never read or migrated
- Fixed startup scheduled-task XML: UTF-16 bytes now match the declared encoding, values are XML-escaped, dates are culture-invariant, and the temp file is deleted in `finally` ([#179](https://github.com/IgorMundstein/WinMemoryCleaner/issues/179))
- Fixed update check gating: unsupported platform / disabled setting / interval-not-elapsed now skip the check entirely; TLS 1.3 flag removed (issue [#174](https://github.com/IgorMundstein/WinMemoryCleaner/issues/174))
- Completed Serbian (Latin) translation keys added by #208 ([#186](https://github.com/IgorMundstein/WinMemoryCleaner/pull/186) × [#208](https://github.com/IgorMundstein/WinMemoryCleaner/pull/208) integration)
- Added compile-time isolated test build (`CommunityTestBuild=true`) that never touches real settings, scheduled tasks, or event log; default run excludes `Manual`/`Desktop` categories
- Added `build-community.ps1` (build / test / package) and a non-publishing CI workflow; upstream signing, release, and package-manager workflows are parked under `.github/workflows-disabled/`

### 3.0.8

**2025-12-13**

- Added /Reset command line option for troubleshooting purposes
- Added logic to delete orphaned old update files created by the old update mechanism, which are no longer needed ([#168](https://github.com/IgorMundstein/WinMemoryCleaner/issues/168))
- Added settings to turn the start menu shortcut creation on or off ([#169](https://github.com/IgorMundstein/WinMemoryCleaner/issues/169))
- Added unit tests to enhance code testability
- Improved memory usage display on tray icon ([#170](https://github.com/IgorMundstein/WinMemoryCleaner/issues/170))
- Updated documentation with missing changes from previous versions
- UI & Code enhancements

### 3.0.7

**2025-11-20**

- Added start menu shortcut creation when the app opens ([#160](https://github.com/IgorMundstein/WinMemoryCleaner/issues/160))
- Removed the code that tried to validate the code signature, as it was causing errors with package managers
- UI & Code enhancements

### 3.0.6

**2025-11-18**

- Fixed tooltip text for the system tray icon. Windows restricts tooltip length to 64 characters, and in some languages, this limit is exceeded when the virtual memory display is enabled. ([#148](https://github.com/IgorMundstein/WinMemoryCleaner/issues/148)) ([#155](https://github.com/IgorMundstein/WinMemoryCleaner/issues/155)) ([#159](https://github.com/IgorMundstein/WinMemoryCleaner/issues/159))

### 3.0.5

**2025-11-17**

- Console mode hotfix ([#156](https://github.com/IgorMundstein/WinMemoryCleaner/issues/156))

### 3.0.4

**2025-11-17**

- Added a setting to set the color of the tray icon when optimization is running
- Added a setting to trigger optimization to the tray icon using the middle mouse click ([#141](https://github.com/IgorMundstein/WinMemoryCleaner/issues/141))
- Added a tray icon rotate effect when optimization is running
- Added support for European Portuguese (PT) language
- Enhanced the run on startup feature for desktop and laptop computers ([#137](https://github.com/IgorMundstein/WinMemoryCleaner/issues/137)) ([#139](https://github.com/IgorMundstein/WinMemoryCleaner/issues/139))
- Fixed app crash after Windows hibernation ([#145](https://github.com/IgorMundstein/WinMemoryCleaner/issues/145))
- Fixed auto update ([#148](https://github.com/IgorMundstein/WinMemoryCleaner/issues/148)) ([#151](https://github.com/IgorMundstein/WinMemoryCleaner/issues/151))
- Removed unnecessary code related to package managers (Chocolatey, Scoop, and WinGet) due to a new strategy for package publication
- UI & Code enhancements
- Updated Arabic and Korean translations

### 3.0.2

**2025-09-06**

- Enhanced code and user interface
- Enhanced functionality to run on startup and improved package manager support (Chocolatey, Scoop, and WinGet)
- Fixed a bug that prevented the "Run On Priority" setting from being saved.
- Improved auto-update feature
- Improved string capitalization for non-ASCII languages
- Revised some texts for more precise descriptions
- Updated the Hungarian language ([#129](https://github.com/IgorMundstein/WinMemoryCleaner/issues/129))

### 3.0.0

**2025-08-02**

- Added a help `?` menu
- Added a security check to verify the code certificate and warn if the user downloaded from an untrusted source. It's not bulletproof because the project is open source, but it makes it harder for people with bad intentions
- Added a setting to reduce or increase the font size. It's helpful for different screen sizes and resolutions
- Added an option to turn off the optimization hotkey ([#94](https://github.com/IgorMundstein/WinMemoryCleaner/issues/94))
- Added app to package managers: Chocolatey, Scoop, and Winget ([#89](https://github.com/IgorMundstein/WinMemoryCleaner/issues/89))
- Added code digital signature provided by SignPath.io to ensure authenticity and user safety
- Added donation options to the new help `?` menu and on GitHub
- Added GitHub workflows to enhance release delivery and trustworthiness on malicious scanner websites
- Added localization for Hebrew, Hungarian, Norwegian, and Thai languages
- Added optimization reason (Low Memory, Manual, or Schedule) on notifications and logs ([#110](https://github.com/IgorMundstein/WinMemoryCleaner/issues/110))
- Added reset to default settings feature to the new help `?` menu
- Added support to run as a Windows Service ([#96](https://github.com/IgorMundstein/WinMemoryCleaner/issues/96))
- Added two new memory areas: modified file cache and registry cache
- Enhanced run on startup feature ([#91](https://github.com/IgorMundstein/WinMemoryCleaner/issues/91)) ([#108](https://github.com/IgorMundstein/WinMemoryCleaner/issues/108))
- Enhanced text formats for better translations
- Enhanced tray icon customizations for memory usage ([#111](https://github.com/IgorMundstein/WinMemoryCleaner/issues/111)) ([#112](https://github.com/IgorMundstein/WinMemoryCleaner/issues/112))
- Improved code, documentation and user interface ([#92](https://github.com/IgorMundstein/WinMemoryCleaner/issues/92)) ([#103](https://github.com/IgorMundstein/WinMemoryCleaner/issues/103))
- Modified window event log messages to JSON format
- Moved the About this project link to the new help menu
- Moved Windows registry path from the current user to the local machine
- Renamed memory area system working set to system file cache
- Renamed memory processes working set to working set

**Notes (This project is celebrating its 6th year. So, nothing better than a major update release.)**

- It's the first release implementing the CI/CD pipelines to publish the app to Chocolatey, Scoop, and Winget. So, there may be a delay in being available on these package managers.
- If you find this app helpful, please consider donating. Your donation helps keep the project alive, optimized, and free for everyone.
- If you run the app with command-line arguments (no GUI), check the modified memory area parameter names.
- Many contributors often provide translation updates, as some texts might not be in the best format due to frequent translation revisions. After we introduce the CI/CD workflows, we expect to publish releases more often to update minor corrections quickly. We now use the 'major.minor.patch' format for the app version, which will allow us to launch patch releases for localizations.
- We prioritize transparency and user safety. Since version 3.0.0, we have been digitally signing our files through [SignPath.io](https://about.signpath.io/product/open-source) using a free certificate provided under the [SignPath Terms of Use](https://signpath.org/terms). The project received the certificate in recognition of its popularity and public value in the open-source community. This process ensures that we distribute authentic files that have not been tampered with. By doing this, we will build trust with Microsoft Defender SmartScreen over time, and maybe someday we will obliterate that "Windows protected your PC" warning.

### 2.8

**2023-12-24**

- Added optimization progress bar to the optimize button
- Improved auto-update task
- Improved code & UI
- Improved memory usage tray icon

### 2.7

**2023-12-22**

- Changed the error dialog to a warning log event when a firewall blocks the app
- Improved Chinese (Simplified/Traditional), French, Korean, and Serbian languages
- Improved code
- Improved memory usage tray icon

### 2.6

**2023-12-17**

- Added Albanian, Bulgarian, Irish, Persian, and Russian languages
- Added approximate memory released to optimization notification
- Added right-to-left (RTL) language and UI support
- Added run on low priority setting. If enabled, it limits the app's resource usage by reducing the process priority and ensuring it runs efficiently. It might increase the optimization time, but it helps if your Windows freezes during it
- Added show virtual memory setting and memory usage view
- Added support for cultures' native formats, like decimal separators
- Added tray icon customization. Users can choose between the default app image or show physical memory usage with a background color based on the value. (0% - 79%) White | (80% - 89%) Orange | (90% - 100%) Red
- Improved code, documentation, and UI
- Improved Greek language
- Improved UI rendering when the start minimized setting is enabled

### 2.5

**2023-08-20**

- Added optimization runtime stats to the log
- Added **compact mode** view. Click the arrow at the top right of the screen to collapse or expand the window
- Changed app priority to low. Optimization may run a little slower, but it will reduce the chance of Windows freezing during the optimization
- Improved code and UI
- Improved Greek and Polish languages

### 2.4

**2023-08-07**

- Added Polish and Ukrainian languages
- Improved code and documentation

### 2.3

**2023-08-04**

- Added Korean and Serbian languages
- Improved Code & UI
- Improved Slovenian language
- Signed all executable versions using a personal self-code signing certificate. It reset the downloads counter

### 2.2

**2023-08-02**

- Added optimization hotkey setting
- Added Arabic, Indonesian, and Japanese languages
- Fixed bugs
- Improved Code & UI
- Improved German language

### 2.1

**2023-07-27**

- Added close after optimization setting
- Added support for the Chinese (Simplified), Chinese (Traditional), Dutch, French, German, Greek, Italian, Macedonian, Slovenian, Spanish, and Turkish languages
- Added the ability to read language JSON files at the exact executable location. That will help contributors to test the translation before submitting it
- Added global hotkey (CTRL + ALT + M) to optimize
- Code improvements and bug fixes
- Modified notify icon title to show the memory usage
- Modified the default window focus to the Optimize button. That will allow the user to press ENTER to run the optimization after the app starts

### 2.0

**2023-03-26**

-  Always on top
-  Auto clean (Interval & Usage)
-  Auto app update
-  Code cleaning & optimizations
-  Localization (English/Portuguese)
-  Minimize the app to the system tray when closed
-  New UI (Darker)
-  Processes the exclusion list
-  Run on startup
-  Show optimization notifications
-  Start minimized
-  System tray icon (Notifications/Optimize/Exit)
-  Windows Server 2003 and Windows XP 64-bit support

### 1.1

**2021-09-06**

* Initial release deprecated. Files updated to 2.0 because it has the auto-update feature
