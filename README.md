> [!IMPORTANT]
> **Unofficial community continuation.** This repository continues [IgorMundstein/WinMemoryCleaner](https://github.com/IgorMundstein/WinMemoryCleaner) (last upstream activity December 2025). It is **not** affiliated with or endorsed by the original author and is **not** an official handover.
>
> - Original work © **Igor Mundstein**, **GPL-3.0** — full history, license, and attribution are preserved; see [CREDITS.md](CREDITS.md) and [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md).
> - Community binary: `WinMemoryCleaner.Community.exe` ("WinMemoryCleaner Community"), independent settings key `HKLM\SOFTWARE\WinMemoryCleaner.Community`, **auto-update pulls from this repository's checksum-verified release channel** (the SHA-256 sidecar is verified before anything is installed).
> - Community builds are **unsigned**; the signed official binary remains the [upstream release](https://github.com/IgorMundstein/WinMemoryCleaner/releases/latest).

# Windows Memory Cleaner

[![](https://img.shields.io/badge/WINDOWS-XP%20%E2%80%93%2011-blue?style=for-the-badge)](#windows-memory-cleaner) [![](https://img.shields.io/badge/SERVER-2003%20%E2%80%93%202025-blue?style=for-the-badge)](#windows-memory-cleaner) [![](https://img.shields.io/github/license/IgorMundstein/WinMemoryCleaner?color=2ea44f&style=for-the-badge)](/LICENSE) [![](https://img.shields.io/github/downloads/IgorMundstein/WinMemoryCleaner/total?color=orange&style=for-the-badge)](https://github.com/IgorMundstein/WinMemoryCleaner/releases/latest)

WMC is a free RAM cleaner that effectively optimizes memory areas by utilizing the native Windows API. This can help improve performance when programs do not properly release allocated memory. Featuring a user-friendly interface and intelligent functionality, this portable application requires no installation; however, it does need administrator privileges to run.

[![](./docs/assets/images/main-window.png)](#windows-memory-cleaner)

## 💾 Download
> **Community note**: community binaries are published on this repository's [Releases](https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest) page (unsigned, built by CI) and can be built from source (see *Build from source* below). The badge and package-manager links below belong to the **official upstream** distribution by Igor Mundstein.

[![](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fapi.github.com%2Frepos%2FIgorMundstein%2FWinMemoryCleaner%2Freleases%2Flatest&query=%24.tag_name&label=Release&style=for-the-badge)](https://github.com/IgorMundstein/WinMemoryCleaner/releases/latest/download/WinMemoryCleaner.exe)

### 🍫 [Chocolatey](https://community.chocolatey.org/packages/winmemorycleaner)

```cmd
choco install winmemorycleaner
```

### 🍦 [Scoop](https://scoop.sh/#/apps?q=winmemorycleaner&o=true&dm=true&n=true)

```cmd
scoop bucket add extras
```

```cmd
scoop install extras/winmemorycleaner
```

### 📦 [WinGet](https://winstall.app/apps/IgorMundstein.WinMemoryCleaner)

```cmd
winget install IgorMundstein.WinMemoryCleaner
```

### 🛠️ Build from source (community)

The **main WPF executable remains .NET Framework 4.0 / C# 4**. Its embedded sensor backend is **Windows x64 / .NET Framework 4.7.2 / C# 4**, with fixed LibreHardwareMonitorLib **0.9.6**. Restore the main project's NuGet packages before building:

```powershell
nuget restore .\src\packages.config -PackagesDirectory .\src\packages -NonInteractive

# Development only: the main project's in-box MSBuild does not run its analyzers.
# The backend still REQUIRES Roslyn and the complete analyzer set; no C# 5 fallback.
powershell -ExecutionPolicy Bypass -File .\build-community.ps1 -Tests -SensorCompilerPath 'C:\path\to\Roslyn\csc.exe'

# Canonical local equivalent: Visual Studio MSBuild builds backend, payload and main EXE together.
powershell -ExecutionPolicy Bypass -File .\build-community.ps1 -Tests -Package -MSBuildPath 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
```

**Released binaries come only from [Community release build](.github/workflows/community-release.yml).** Packaging without Visual Studio MSBuild, `-Tests`, a missing Roslyn compiler/analyzer, a changed language pin, or a missing payload fails explicitly. Both own-code projects use `AllRules.ruleset` and warnings-as-errors; the pinned third-party binaries are not claimed to satisfy our C# 4 source rules. The build restores hash-locked sensor inputs, compiles both safety suites, embeds 24 verified files, and compares the EXE's resources with the same build's inputs without executing them. The isolated NUnit runner uses CLR 4, labels and the unchanged 60-second per-test timeout; only the existing Manual/Desktop categories are excluded. It also builds and tests a separate hash-locked corresponding-source ZIP (driver/API/tool, PawnPP submodule, module sources/compiler inputs); the portable ZIP includes six complete license texts and provenance notices. The official installer wrapper source/signing credentials are not publicly available and are not offered by that bundle.

Reports stay in `build\test-results`, generated inputs in `build\sensors`, and publishable packages in `dist`. Locked outputs fail rather than stopping a user process. No build, test or update installs a runtime or driver.

## 🚀 Features

| Feature | Description |
|:---|:---|
| **Always&nbsp;on&nbsp;Top** | Pins the main application window so it is always visible above other windows. |
| **Auto&nbsp;Optimization** | Set the app to clean memory automatically, either by period (`Every X hours`) or when free physical RAM drops below a specified threshold (`When free physical memory is below X percent`). |
| **Auto&nbsp;Update** | Automatically checks for new versions every 24 hours to keep the application up to date. |
| **Close&nbsp;after&nbsp;Optimization** | The application will automatically close after a memory optimization is completed. |
| **Close&nbsp;to&nbsp;Notification&nbsp;Area** | Minimizes the app to the system tray instead of closing when the 'X' button is clicked. |
| **Compact&nbsp;Mode** | Collapse the main window into a minimal view for at-a-glance monitoring. |
| **Font&nbsp;Size&nbsp;Adjustment** | Customize the font size for different screen sizes and resolutions. |
| **Global&nbsp;Hotkey** | Trigger an optimization instantly from anywhere with a customizable hotkey (default `CTRL+SHIFT+M`). |
| **Hotkey&nbsp;Toggle** | Enable or disable the global optimization hotkey independently. |
| **Multi-Language&nbsp;Support** | Albanian, Arabic, Bulgarian, Chinese (Simplified), Chinese (Traditional), Dutch, English, French, German, Greek, Hebrew, Hungarian, Indonesian, Irish, Italian, Japanese, Korean, Macedonian, Norwegian, Persian, Polish, Portuguese (Brazil), Portuguese (Portugal), Russian, Serbian (Cyrillic), Serbian (Latin), Slovenian, Spanish, Thai, Turkish, and Ukrainian. |
| **Working&nbsp;Set&nbsp;Exclusions** | Exclusions skip only per-process working-set trimming; other selected global memory cleanups and automatic optimization remain unaffected. |
| **Run&nbsp;on&nbsp;Low&nbsp;Priority** | Limits the app's resource usage by reducing its process priority. This may increase optimization time but can prevent system freezes. |
| **Run&nbsp;on&nbsp;Startup** | Automatically starts the application when Windows boots by creating a task in the Windows Task Scheduler. |
| **Show&nbsp;Optimization&nbsp;Notifications** | Display system tray notifications after each optimization, showing the reason and amount of memory freed. |
| **Show&nbsp;Virtual&nbsp;Memory** | Displays virtual memory (page file) usage in the main window and system tray text. |
| **Start&nbsp;Menu&nbsp;Shortcut** | Automatically creates a Start Menu shortcut for quick access to the application. |
| **Start&nbsp;Minimized** | The application will start minimized directly to the system tray. A single-click on the tray icon restores it. |
| **Tray&nbsp;Icon&nbsp;Customization** | Customize tray icon colors (background, text, warning, danger, optimizing), display memory usage, set threshold levels, and enable middle-click optimization. |
| **Resource Monitoring** | Live CPU / GPU usage and a physical-memory gauge with the data source named for every readout; refresh interval selectable from 1 / 2 / 5 seconds. |
| **Process Insights & Safe Actions** | Searchable, sortable process list (memory, normalized CPU, GPU usage, dedicated VRAM) with identity-verified working-set trim and guarded termination of the selected process. |
| **Monitoring Overlay** | Compact always-on-top strip — horizontal by default, switchable to vertical — that is click-through, never steals focus, saves its position, and offers a "monitor only" mode that hides the whole main window. |
| **Real CPU / GPU Temperatures** | Automatic, identified readings from the embedded LibreHardwareMonitor 0.9.6 backend, with independent ASUS / NVIDIA NVAPI / LHM-OHM WMI fallbacks; absent or stale readings show a reason, never a fake 0 °C. |

### ↕️ Compact Mode

The Compact Mode feature allows you to collapse the main window into a minimal view for at-a-glance monitoring. By clicking the arrow next to the minimize button, the UI shrinks to show only the most essential memory statistics and controls. This mode is ideal for users who want to keep an eye on their system’s memory usage without occupying much screen space. Toggle Compact Mode on or off at any time to suit your workflow.

[![](./docs/assets/images/main-window-compact.png)](#%EF%B8%8F-compact-mode)

### 📊 Community Monitoring

On top of the original cleaner, the community edition adds an on-demand monitoring layer. **Our acquisition code only enables CPU/GPU and never calls fan, power, voltage, overclock or performance-mode controls.** It does not automatically install anything; an optional official PawnIO driver install is a separate, explicitly confirmed user action. Underlying vendor measurement APIs can use read/write-capable native handles and IOCTLs, so this is not a claim that every upstream access has read-only permission bits.

- **Resource overview**: a physical-memory gauge plus paired CPU / GPU usage readouts, each labelled with its data source (Windows GetSystemTimes, Windows PDH).
- **Process page**: enumerate every visible process (duplicate names keep separate PID rows), search by name or PID, sort by any metric; unavailable values sort last and never pretend to be zero.
- **Safe process actions**: release the working set of a selected process, or end it after an explicit confirmation dialog. Identity checks and the operation itself use the same native process handle, so PID reuse cannot redirect an action; critical, protected and own processes are refused.
- **Compact monitoring strip**: replaces the old fixed-width floating panel with three tight CPU / GPU / RAM groups — about 316×26 DIP horizontal, 118×78 DIP vertical with all items enabled. It stays topmost, is mouse-through by default, never takes focus, supports drag positioning and per-monitor corner placement, and both the layout (horizontal / vertical) and position persist.
- **Monitor-only mode**: one click enables the strip and hides the main window; the tray icon reopens settings. Nothing else about your close-button preference changes, and sampling continues while the window is hidden.
- **Honest, automatic temperatures**: the embedded x64/net472 backend probes supported Intel/AMD CPUs and NVIDIA/AMD/Intel GPUs; a separate LibreHardwareMonitor application is not needed for this path. CPU requires the pinned official standard PawnIO 2.1.0 image, valid Windows trust and a ready device, checked in both processes. Unknown/shared, newer, unsigned or unrestricted drivers are not overwritten or treated as absent. Fixed LHM 0.9.6 couples Intel GPU enumeration to that same CPU/driver gate; NVIDIA/AMD GPU probing stays independent. This is broad detection, **not an all-hardware guarantee**.
- **Independent fallbacks**: each missing CPU/GPU reading can use the existing ASUS CPU sensor (`0x00120094`) / NVIDIA NVAPI, then an already-running default-named LibreHardwareMonitor or OpenHardwareMonitor WMI provider. Details retain the actual device/sensor/provider and the universal failure reason. CPU candidates follow explicit package/Tdie/combined/average/CCD/core rules; GPU uses core temperature only. Hotspot, VRAM, ACPI zones, distance-to-threshold and invalid/non-finite/out-of-range values never become a fake core temperature.
- **Explicit driver consent**: the settings status line explains component/driver readiness. Clicking **Install universal temperature driver** opens a confirmation before the unmodified, signature-checked official installer can start. Cancellation performs no install. Security-policy blocks, failure or reboot requirements are reported without changing Secure Boot/HVCI or rebooting Windows; an existing shared driver is never replaced or uninstalled by the cleaner.
- **On-demand sampling and cost**: with temperatures demanded there is at most one owned backend, prepared from the EXE into `%LOCALAPPDATA%\WinMemoryCleaner.Community\Sensors`. A clean EXE-only update can prepare its own versioned payload; no companion DLLs are required next to the download. This adds compressed EXE size, disk cache and a separate process/private-memory cost. Switching off demand invalidates cached readings immediately and stops the owned child asynchronously; with the overlay off and shell hidden, CPU/GPU, temperature and per-process sampling stop. Only the tray memory display and the auto-optimization check keep their low cadence. Missing .NET 4.7.2/x64 support fails honestly with fallbacks, without installing a runtime.

New-version evidence is tracked in the [universal temperature verification report](docs/universal-temperature-report.zh-CN.md): local complete-analyzer builds, main safety **551/551**, isolated backend safety **35/35**, actual embedded preparation **2/2**, and the real-window regression **14/14** have passed. One independent review found no Critical/Important defect; the verified minor lifecycle, cleanup, path and release-guard issues are fixed. The latest isolated production-window run before those review fixes records **75 passed / 0 failed / 1 unavailable**: real RTX 4080 core temperatures and ASUS CPU fallback are distinguished from the unavailable trusted-driver universal CPU path. An independent audit of 780 local measurement samples found combined CPU medians around **0.096%**, dispatcher p95 below **0.85 ms**, and ten-minute private-memory growth **5.266 MiB**; the report pins that development binary and keeps final canonical CI/publication verification separate. The [Chinese guide](docs/monitoring-guide.zh-CN.md) explains driver and compatibility limits. The [2026-10-17 compact report](docs/compact-monitoring-report.zh-CN.md) and [historical ledger](docs/monitoring-progress.md) describe **v3.0.8.3-era evidence only**, not this backend's performance or universal CPU verification.

### 🔔 System Tray (Notification Area)

The application provides quick access and information directly from the system tray.

- **Menu**: A right-click menu offers quick access to trigger an optimization or exit the application.

  - Optimize
  - Exit
 
- **Notification**: After an optimization, a notification appears showing the reason and the approximate amount of memory that was freed.

  [![](./docs/assets/images/notification.png)](#-system-tray-notification-area)

- **Tray Icon Customization**: The tray icon can be configured to:

  - Customize background, danger, optimizing, text, and warning colors
  - Display real-time physical memory usage instead of the app logo
  - Enable middle mouse click to trigger optimization
  - Set danger and warning level thresholds that automatically change icon colors
  - Use transparent background when showing memory usage
  - Visual rotation effect during optimization

## 🧬 Technical Deep Dive: How It Works

WinMemoryCleaner provides a user-friendly interface for powerful, documented Windows API functions. There are no tricks or secrets—just direct access to the tools needed to manage your system's memory effectively. Each cleaning function targets a specific memory area, and its availability depends on your Windows version.

Here’s a breakdown of what each function does and the minimum supported Windows version required to use it:

| Memory Area | Description | Windows | Server |
| :--- | :--- | :---: | :---: |
| **Combined&nbsp;Page&nbsp;List** | Flushes memory blocks from the page-combining list, a memory-saving feature in modern Windows that merges identical pages of memory. | 8+ | 2012+ |
| **Modified&nbsp;File&nbsp;Cache** | Flushes the volume file cache to disk for all fixed drives, ensuring all pending writes are committed. | XP+ | 2003+ |
| **Modified&nbsp;Page&nbsp;List** | Writes unsaved pages from RAM to disk and moves the now-saved pages to the standby list. | Vista+ | 2008+ |
| **Registry&nbsp;Cache** | Flushes registry hives from memory. Hives are logical groups of keys and values that are loaded into memory when the OS starts or a user logs in. | 8.1+ | 2012+ |
| **Standby&nbsp;List** | Clears the entire Standby List, which contains cached data from closed applications. This aggressive method frees the maximum amount of cached RAM for demanding tasks. | Vista+ | 2008+ |
| **Standby&nbsp;List&nbsp;(low&nbsp;priority)** | Clears only the lowest-priority pages from the Standby List. This gentle method frees some cached RAM without removing data that Windows considers more important. | Vista+ | 2008+ |
| **System&nbsp;File&nbsp;Cache** | Flushes the cache Windows uses for its system files, trimming it to release memory. Useful for refreshing the system’s state before launching a memory-intensive application. | XP+ | 2003+ |
| **Working&nbsp;Set** | Removes memory from all user-mode and system working sets, forcing processes (like games or browsers that hoard memory) to release non-essential RAM. This can reduce stutter and improve responsiveness. | XP+ | 2003+ |

## 🔴 The Problem: Inefficient Memory Management

Modern operating systems are good at managing memory, but they aren't perfect. Over time, RAM can become cluttered with cached data from closed applications (**Standby List**) or held unnecessarily by running processes (**Working Set**). This leads to system slowdowns, stuttering in applications, and reduced responsiveness, especially on systems with limited RAM.

The market for PC utilities is plagued by "RAM boosters" that use deceptive tricks and offer no real, verifiable benefits, creating deep-seated skepticism among users.

## ✅ The Solution: A Transparent, Evidence-Based Tool

WinMemoryCleaner is the antidote to "snake oil" utilities. It does not use undocumented hacks or harmful tricks. Instead, it provides a clean, user-friendly interface to powerful, **native Windows API functions** that give you direct control over your system's memory. It is a tool built on transparency, proof, and respect for the user.

## 🔎 Proof of Concept: See It Work Yourself

Don't take our word for it. You can verify the effects of this tool using Windows' own **Resource Monitor**.

1. Open Resource Monitor (search `resmon.exe` in the Start Menu).
2. Go to the **Memory** tab. Observe the blue "Standby" portion of the bar. This is RAM used for caching files from closed programs.
3. Now, open and close a few large applications (a game, a browser, Photoshop). Watch the blue "Standby" section grow.
4. In WinMemoryCleaner, select **only the `Standby List`** and click `Optimize`.
5. Watch the Resource Monitor again. The blue "Standby" memory will instantly drop, and the light green "Free" memory will increase by the same amount.

This is a direct, verifiable demonstration that the application converts cached memory into truly free memory, ready for your next task.

## 🔒 Trust & Integrity
> **Community note**: the CI/CD signing pipeline and SignPath certificate described in this section belong to the **upstream** project. Community continuation builds are **unsigned** — verify the SHA-256 hash or build from source yourself.

We understand that users are rightfully skeptical of system utilities. This project is built on a foundation of verifiable trust and transparency.

### 🔑 Automated & Secure Builds (CI/CD)

Every community release is built and published by **GitHub Actions**, with no manual binary upload: [Community CI](.github/workflows/community-ci.yml) builds production and isolated-test main executables plus the same-build backend, runs both safety suites, and verifies the embedded payload on code pushes. [Community release build](.github/workflows/community-release.yml) does the same with Visual Studio/Roslyn MSBuild, then packages the EXE, `.sha256`, `AssemblyInfo.txt`, archive, third-party notices and corresponding-source bundle. Checksum, version and payload checks run before publication; an existing tag/asset is never replaced. The upstream signing, release, and package-manager workflows remain preserved but disabled in [`.github/workflows-disabled/`](.github/workflows-disabled).

### 🔁 Download Verification (Self-Update)

Self-update never installs a download it cannot verify. When *Auto Update* is enabled, the updater:

1. reads the announced version from the channel's `AssemblyInfo.txt` and stops unless it is newer than the running version;
2. downloads the new executable;
3. downloads the published SHA-256 checksum of that executable and compares it with the hash of the downloaded file;
4. only on an exact match does it replace the executable (terminate, move, restart). A missing or non-HTTPS checksum source, a download error, a mismatch, or an unexpected version aborts the update, deletes the downloaded file, and writes the reason to the log.

Because steps 3 and 4 are mandatory, a channel that publishes no checksum is treated as unsupported (`Helper.IsAutoUpdateSupported` requires an HTTPS checksum source).

**Community update channel:** it is enabled and served by this repository's releases. The updater reads `https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/` plus `AssemblyInfo.txt`, `WinMemoryCleaner.Community.exe` and `WinMemoryCleaner.Community.exe.sha256` — every community release publishes those exact asset names. *Auto Update* still defaults to off, so nothing is downloaded until you enable it in the settings; any download that cannot be verified is deleted and the running executable is left alone. A checksum published next to the binary protects against a corrupted, truncated, or substituted download — it is not a code signature.

### 🔑 Verifiable Code Signing

Since version 3.0.0, we have been digitally signing our files through [SignPath.io](https://about.signpath.io/product/open-source) using a free certificate provided under the [SignPath Terms of Use](https://signpath.org/terms). The project received the certificate in recognition of its popularity and public value in the open-source community. This process ensures that we distribute authentic files that have not been tampered with.

A digital signature proves two things:
* **Authenticity:** The publisher of the file is who they say they are.
* **Integrity:** The file has not been altered or tampered with since it was signed.

You can verify the signature by right-clicking the `.exe` -> `Properties` -> `Digital Signatures`

[![](./docs/assets/images/digital-signature.png)](#-trust--integrity)

### 🔑 Microsoft Defender SmartScreen

Even with a valid digital signature, Microsoft Defender SmartScreen may initially flag a new release with a "Windows protected your PC" warning.

[![](./docs/assets/images/microsoft-defender-smart-screen.png)](#-microsoft-defender-smartscreen)

This happens because the application is new and has not yet built a strong reputation with Microsoft. This is a standard, reputation-based security measure. By running the app, you help it build a positive reputation, which will cause this warning to disappear more quickly for other users. We appreciate your trust and understanding.

### 🔑 Independent Security Scans

Each new version is automatically submitted for analysis to leading security platforms, including VirusTotal and Hybrid Analysis, to ensure it is free from threats.

## 💻 Automation & Deployment

You must run these headless operations with administrator privileges.

### 🔳 Console Mode

Run optimizations silently for scripting and automation. Use any combination of the following arguments:

- `/CombinedPageList`
- `/ModifiedFileCache`
- `/ModifiedPageList`
- `/RegistryCache`
- `/StandbyList` or `/StandbyListLowPriority`
- `/SystemFileCache`
- `/WorkingSet`

**> Command-line example:**

```cmd
{path}\WinMemoryCleaner.exe /ModifiedFileCache /StandbyList /WorkingSet
```

### ⚙️ Windows Service Mode

For continuous, hands-off optimization, install the application as a background service. The installation will close some processes to install or uninstall the service without requiring a system restart, and log files will be generated along with the .exe file. Some application settings will be modified based on recommendations. You can still open the application (GUI) and configure it as desired. The service will utilize these settings.

✅ **Install Service:**
```cmd
{path}\WinMemoryCleaner.exe /Install
```

❌ **Uninstall Service:**
```cmd
{path}\WinMemoryCleaner.exe /Uninstall
```

## 🔧 Troubleshooting

### ⚠️ Application flagged as Malware/Virus:

One of the reasons for this **false alarm** is that the application adds entries to the registry and creates a scheduled task to run at startup. Windows doesn't suggest letting applications with administrator privileges run at startup. I understand that, but this is the required method for this functionality. I apologize, but the application cannot perform a deep memory clean without administrator privileges.

This is a common issue that persists with every new app version. I constantly submit the executable to Microsoft. Usually, it takes up to 72 hours for Microsoft to remove the detection.

**Solutions:**

- **Recommended: Use a Package Manager** — If you frequently encounter false positive detections, auto-update issues, or SmartScreen warnings, we **strongly recommend** installing via [Chocolatey](#-chocolatey), [Scoop](#-scoop), or [WinGet](#-winget). These package managers provide:
  
  - Automated update mechanisms that bypass Windows security warnings
  - Community-verified distribution channels
  - No manual download/verification steps required
  - Reduced exposure to false positive antivirus flags
  - Simplified version management with single-command updates

  **Example update commands:**

  ```cmd
  choco upgrade winmemorycleaner
  ````
  
  ```cmd
  scoop update winmemorycleaner
  ```

  ```cmd
  winget upgrade IgorMundstein.WinMemoryCleaner
  ```

- If auto-update fails or you need a verified build, download the latest release from the **Download** section above or the Releases page and manually replace your existing `WinMemoryCleaner.exe`. Run the new file as administrator.
- Long-term solution: please submit the app to Microsoft for malware analysis to reduce false positives: [submit for analysis](https://www.microsoft.com/en-us/wdsi/filesubmission)
- Quick workaround: add an exclusion to your antivirus app allowlist or to [Windows Security](https://support.microsoft.com/en-us/windows/add-an-exclusion-to-windows-security-811816c0-4dfd-af4a-47e4-c301afe13b26)

#### Verify & replace executable (safe replacement)

If you suspect the binary is corrupted, tampered, or the auto-update fails, follow these safe-replacement steps.

- Simple (non-technical):

  1. Download the latest release from the **Download** section above or the Releases page on GitHub.
  2. Right-click the downloaded file → **Properties** → **Digital Signatures** and verify the signer and timestamp.
  3. Rename your current `WinMemoryCleaner.exe` to `WinMemoryCleaner.exe.bak` (backup).
  4. Move the new `WinMemoryCleaner.exe` into place and run it as administrator.
  5. If the new version works, delete the `.bak`. To roll back, restore the `.bak` file.

- Advanced (recommended best practices):

  1. Export current settings (registry): `reg export "HKLM\Software\WinMemoryCleaner" WinMemoryCleaner-reg-backup.reg`
  2. Download the official release and obtain the published SHA256 from the release notes (if provided).
  3. Verify the file hash:

     ```powershell
     Get-FileHash .\WinMemoryCleaner.exe -Algorithm SHA256
     ```

-   4. Verify the Authenticode signature:
  
     ```powershell
     Get-AuthenticodeSignature .\WinMemoryCleaner.exe
     ```

     Confirm `Status` is `Valid` and signer is expected.

  5. Stop running instances (and scheduled task if present) before replacing:

     ```powershell
     Stop-Process -Name WinMemoryCleaner -ErrorAction SilentlyContinue
     ```

  6. Backup and replace safely:

     ```powershell
     Move-Item .\WinMemoryCleaner.exe WinMemoryCleaner.exe.bak
     Copy-Item C:\path\to\downloaded\WinMemoryCleaner.exe .\
     Start-Process .\WinMemoryCleaner.exe -Verb RunAs
     ```

  7. Check Event Viewer (`eventvwr` → Windows Logs → Application) for errors. Roll back by restoring the `.bak` and re-enabling tasks if needed.

### 🔄 Reset to Factory Defaults

**⚠️ Use this to restore factory defaults** - If the app keeps crashing, won't open, or is stuck in a loop, this will terminate frozen windows, disable auto-update, and restore the application to its factory default state.

**What happens when you reset to factory defaults:**

- All your custom settings are reverted to the application's factory defaults (your language choice is preserved)
- Any frozen or stuck app windows will be closed
- Auto-update is turned off and must be re-enabled manually in the application settings
- The app returns to its original state

**Command to run (one-line)**

```cmd
{path}\WinMemoryCleaner.exe /Reset
```

**Step-by-step (guided)**

1. Click **Start**, type `cmd`.
2. Right-click **Command Prompt** → **Run as administrator**.
3. Type (or drag-and-drop) the full path to `WinMemoryCleaner.exe`, then type a space and add `/Reset`.

   Example:

   ```cmd
   "C:\Downloads\WinMemoryCleaner.exe" /Reset
   ```

   > **💡 TIP:** Drag and drop `WinMemoryCleaner.exe` into the Command Prompt — it inserts the full path. If the path contains spaces, enclose it in double quotes before adding ` /Reset`.

4. Press **Enter**.
5. The app will reset and close. Open it normally to use it again.

### 📋 View Application Logs

All optimization activities and essential operations are logged to the Windows Event Viewer for a transparent audit trail.

**Steps to view logs:**
1. Press **Win + R**, type **eventvwr**, and press Enter
2. Navigate to `Windows Logs > Application`
3. Look for events with the source name **Windows Memory Cleaner**

[![](./docs/assets/images/windows-event-log.png)](#-logs)

## 🛠️ Complementary Tools

While **Windows Memory Cleaner** excels at efficiently managing and freeing up memory, the following tools can provide even deeper insights into your system's memory usage and help with advanced troubleshooting:

### 🔧 [Process Explorer](https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer)

An advanced task manager from Microsoft Sysinternals that goes beyond what's available in Windows Task Manager. It provides comprehensive information about processes, including their memory usage, handles, and DLLs. Useful for identifying specific applications or services that might be consuming excessive memory.

### 🔧 [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap)

A powerful physical memory usage analysis utility from Microsoft Sysinternals. RAMMap provides detailed, real-time information about how Windows is allocating physical memory, including how much is in use by processes, drivers, the kernel, and various cached data. **It also contains some cleaning features that address certain memory areas similar to Windows Memory Cleaner.** It's an excellent tool to use alongside WinMemoryCleaner to understand precisely *where* memory is being used before and after an optimization.

## ❓ Frequently Asked Questions (FAQ)

### 💭 Is this app still useful on modern PCs, and how is it not 'snake oil'?

This is an excellent question that gets to the core of this project's philosophy.

**The short answer:** Yes, it's still useful for a massive number of users, and it's the opposite of snake oil because it's built on transparency and verifiable proof.

**The longer answer:** We've all been burnt. The PC utility market has a dark history of deceptive "boosters" that preyed on users' frustrations. They were the T-Virus of the software world, promising performance but often making things worse with shady, undocumented tricks. That history creates a lot of justified prejudice.

WinMemoryCleaner is the **antivirus serum**. It's not a magical cure-all, but a targeted, clean, and honest tool. Here's why it's different and still relevant:

* **It's for the Majority, Not Just the Elite:** While a brand-new PC with 64GB of RAM might not see a dramatic difference, that's not the reality for the average user. To understand what typical PCs look like, we can look at the Steam Hardware & Software Survey, which collects data from millions of gamers worldwide. According to this survey, the most common amount of RAM is just 16GB. This tool is built for that majority: the student with an 8GB laptop, the gamer on a budget with 16GB, and the developer running multiple virtual machines who needs to reclaim every last megabyte to keep their system running smoothly.
* **It Gives You Control:** Modern Windows is good at memory management, but it's automated. It doesn't know you're about to launch a massive game and need all available RAM *right now*. This tool lets you make that decision yourself by clearing cached memory (the Standby List) on demand.
* **It's Verifiable:** As shown in the "Proof of Concept" section, you don't have to trust us blindly. You can use Windows' own tools to see the app working in real-time. We're not hiding anything.
* **It's Built on Honesty:** The code is open-source, and every function it performs is a documented, native Windows API call. We are simply providing a safe and easy-to-use interface for powerful system maintenance tasks that already exist.

This project exists to serve the users who were left behind by the march of technology, and to restore faith that a utility can be both effective and honest.

### 💭 What are the project requirements?

- Logging to Windows Event Viewer
- Minimalistic user interface using Windows Presentation Foundation (WPF) and single-page application (SPA) architecture
- Model-View-ViewModel (MVVM) design pattern
- No third-party dependencies
- Portable (Single executable file)
- Right-to-left language support and bidirectional text
- Use of S.O.L.I.D. principles in object-oriented programming (limited due to the legacy .NET 4.0 framework)
- Use of Windows API methods for memory management
- Windows retro-compatibility (Windows XP, Server 2003, and later)

### 💭 Where does the app save the settings?

Upstream saves them in the Windows registry path `Computer\HKEY_LOCAL_MACHINE\Software\WinMemoryCleaner`. This community build uses its own independent path `Computer\HKEY_LOCAL_MACHINE\Software\WinMemoryCleaner.Community` and never reads, migrates, or overwrites the upstream settings.

### 💭 Why has the app been flagged as Malware/Virus and blocked by Windows Defender, SmartScreen, or Antivirus?

One of the reasons for this **false alarm** is that the application adds entries to the registry and creates a scheduled task to run at startup. Windows doesn't suggest letting applications with administrator privileges run at startup. I understand that, but this is the required method for this functionality. I apologize, but the application cannot perform a deep memory clean without administrator privileges.

That's a common issue that persists with every new app version. I constantly submit the executable to Microsoft. Usually, it takes up to 72 hours for Microsoft to remove the detection.
It helps if more users [submit the app for malware analysis](https://www.microsoft.com/en-us/wdsi/filesubmission)

Meanwhile, as a workaround, you can [add an exclusion to Windows Security](https://support.microsoft.com/en-us/windows/add-an-exclusion-to-windows-security-811816c0-4dfd-af4a-47e4-c301afe13b26)

## 🌐 Translation

If you're a native speaker of a language other than English, you can contribute by translating the [English.json](/src/Resources/Localization/English.json) file.

**Please note:** Translated texts should be provided in **lowercase**. The application will automatically handle capitalization as needed for the user interface.

### 🔬 How to Test Your Translation

You can test any translation by creating a file alongside the executable:

1. Visit [https://ss64.com/locale.html](https://ss64.com/locale.html) to get the **locale description** for your language.
2. Save your translation as **{locale-description}.json** using **UTF-8** character encoding.
3. Launch the application. If successful, the new language and your changes will be visible.
4. Once tested, please either submit a pull request or submit the file via the **[Translation Request](https://github.com/IgorMundstein/WinMemoryCleaner/issues/new?template=translation_request.yml)** issue template.

### 👨‍💻 For Developers

If you are a software developer, you can integrate the new file directly into the project:

1. Add the new file to the `Resources\Localization` folder.
2. Change the file's **Build Action** property to `Embedded Resource`.
3. Rebuild and run the `WinMemoryCleaner` project.

When new versions require translation updates, we may use AI tools to provide a baseline. We always value and encourage contributions from native speakers to refine and perfect these translations.

| Language | Contributor(s) | Language | Contributor(s) |
|:---|:---|:---|:---|
| 🇦🇱&nbsp;Albanian | [Omer Rustemi](https://github.com/omerrustemicode) | 🇯🇵&nbsp;Japanese | [dai](https://github.com/dai) |
| 🇸🇦&nbsp;Arabic | [Abderraouf FELLAHI](https://github.com/flh-raouf), [Abdulmajeed Al-Rajhi](https://github.com/Abdulmajeed-Alrajhi) | 🇰🇷&nbsp;Korean | [VenusGirl](https://github.com/VenusGirl) |
| 🇧🇬&nbsp;Bulgarian | [Konstantin](https://github.com/constantinejc) | 🇲🇰&nbsp;Macedonian | [Dimitrij Gjorgji](https://github.com/Cathadox) |
| 🇨🇳&nbsp;Chinese&nbsp;(Simplified) | [KaiHuaDou](https://github.com/KaiHuaDou), [Kun Zhao](https://github.com/kzhdev), [Rayden](https://github.com/raydenake22) | 🇳🇴&nbsp;Norwegian | [Dan](https://github.com/danorse) |
| 🇨🇳&nbsp;Chinese&nbsp;(Traditional) | [Rayden](https://github.com/raydenake22), [rtyrtyrtyqw](https://github.com/rtyrtyrtyqw) | 🇮🇷&nbsp;Persian | [Kavian](https://github.com/KavianK) |
| 🇳🇱&nbsp;Dutch | [Jesse](https://github.com/dragonhuntermc), [hax4dazy](https://github.com/hax4dazy) | 🇵🇱&nbsp;Polish | [Patryk](https://github.com/Fresta56) |
| 🇫🇷&nbsp;French | [William VINCENT](https://github.com/wixaw) | 🇵🇹&nbsp;Portuguese&nbsp;(Portugal) | AI |
| 🇩🇪&nbsp;German | [Calvin](https://github.com/Slluxx), [Niklas Englmeier](https://github.com/iamniklas), [Steve](https://github.com/uDEV2019) | 🇷🇺&nbsp;Russian | [Ruslan](https://github.com/ruslooob) |
| 🇬🇷&nbsp;Greek | [Theodoros Katsageorgis](https://github.com/tkatsageorgis) | 🇷🇸&nbsp;Serbian (Cyrillic & Latin) | [Dragoš Milošević](https://github.com/DragorMilos), [Radoš Milićev](https://github.com/rammba) |
| 🇮🇱&nbsp;Hebrew | [Eliezer Bloy](https://github.com/eliezerbloy) | 🇸🇮&nbsp;Slovenian | [Jadran Rudec](https://github.com/JadranR) |
| 🇭🇺&nbsp;Hungarian | [gycsisz](https://github.com/gycsisz) | 🇪🇸&nbsp;Spanish | [Ajneb Al Revés](https://github.com/AjnebAlReves), [Fran](https://github.com/FrannDzs) |
| 🇮🇩&nbsp;Indonesian | [Mochammad Misbahus Surur](https://github.com/Eskeyz), [Minids](https://github.com/tdnphantom) | 🇹🇭&nbsp;Thai | [nongice](https://github.com/21icepril) |
| 🇮🇪&nbsp;Irish | [Happygolucky254](https://github.com/Happygolucky254) | 🇹🇷&nbsp;Turkish | [Rıza Emet](https://github.com/rizaemet), [Viollje](https://github.com/Viollje) |
| 🇮🇹&nbsp;Italian | [Michele](https://github.com/wintrymichi) | 🇺🇦&nbsp;Ukrainian | [Riebi](https://github.com/RieBi), [Oleksandr](https://github.com/Mariachi1231) |

## ❤️ Contribute to the Project
> **Community note**: the links below belong to the **original author, Igor Mundstein**. This community continuation adds no donation channel of its own; the maintainers receive nothing from them.

In the past, I faced challenges with the proper hardware and software needed to fully enjoy technology and gaming. It was a constant battle to squeeze every last drop of performance out of a limited machine.

Although I may not rely on this tool as much as I once did, I continue to maintain it in my free time for a simple reason: to assist others who still encounter similar challenges. This project is a contribution to the field of technology and a tribute to the wonderful open-source community that supports us all.

If you find this app helpful, please consider tipping. Your contribution helps keep the project alive, optimized, and free for everyone.

| 💰 Cash | 🪙 Crypto |
| :--- | :--- |
| [Sponsor on GitHub](https://github.com/sponsors/IgorMundstein) | [Bitcoin (BTC)](https://www.blockchain.com/btc/address/bc1qu884q5r2uqugvdhyk8l6waakumeve7jykqp7ap) |
| [Buy me a coffee on Ko-fi](https://ko-fi.com/igormundstein) | [Ethereum (ETH)](https://www.blockchain.com/explorer/addresses/eth/0xb71A94733B0578D155D9A765E0d2C4dA0f44156d) |
