# WinMemoryCleaner Community

[简体中文](#zh-cn) | [English](#english)

<a id="zh-cn"></a>

## 简体中文

Windows 内存清理、资源监控与进程占用查看工具，便携运行，需要管理员权限。

> **非官方社区续作**：基于 [Igor Mundstein 的 Windows Memory Cleaner](https://github.com/IgorMundstein/WinMemoryCleaner) 继续维护，与原作者没有隶属关系，也不代表官方接任或获得其背书。原作及贡献者署名、GPL-3.0 许可证和历史记录保留。社区构建名为 `WinMemoryCleaner.Community.exe`，目前**未签名**，不是上游签名版本。

### 下载与校验

- [社区版正式下载（Releases）](https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest)
- [社区仓库](https://github.com/shabhui/WinMemoryCleaner-Community) · [问题反馈](https://github.com/shabhui/WinMemoryCleaner-Community/issues)
- 如需原作者的版本，请使用[上游 Releases](https://github.com/IgorMundstein/WinMemoryCleaner/releases/latest)。上游包管理器条目不等同于社区版。

从**同一个 Release** 下载 `WinMemoryCleaner.Community.exe` 和 `WinMemoryCleaner.Community.exe.sha256`，在下载目录运行 PowerShell：

```powershell
$expected = [regex]::Match((Get-Content .\WinMemoryCleaner.Community.exe.sha256 -Raw), '(?i)\b[0-9a-f]{64}\b').Value
$actual = (Get-FileHash .\WinMemoryCleaner.Community.exe -Algorithm SHA256).Hash
if (!$expected -or $actual -ine $expected) { throw 'SHA-256 校验失败，请勿运行' }
'校验一致'
```

SHA-256 可检查下载完整性，**不是数字签名，也不能抵御发布账号与校验文件同时被篡改**。遇到安全软件告警时先核对来源、哈希和发布记录，不建议关闭防护或添加全局排除。替换旧版前请从托盘菜单自行退出，并保留可回退的旧文件；程序为单实例，重复启动不代表已切换到新版。

自动更新默认关闭。启用后从本仓库的 `releases/latest` 通道读取版本公告、EXE 和 SHA-256；校验失败不会替换正在使用的程序。驱动安装是另一项需要单独确认的操作。

### 可以做什么

| 入口 | 用途 |
|---|---|
| **概览** | 查看物理内存；CPU/GPU 使用率与温度并排，长说明收进默认折叠的“详情”；可选游戏 FPS，保留原有内存清理 |
| **进程** | 按名称或 PID 搜索、排序，同名进程保留独立行；可显式选为本次运行的 FPS 目标 |
| **设置** | 调整 1 / 2 / 5 秒刷新（默认 2 秒）、悬浮显示、温度与 FPS 目标模式，以及原有清理、托盘、快捷键、主题和语言设置 |
| **托盘** | 查看内存占用、重新打开窗口、清理或退出 |
| **悬浮监控** | 默认关闭；可横向/竖向、拖动定位、保存位置，默认鼠标穿透且不抢焦点；“仅显示监控条”会隐藏主窗口，托盘仍可打开设置 |

**操作风险：** Windows 会自行管理缓存，释放更多内存不等于性能一定提高。工作集回收可能导致重新读盘或短暂卡顿；进程排除仅影响逐进程工作集回收，不影响其他全局清理。结束进程会丢失未保存数据，必须先确认。程序会校验所选进程身份并拒绝自身、关键及受保护进程，但这些防护不能消除对普通应用误操作的风险。

### 采样与温度的边界

- 使用率来自 Windows 采样接口；进程 CPU 按全机归一化，GPU 使用率取物理显卡忙引擎。首帧、权限不足、进程退出或驱动不支持时可能没有读数，不能直接与其他工具的不同统计口径比较。
- 没有可信温度时显示“不可用”或 `—`，不是 0°C；不把 ACPI 区域、热点或显存温度冒充 CPU/GPU 核心温度。
- 温度按界面需求采样：可见概览/进程页会请求温度；仅悬浮显示时由温度显示项决定。关闭悬浮并隐藏/最小化主窗口后，停止无消费者的 CPU/GPU、温度与进程详情采样，保留托盘及自动清理所需的低频内存检查。
- 优先复用已有 ASUS CPU / NVIDIA NVAPI 来源和已运行的默认名 LHM/OHM WMI 提供者；缺少指标时才使用内嵌 LibreHardwareMonitor **0.9.6** 独立后端。轻量来源覆盖全部温度时无需保持后端运行。
- 独立后端按需校验并释放至 `%LOCALAPPDATA%\WinMemoryCleaner.Community\Sensors`，最多一个自有后端进程；会增加磁盘缓存和进程内存开销，不需要在 EXE 旁手工放 DLL。
- 不保证所有 CPU/GPU、虚拟机、驱动或安全策略均能提供温度；悬浮层也不保证覆盖独占全屏、受保护桌面或反作弊环境。没有游戏注入或绕过保护功能。

#### 可选游戏 FPS：开关与目标

- **默认关闭。** 在概览勾选 `FPS`，或在设置的显示项中开启“游戏 FPS（提交）”。同一个显示项控制概览和监控条；监控条本身仍独立开关，不会因开启 FPS 自动弹出。
- **自动跟随前台**：只查询当前前台窗口所属进程，排除本工具，不为发现前台枚举全进程。也可选择**手动选择进程**：在进程页选中游戏并点“选为 FPS 目标”。只高亮行不会切目标；选择目标也不会自动开启 FPS。
- 手动目标按 PID 和创建时刻校验，只在本次运行中保留。游戏退出、PID 被复用或身份无法验证时停止提供读数；游戏或工具重启后需要重新选择，不跨重启保存 PID。
- 统计的是经验证格式的 **DirectX DXGI / D3D9 成功 Present 提交速率**，不是显示器 Hz、界面刷新率、上屏帧率或补帧结果。无有效事件、权限不足、接口不支持、事件丢失等均显示 `—` 和实际状态，不伪造 0/60 FPS。
- 采集使用自有、按目标进程及事件过滤的 Windows ETW 会话，不注入、不 hook 游戏、不安装组件或驱动。FPS 关闭或没有可见概览/监控条需求时不启动采集；进程页、设置页、紧凑模式及隐藏/最小化主窗口在没有 FPS 监控条需求时暂停，切目标立即作废旧读数。
- **验证边界**：当前新增 FPS 的原生 ETW 边界与真实游戏尚未实测；不保证 OpenGL/Vulkan、独占全屏或反作弊应用。确定性测试不能替代游戏兼容性、开销及准确性验收，原发布性能门禁仍未放行。

#### 可选温度驱动：先确认，再准备

日常启动、监控、构建、测试及程序更新**不会自动下载安装器或安装驱动**。显式开启 CPU 温度或包含 CPU 温度的悬浮显示时，会异步检查状态；只有确认驱动确实缺失才询问安装。权限不足、设备未就绪、身份不明等状态会分别说明，不伪装成“未安装”。

用户明确确认后，才准备固定的[官方 PawnIO **2.2.0** 安装包](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)：可从受限 HTTPS 来源获取，也可选择本地官方离线包。两种方式都必须通过固定长度、SHA-256、Windows 签名信任、签名者及路径检查；离线包无效不会偷偷改走下载，取消选择或准备不启动安装器。

已有驱动须精确匹配受支持的官方 **2.1.0 / 2.2.0** 版本与哈希，并通过签名及设备就绪检查；仅有签名或版本更高并不足够。**不会静默覆盖、降级或卸载已有共享驱动**，不静默安装、不自动重启、不关闭 Secure Boot/HVCI 等系统保护。自有采集代码不控制风扇、功耗、电压或超频；上游测量接口可能使用具备读写权限的句柄/IOCTL，不应理解为所有底层访问都只有只读权限。

### 运行与构建

- **主程序**：WPF、.NET Framework **4.0**、C# **4**；新增监控指标的可用性取决于 Windows 和驱动，并非所有旧系统均具备全部指标。
- **可选独立温度后端**：Windows **10/11 x64**、.NET Framework **4.7.2 或更新的 4.x**。缺少运行时或架构支持时如实降级，不自动安装 .NET。
- 社区设置独立保存在 `HKLM\SOFTWARE\WinMemoryCleaner.Community`，不迁移或覆盖上游设置。

在具备 Visual Studio / Build Tools MSBuild 与 NuGet 的开发环境中，从仓库根目录执行；以下 `$msbuild` 应来自 Visual Studio 开发者 PowerShell，而不是系统内置 Framework MSBuild：

```powershell
nuget restore .\src\packages.config -PackagesDirectory .\src\packages -NonInteractive
$msbuild = (Get-Command msbuild).Source
.\build-community.ps1 -Tests -MSBuildPath $msbuild
# 需要生成本地候选包时：
.\build-community.ps1 -Tests -Package -MSBuildPath $msbuild
```

[`build-community.ps1`](build-community.ps1) 会恢复锁定的传感器输入、构建同源后端与主程序、验证实际内嵌载荷，并在 `-Tests` 下运行两套安全测试及对应源码检查。`-Package` 要求 Visual Studio MSBuild 和 `-Tests`；保持 C# 4、完整分析器与警告即错误。默认系统 Framework 构建仅用于开发，后端仍需用 `-SensorCompilerPath` 指向可用的 Roslyn 编译器，不能冒充正式构建。

**正式产物只从 [Community release build](.github/workflows/community-release.yml) 发布**；[Community CI](.github/workflows/community-ci.yml) 用于不发布的构建与安全测试。输出分别位于 `build/test-results`、`build/sensors` 和 `dist`。本地候选、历史报告或文档整理不代表当前候选已通过 CI、完整 GUI 或性能验收；以对应提交的实际运行结果为准。

### 文档、反馈与许可

下列相对链接用于源码仓库。便携 ZIP 把指南平铺为 `monitoring-guide.zh-CN.md`，传感器说明为 `SENSOR-THIRD-PARTY-NOTICES.md` / `SENSOR-SOURCES.md`；源码和其他历史报告请在社区仓库查看。

- [监控与进程管理指南](docs/monitoring-guide.zh-CN.md)
- [Community.5 发布验证记录与限制](docs/release-community-5-validation.zh-CN.md)
- 候选历史记录：[p3 驱动提示](docs/cpu-driver-prompt-report.zh-CN.md)、[p2 验收（含未通过项）](docs/acceptance-p2-01-report.zh-CN.md)、[体积优化](docs/size-optimization-report.zh-CN.md)、[刷新优化](docs/refresh-optimization-report.zh-CN.md)。报告中的 `build/...` 仅是本地证据位置，不是公开下载或已发布资产。
- 提交问题前请删除用户名、计算机名、绝对路径、进程身份、个人配置及截图/日志中的私人内容；不要直接上传完整本地证据目录。
- 原作 © **Igor Mundstein** 及贡献者；社区修改由 **shabhui 和社区贡献者**维护，依 [GPL-3.0](LICENSE) 分发。完整署名及翻译贡献者见 [CREDITS.md](CREDITS.md)，上游来源见 [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md)。
- 温度依赖的[第三方许可与来源](sensors/THIRD-PARTY-NOTICES.md)、[对应源码说明](sensors/SOURCE-README.md)单独保留；对应源码包不等于可逐字节重建官方签名安装器。外部 2.2.0 驱动源码链接不应误认为已包含于保留的 2.1.0 源码档案。
- 如希望支持原作者，可访问 [Igor Mundstein 的 GitHub Sponsors](https://github.com/sponsors/IgorMundstein) 或 [Ko-fi](https://ko-fi.com/igormundstein)；这些不是社区维护者的收款渠道。

---

<a id="english"></a>

## English

A portable Windows memory-cleaning, resource-monitoring and per-process usage tool. Administrator privileges are required.

> **Unofficial community continuation:** maintained from [Igor Mundstein's Windows Memory Cleaner](https://github.com/IgorMundstein/WinMemoryCleaner), with no affiliation, official succession or endorsement implied. Original and contributor credits, the GPL-3.0 license and project history are preserved. The community executable is named `WinMemoryCleaner.Community.exe`; it is currently **unsigned**, not an upstream-signed build.

### Download and verify

- [Community releases](https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest)
- [Community repository](https://github.com/shabhui/WinMemoryCleaner-Community) · [Report an issue](https://github.com/shabhui/WinMemoryCleaner-Community/issues)
- For the original author's version, use the [upstream releases](https://github.com/IgorMundstein/WinMemoryCleaner/releases/latest). Upstream package-manager entries do not identify the community build.

Download `WinMemoryCleaner.Community.exe` and `WinMemoryCleaner.Community.exe.sha256` from **the same release**, then run PowerShell in the download directory:

```powershell
$expected = [regex]::Match((Get-Content .\WinMemoryCleaner.Community.exe.sha256 -Raw), '(?i)\b[0-9a-f]{64}\b').Value
$actual = (Get-FileHash .\WinMemoryCleaner.Community.exe -Algorithm SHA256).Hash
if (!$expected -or $actual -ine $expected) { throw 'SHA-256 mismatch. Do not run this file.' }
'Checksum matches'
```

SHA-256 checks download integrity; it is **not a digital signature and cannot protect against the release account and checksum file both being compromised**. If security software reports a warning, check the source, hash and release history first. Disabling protection or adding broad exclusions is not recommended. Before replacing an older version, exit it from its tray menu and keep a rollback copy. The application is single-instance: launching it again does not mean the running executable has been replaced.

Automatic updates are off by default. When enabled, they use this repository's `releases/latest` channel for version information, the EXE and its SHA-256 checksum. A failed checksum prevents replacement. Driver installation is a separate operation requiring its own confirmation.

### Features

| Entry point | Purpose |
|---|---|
| **Overview** | Physical memory; CPU/GPU usage alongside temperatures, with long explanations in collapsed Details; optional game FPS and the existing memory-cleaning operations |
| **Processes** | Search and sort by name/PID or usage, keep same-name processes separate, and explicitly select an FPS target for this run |
| **Settings** | Select a 1 / 2 / 5-second refresh interval (2 seconds by default), overlay, temperature and FPS target modes, plus existing cleaning, tray, hotkey, theme and language settings |
| **Tray** | View memory usage, reopen the window, clean memory or exit |
| **Monitoring overlay** | Off by default; horizontal or vertical, draggable with saved positioning, click-through and non-focus-stealing by default; monitor-only mode hides the main window while settings remain accessible from the tray |

**Operational risks:** Windows manages its own caches; freeing more memory does not necessarily improve performance. Trimming working sets may cause disk reads or brief stalls. Process exclusions apply only to per-process working-set trimming, not other system-wide cleaning operations. Ending a process can lose unsaved work and requires confirmation. The application verifies the selected process identity and rejects itself, critical and protected processes, but these safeguards cannot eliminate the risk of acting on the wrong ordinary application.

### Sampling and temperature limitations

- Usage readings come from Windows sampling interfaces. Process CPU is normalized across the whole machine; GPU usage uses the busiest engine of a physical GPU. Initial samples, insufficient permissions, exited processes or unsupported drivers may produce no reading. Different tools may use different accounting methods.
- An unavailable or untrusted temperature is shown as unavailable or `—`, not 0°C. ACPI zones, hotspots and VRAM temperatures are not presented as CPU/GPU core temperatures.
- Temperature sampling follows UI demand: visible overview/process pages request temperatures, while overlay-only demand depends on enabled temperature items. With the overlay off and the main window hidden or minimized, CPU/GPU, temperature and process-detail sampling without consumers stops; low-frequency memory checks needed by the tray and automatic cleaning remain active.
- Existing ASUS CPU / NVIDIA NVAPI sources and already-running LHM/OHM WMI providers under their default process names are reused first. The embedded LibreHardwareMonitor **0.9.6** backend is used for missing metrics. If lightweight sources cover all requested temperatures, the backend need not remain running.
- The independent backend is verified and extracted on demand to `%LOCALAPPDATA%\WinMemoryCleaner.Community\Sensors`, with at most one owned backend process. It adds disk-cache and process-memory overhead; no manual placement of DLLs beside the EXE is needed.
- Temperature support is not guaranteed for every CPU/GPU, VM, driver or security policy. The overlay is not guaranteed over exclusive fullscreen applications, protected desktops or anti-cheat environments. There is no game injection or protection-bypass feature.

#### Optional game FPS: display and target selection

- **Off by default.** Enable `FPS` on Overview or “Game FPS (presents)” in Settings. One display item controls Overview and the monitoring overlay; the overlay keeps its separate on/off switch and is not opened automatically by enabling FPS.
- **Follow foreground** queries only the process owning the current foreground window, excludes this tool, and does not enumerate all processes to find it. Alternatively use **Selected process**: highlight the game on Processes and click “Use as FPS target”. Highlighting alone does not retarget capture; choosing a target does not enable FPS.
- Manual targets are verified by PID and creation time and retained only for the current run. Exited processes, reused PIDs and unverifiable identities cannot provide a reading. Select again after restarting the game or tool; manual PIDs are not persisted across restarts.
- The metric is the **successful DirectX DXGI / D3D9 Present submission rate** for verified event formats, not display Hz, UI refresh rate, displayed FPS or generated frames. No valid events, insufficient permissions, unsupported APIs or event loss produce `—` and the actual state, never invented 0/60 FPS.
- Collection uses an owned Windows ETW session filtered by target process and event, without game injection/hooks, component installation or new drivers. With FPS off or no visible Overview/overlay demand, capture does not start. Processes, Settings, compact mode and a hidden/minimized main window pause FPS when no FPS overlay needs it; changing targets invalidates the old reading immediately.
- **Verification boundary:** native ETW boundaries and real games have not yet been exercised for this new feature. OpenGL/Vulkan, exclusive fullscreen and anti-cheat applications are not guaranteed. Deterministic tests do not establish game compatibility, overhead or accuracy; the existing release performance gates remain unpassed.

#### Optional temperature driver: confirm before preparation

Routine startup, monitoring, builds, tests and application updates **do not automatically download an installer or install a driver**. Explicitly enabling CPU temperature, or an overlay that includes it, checks the state asynchronously. An installation prompt is shown only when the driver is confirmed missing. Access denied, device-not-ready and unknown-identity states are reported separately rather than treated as missing.

Only after explicit confirmation does the application prepare the pinned [official PawnIO **2.2.0** installer](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0). It can download from restricted HTTPS sources or use an official offline package selected by the user. Both paths require the pinned length and SHA-256, Windows signature trust, signer and path checks. An invalid offline package does not silently fall back to a download; canceling selection or preparation does not launch the installer.

An existing driver must exactly match a supported official **2.1.0 / 2.2.0** version and hash and pass signature and device-readiness checks. A signature alone, or a newer version number, is insufficient. Existing shared drivers are **never silently overwritten, downgraded or uninstalled**. There is no silent installation, automatic restart or disabling of Secure Boot/HVCI or other system protections. The application's collection code does not control fans, power, voltage or overclocking. Upstream measurement interfaces may use handles/IOCTLs with read/write permissions; this is not a claim that every low-level access is read-only.

### Requirements and building

- **Main application:** WPF, .NET Framework **4.0**, C# **4**. Availability of added monitoring metrics depends on Windows and its drivers; not all older systems provide every metric.
- **Optional independent temperature backend:** Windows **10/11 x64**, .NET Framework **4.7.2 or a newer 4.x runtime**. Missing runtime or architecture support is reported with graceful degradation; .NET is not installed automatically.
- Community settings use the separate `HKLM\SOFTWARE\WinMemoryCleaner.Community` key. Upstream settings are not migrated or overwritten.

From the repository root, use a development environment with Visual Studio / Build Tools MSBuild and NuGet. The `$msbuild` below must come from a Visual Studio Developer PowerShell, not the in-box Framework MSBuild:

```powershell
nuget restore .\src\packages.config -PackagesDirectory .\src\packages -NonInteractive
$msbuild = (Get-Command msbuild).Source
.\build-community.ps1 -Tests -MSBuildPath $msbuild
# To produce a local candidate package:
.\build-community.ps1 -Tests -Package -MSBuildPath $msbuild
```

[`build-community.ps1`](build-community.ps1) restores pinned sensor inputs, builds the backend and main application from the same source state, and verifies the actual embedded payload. With `-Tests`, it runs both safety suites and corresponding-source checks. `-Package` requires Visual Studio MSBuild and `-Tests`, preserving C# 4, the complete analyzer set and warnings as errors. The default in-box Framework build is for development only; its backend still needs `-SensorCompilerPath` pointing to a usable Roslyn compiler. It is not a release build.

**Distributable release binaries come only from [Community release build](.github/workflows/community-release.yml).** [Community CI](.github/workflows/community-ci.yml) performs non-publishing builds and safety tests. Outputs use `build/test-results`, `build/sensors` and `dist`. A local candidate, historical report or documentation update does not establish that the current candidate has passed CI, full GUI checks or performance acceptance; consult the results for the matching commit.

### Documentation, feedback and licensing

The relative links below target the source repository. The portable ZIP places the guide at `monitoring-guide.zh-CN.md` and sensor notices at `SENSOR-THIRD-PARTY-NOTICES.md` / `SENSOR-SOURCES.md`. Source files and other historical reports are available in the community repository. The detailed guides and reports linked below are in Chinese.

- [Monitoring and process-management guide](docs/monitoring-guide.zh-CN.md)
- [Community.5 release verification and limitations](docs/release-community-5-validation.zh-CN.md) (Chinese)
- Candidate history: [p3 driver prompts](docs/cpu-driver-prompt-report.zh-CN.md), [p2 acceptance, including failures](docs/acceptance-p2-01-report.zh-CN.md), [size optimization](docs/size-optimization-report.zh-CN.md), and [refresh optimization](docs/refresh-optimization-report.zh-CN.md). References to `build/...` in reports identify local evidence locations, not public downloads or published assets.
- Before submitting an issue, remove usernames, computer names, absolute paths, process identities, personal configuration and private content from screenshots/logs. Do not upload complete local evidence directories.
- Original work © **Igor Mundstein** and contributors. Community changes are maintained by **shabhui and community contributors**, distributed under [GPL-3.0](LICENSE). Full attribution and translation credits are in [CREDITS.md](CREDITS.md); upstream provenance is in [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md).
- Sensor [third-party notices and provenance](sensors/THIRD-PARTY-NOTICES.md) and [corresponding-source notes](sensors/SOURCE-README.md) are retained separately. The corresponding-source bundle is not a claim of byte-for-byte reproducibility of the official signed installer. External links to 2.2.0 driver sources do not mean those sources are included in the retained 2.1.0 source archives.
- To support the original author, visit [Igor Mundstein's GitHub Sponsors](https://github.com/sponsors/IgorMundstein) or [Ko-fi](https://ko-fi.com/igormundstein). These are not payment channels for the community maintainer.

[返回中文 / Back to Chinese](#zh-cn)
