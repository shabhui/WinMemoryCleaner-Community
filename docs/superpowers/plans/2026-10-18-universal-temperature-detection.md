# Universal Temperature Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留清理器兼容性及紧凑监控条的前提下，自动跨品牌探测真实 CPU/GPU 温度并诚实降级。

**Architecture:** 主 EXE 保持 net40，内嵌独立 x64/net472 采集进程与固定 LibreHardwareMonitorLib 0.9.6 依赖。两个项目链接同一份 C#4 选择规则与有界 JSON Lines 协议；温度协调器后台管理可信载荷、专属 Job、缓存新鲜度和原生/WMI 备用来源。系统驱动安装只由明确确认的 UI 操作启动。

**Tech Stack:** WPF/net40/C#4；独立 x64/net472/C#4；LibreHardwareMonitorLib 0.9.6；NUnit 2.6.4；GZipStream；Windows Job / WinVerifyTrust；Visual Studio MSBuild/Roslyn canonical CI。

**Spec:** `docs/superpowers/specs/2026-10-18-universal-temperature-detection-design.md`

## Global Constraints

- 主程序保持 WPF、.NET Framework 4.0、C# 4、`AllRules.ruleset` 和 warnings-as-errors；自有后端 x64/net472/C#4 使用同一分析器规则。
- 固定 LHM 0.9.6、提交 `3d331e3370efb858411f19511373eff65a218701`；固定 NuGet 内容哈希，保留许可/源码信息。
- 不结束 PID 23072、不覆盖 `D:\下载\WinMemoryCleaner.Community.exe`、不写真实清理器 HKLM 设置/原开机任务。
- 本次连续实施获用户授权；实际安装 PawnIO 没有单独授权，开发和 CI 均不得执行安装器。
- 只启 CPU/GPU；CPU 仅在官方签名 PawnIO 状态可靠验证后启用；配置默认值适配器不读取/保存控制设置，自有代码不调用控制接口。
- 温度有限且 1–150°C；CPU 按规格六级选择，GPU 仅核心温度；不得冒充热点、显存、ACPI 区域或到阈值距离。
- JSON Lines v1：UTF-8、帧≤64 KiB、深度≤16、代表设备≤64、名字≤256字符、标识≤512字符、stderr只保留最近16 KiB。
- 刷新1/2/5秒，后台首帧10秒预算，年龄上限 `max(10秒,3×刷新间隔)`，UTC超前>5秒拒绝，故障退避30秒，后台停止最多2秒。
- 一个主实例最多一个后端；专属 kill-on-close Job；需求关闭/暂停/Dispose退出，不阻塞 UI，不按进程名杀进程。
- 单 EXE 更新通道不变；下一版 AssemblyVersion>3.0.8.3；预定 `3.0.8.4-community.4`，不覆盖已发布 tag/资产。
- 无授权或硬件不足的实测必须单列，模拟、原生备用与通用实机证据不能混用。

## Review Focus

- 校验之后缓存文件/目录被替换：重解析路径拒绝，执行依赖持有只共享读取句柄；Task 2 验证替换失败和损坏重建。
- 双 GPU 中一块仅提供热点/显存：不能压过另一块真实核心；Task 1 用混合设备夹具固定行为。
- stdout 不换行/异常时间/重复帧：内存有界且旧值不能续命；Task 1/3 验证长度拒绝、新鲜度及故障退出。
- 安装器退出0但驱动未就绪或已有新版：不能声称可用/降级安装；Task 2 用安装替身验证。
- 温度开关及退出与后端准备同时发生：撤销会话即失效，临迟启动也要回收；Task 3/4 验证并发取消、无孤儿及隐藏需求。

---

## File Structure / Ownership

- `shared/Sensors/SensorReading.cs`：候选/代表读数与测量类别，内部 DTO。
- `shared/Sensors/TemperatureSelection.cs`：精确名称、优先级、范围和稳定来源选择。
- `shared/Sensors/SensorProtocol.cs`：只解析基本 JSON 字典的显式协议编码/解码，不反序列化类型。
- `shared/Sensors/BoundedLineReader.cs`：换行前UTF-8字节上限、深度限制辅助。
- `shared/Sensors/SensorFreshness.cs`：序号、UTC与单调接收时间纯逻辑。
- `shared/Sensors/SensorDriverTrust.cs`：官方签名/驱动设备只读就绪验证，两个项目链接。
- `sensors/WinMemoryCleaner.Sensors.csproj`、`Properties/AssemblyInfo.cs`、`App.config`：隔离后端与固定框架/分析器。
- `sensors/DefaultSensorSettings.cs`、`LhmTemperatureSource.cs`：默认配置和只启CPU/GPU的库适配器。
- `sensors/Program.cs`：父身份、配置/stop、后台输入和有界采样协议宿主。
- `sensors/Tests/*.cs`、`WinMemoryCleaner.Sensors.Tests.csproj`：适配器/宿主安全测试，不装驱动。
- `sensors/dependencies.lock.json`、`THIRD-PARTY-NOTICES.md`：包内容哈希、运行时文件及许可/来源。
- `src/Service/Monitoring/Sensors/SensorPayload.cs`：清单验证、原子缓存、防替换租约。
- `src/Service/Monitoring/Sensors/SensorBackendController.cs`：后台启动/Job/IPC/健康缓存/停止。
- `src/Service/Monitoring/Sensors/SensorDriverInstaller.cs`：独立的显式授权安装状态机/安装服务。
- `src/Service/Monitoring/Sensors/WmiTemperatureSource.cs`：LHM/OHM独立存在门控及共享选择规则。
- `src/Service/Monitoring/TemperatureSampler.cs`：通用优先、ASUS/NVAPI/WMI独立降级。
- `src/Interfaces/ITemperatureConfiguration.cs`：可选内部配置 seam，`IResourceMonitor` 签名不变。
- `src/Service/Monitoring/ResourceMonitor.cs`、`src/Model/Monitoring/ResourceSnapshot.cs`、`src/ViewModel/MonitoringViewModel.cs`：刷新/状态/后台生命周期接线。
- `src/Core/Monitoring/MonitorText.cs`、`src/View/Window/MainWindow.xaml{,.cs}`：既有设置区域一行状态和明确确认入口，中英切换。
- `src/Test/UniversalTemperatureTests.cs`、`SensorProtocolTests.cs`、`SensorPayloadTests.cs`、`SensorBackendTests.cs`、`SensorInstallationTests.cs`：纯逻辑、文件与受控进程测试，不扩大Desktop排除。
- `tools/restore-sensors.ps1`、`tools/build-sensors.ps1`、`tools/verify-sensor-payload.ps1`：固定输入下载/校验、后端构建、逐文件GZip清单与内嵌比对。
- `build-community.ps1`、`tools/build-monitoring.ps1`、`src/WinMemoryCleaner.csproj`、`.github/workflows/community-{ci,release}.yml`：同次构建载荷/主EXE/安全测试。
- `README.md`、`CHANGELOG.md`、`docs/monitoring-guide.zh-CN.md`、`docs/universal-temperature-report.zh-CN.md`：功能/成本/驱动限制与新证据。

## Execution commands

所有 Git 命令带 `git -c safe.directory=G:/AI项目/WinMemoryCleaner-main/WinMemoryCleaner-Community -C <repo>`。PowerShell 执行策略限制下将构建脚本读为UTF8并替换 `$PSScriptRoot` 后以ScriptBlock执行；不改变机器执行策略。

```powershell
$repo='G:\AI项目\WinMemoryCleaner-main\WinMemoryCleaner-Community'
$body=[IO.File]::ReadAllText("$repo\tools\build-monitoring.ps1",[Text.Encoding]::UTF8)
$body=$body.Replace('$PSScriptRoot',"$repo\tools")
& ([ScriptBlock]::Create($body)) -Mode Test
```

本地 in-box 构建仅作开发检查；后端可用下载到会话scratch的net472引用和Roslyn编译器检查。canonical CI 才是最终分析器/发布权威。

### Task 1: Shared selection and bounded protocol

**Files:** Create `shared/Sensors/{SensorReading,TemperatureSelection,SensorProtocol,BoundedLineReader,SensorFreshness}.cs`; create `src/Test/{UniversalTemperatureTests,SensorProtocolTests}.cs`; modify `src/WinMemoryCleaner.csproj` 链接共享源码。

**Interfaces:**
- `TemperatureSelection.Select(IEnumerable<SensorReading> candidates) : IList<SensorReading>`：每设备一个代表值，类型只认 Cpu/GpuNvidia/GpuAmd/GpuIntel。
- `TemperatureSelection.Highest(IEnumerable<SensorReading> readings, bool cpu) : SensorReading`：返回最高有效代表值或null。
- `SensorProtocol.Encode(SensorMessage message) : string`、`Decode(string line) : SensorMessage`；固定 Version=1 / PayloadVersion=`lhm-0.9.6-p1`。
- `BoundedLineReader.ReadLine() : string`：UTF8换行前超64KiB抛InvalidDataException，EOF返回null。
- `SensorFreshness.Accept(SensorMessage message, DateTime utcNow, long monotonicTicks, int refreshSeconds) : bool`；`IsFresh(...) : bool`；`Reset()`。

- [x] **Step 1: Write failing selection tests**：`PackageWinsOverCoreAndDistance`断言Package=54而非Core80/Distance99；`AmdTdieAndCombinedAreNotOffset`断言优先Tdie61、仅合并时73原值；`CoreOnlyAndCcdUseMaximum`断言Core/CCD最高；`HotspotOnlyDeviceDoesNotWin`断言GPU Core46胜过另一卡Hot Spot110；`InvalidReadingExcluded`覆盖null/0/NaN/Infinity/0.99/150.01，1及150有效；`OrdinalTieStableAndCultureIndependent`覆盖土耳其/德语、混合CPU/GPU及名称尾缀拒绝。
- [x] **Step 2: Run Test build**：预期编译失败，缺失共享类型/方法；记录红灯输出。
- [x] **Step 3: Implement shared selection**：精确全名/完整数字模式，设备内优先级再最高，最后Ordinal标识tie；返回带Measurement的副本。
- [x] **Step 4: Write protocol/freshness failing tests**：roundtrip Unicode/invariant；unknown version/session/field type/depth17/count65/64KiB+拒绝；坏读数只丢项；重复/旧序号、UTC超前5.001秒、过期10/15秒均不续命；stderr测试留给Task3。
- [x] **Step 5: Implement protocol/freshness**：DeserializeObject只处理已知基础类型，显式验证结构；读取器在累积前检查字节，禁止任意类型加载；纯逻辑时钟由参数传入。
- [x] **Step 6: Run safe tests**：既有413个及新增测试全绿，`git diff --check`；提交 `feat: add bounded temperature selection and sensor protocol`。

### Task 2: Trusted payload, dependencies and explicit driver installation

**Files:** Create `shared/Sensors/SensorDriverTrust.cs`, `src/Service/Monitoring/Sensors/{SensorPayload,SensorDriverInstaller}.cs`, `sensors/{dependencies.lock.json,THIRD-PARTY-NOTICES.md}`, `tools/restore-sensors.ps1`; create `src/Test/{SensorPayloadTests,SensorInstallationTests}.cs`。

**Interfaces:**
- `SensorDriverTrust.Inspect() : SensorDriverState`：只读查询官方驱动签名、版本、设备就绪；无法可靠确认则CpuAllowed=false。
- `SensorPayload.Prepare() : SensorPayloadLease`：租约提供固定Directory/Executable/Installer及完整性验证，Dispose释放只共享读取句柄。
- `SensorDriverInstaller.Begin(bool explicitlyConfirmed) : bool`、`IsInstalling : bool`、`Status : string`；仅授权开始，一个安装器、完成后重新Inspect而非看退出0。
- 安装测试通过注入 `Func<SensorDriverState>`、`Func<SensorPayloadLease>`、固定安装启动/等待替身，不能连接真实安装器。

- [x] **Step 1: Lock/download dependencies**：下载固定net472包图，记录每nupkg SHA512/base64、SHA256与选定runtime路径；下载固定官方安装器，实际Authenticode有效且Git blob匹配才纳入；提取许可证，不运行安装器。
- [x] **Step 2: Write failing payload tests**：缺资源/长度或SHA错误拒绝；绝对/`..`/重解析/额外文件拒绝；并发Prepare只发布完整目录；损坏可重建；租约持有时重命名/写入执行依赖失败；释放后可清理本应用缓存。
- [x] **Step 3: Implement payload**：CI JSON清单自身SHA作为版本目录；临时目录完整验证后Directory.Move；拒绝重解析链/异常目录，启动前锁定并再核验所有文件。
- [x] **Step 4: Write failing install/trust tests**：未授权/取消不启动；签名失败/设备不可用/新版已装不覆盖；重复请求仅一次；退出0但未就绪/3010需重启/失败均显示原因；安装后重新检查并通知控制器重启。
- [x] **Step 5: Implement trust/installer**：WinVerifyTrust +官方签名身份/模式验证，已有驱动只读；安装文件固定并防替换；交互启动无静默/自动重启/安全降级参数，等待后台执行。
- [x] **Step 6: Run safe tests and input hash checks**：495/495 安全测试通过，16个固定包/12个DLL恢复通过；旧EXE、PID23072、HKLM32/64、原任务和现有共享PawnIO均未改变；提交 `feat: prepare verified sensor payload and explicit driver installation`。

### Task 3: Read-only hardware backend and managed process

**Files:** Create `sensors/{WinMemoryCleaner.Sensors.csproj,App.config,Properties/AssemblyInfo.cs,DefaultSensorSettings.cs,LhmTemperatureSource.cs,Program.cs}`, backend Tests/project, `src/Service/Monitoring/Sensors/SensorBackendController.cs`, `src/Test/SensorBackendTests.cs`, `tools/build-sensors.ps1`。

**Interfaces:**
- `DefaultSensorSettings : LibreHardwareMonitor.Hardware.ISettings`：GetValue返回传入默认值，SetValue无持久化；上游若有额外方法也为no-op。
- `LhmTemperatureSource.Sample(bool cpuAllowed) : SensorMessage`：库只启CPU/GPU，双重驱动验证，CPU/GPU独立状态。
- `SensorBackendController.Configure(int seconds)`、`GetSnapshot(out MetricValue cpu, out MetricValue gpu)`、`Suspend()`、`ResetDriverState()`、`Dispose()`，方法返回不等待采集。
- 后端参数固定父PID/父creation time，会话来自configure；stdout只有hello/snapshot；配置刷新不增加进程。

- [x] **Step 1: Write backend adapter tests**：默认ControlMode不读取历史值，写入不保存；所有非CPU/GPU类别false；父/子可信门控独立；自有源码控制接口调用扫描完成。
- [x] **Step 2: Build/run to verify red then implement adapter/host**：adapter 16断言失败、host 7断言失败后实现；EOF/父身份、输入边界、单hello、无设备状态帧通过。真实ReadDevice用固定接口替身做设备级隔离，临时禁用collector得到6断言失败，恢复后全绿。
- [x] **Step 3: Write controller process tests**：受控替身覆盖启动预算、断管/退出、协议拒绝、stderr洪流16KiB、心跳超龄/30秒退避、同PID刷新、需求取消与迟到准备、Job分配失败、至多一个后端；18断言失败后实现。
- [x] **Step 4: Implement controller**：单后台worker串行拥有会话；Job kill-on-close、固定工作目录/父身份/租约、有界停止；UI调用立即撤销缓存，错误不续命旧值。19控制器测试通过。
- [x] **Step 5: Build payload**：Roslyn/C#4/完整9分析器生产构建；24文件逐个GZip/round-trip校验，manifest最后发布；主程序真实25资源在独立scratch根逐文件校验；失败staging清理和旧载荷不变验证通过。
- [x] **Step 6: Run adapter/controller/safe tests**：后端完整9分析器编译及35/35测试通过（0.203679s）；主程序本地net40/C#4/warnings-as-errors重建及516/516安全测试通过（12.8692845s）。两个csproj结构构建/配置、测试AssemblyInfo通过。无安装/无孤儿；主程序完整canonical分析器仍为Task5关卡。原PID23072在系统重启后不再存在，真实任务仅注册时间变化，EXE/HKLM/共享驱动未变；详见验收报告。

### Task 4: Temperature coordination, lifecycle and minimal UI

**Files:** Create `src/Interfaces/ITemperatureConfiguration.cs`, `src/Service/Monitoring/Sensors/WmiTemperatureSource.cs`; modify `TemperatureSampler.cs`, `ResourceMonitor.cs`, `ResourceSnapshot.cs`, `MonitoringViewModel.cs`, `MonitorText.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, related existing tests。

**Interfaces:**
- `ITemperatureConfiguration.ConfigureTemperatures(int refreshSeconds)` 内部可选接口；ResourceMonitor实现，VM只在采样工作线程调用。
- 快照 `SensorBackendStatus : string` / `SensorDriverStatus : string`；VM提供本地化状态行与安装进行中属性，安装操作与采样共享可信服务但不共享UI阻塞锁。
- WMI独立LHM/OHM来源，提供者默认进程存在判断与30秒失败退避，共享selector禁止热点/距离冒充。

- [ ] **Step 1: Write failing coordinator tests**：通用可用胜过原生，通用CPU失败/GPU成功可独立降级；有效原生仍探测通用；过期不显示旧值；所有来源失败“—”与原因；无提供者不加载WMI；OHM门控独立。
- [ ] **Step 2: Implement coordination**：通用→ASUS/NVAPI→LHM/OHM，fallback详情标注真实提供者及降级原因；保留既有原生P/Invoke语义。
- [ ] **Step 3: Write failing lifecycle/config tests**：1/2/5秒传配置；温度需求关闭/暂停停止；隐藏主窗但条仍需温度继续；UI Dispose不与pump采样并发释放且不等待2秒。
- [ ] **Step 4: Implement optional configuration/status**：VM样本后台传刷新间隔，monitor的资源关闭保持pump串行；安装完成标志由后台重置后端。
- [ ] **Step 5: Implement minimal UI**：既有温度/设置区域一行状态+安装按钮；明确确认说明官方共享系统驱动/只读用途/可能重启，取消不执行；中英/主题切换更新，条布局不变。
- [ ] **Step 6: Run safe tests / isolated real window**：绑定日志0错误、紧凑横竖尺寸/焦点/穿透回归；不运行安装器；提交 `feat: integrate universal temperature status and safe fallback`。

### Task 5: Evidence, canonical CI and release

**Files:** Modify build scripts, both workflows, assembly version, README/CHANGELOG/guide; create `tools/verify-sensor-payload.ps1`, `docs/universal-temperature-report.zh-CN.md`。

**Interfaces:** `build-community.ps1 -Tests -Package -MSBuildPath <VS MSBuild>` 按依赖恢复→后端→载荷→主EXE→两套安全测试→内嵌完整性验证→发布资产顺序执行。

- [ ] **Step 1: Add build verification**：比较主EXE内嵌每文件与本次后端/锁定依赖字节；缺载荷构建不能发布；运行全部自有源码完整分析器，不放宽规则或扩大Desktop排除。
- [ ] **Step 2: Run local functional/fault checks**：真实提供者/状态连续样本，1/2/5秒、取消/暂停/退出，无驱动也验证GPU独立工作；受控后端挂起/退出后UI保持响应。
- [ ] **Step 3: Run performance evidence**：2秒模式3×30秒预热/60秒测量；分别记录主/后端/合计CPU私有内存、UIp95；10分钟留存；目标合计全机CPU中位<1%、UIp95≤50ms、增长≤10MiB。未授权CPU驱动和无AMD/IntelGPU实物明确未验。
- [ ] **Step 4: Update docs/version**：查询远端tag占用，AssemblyVersion/FileVersion=3.0.8.4且Info=3.0.8.4-community.4；成本/运行时/驱动确认/非全硬件保证写明，新证据不借用历史数据。
- [ ] **Step 5: Push and run canonical workflows**：普通非强推；网络失败逐blob/tree核对后Git Data API force=false；先无发布的canonical构建排除分析器错误，修具体错误后复验；全部绿色再发布未占用tag。
- [ ] **Step 6: Verify final release**：下载最终EXE到无旁置文件干净目录，校验版本/sidecar/latest/内嵌载荷和实际准备；不启动受保护身份冲突的生产实例；隔离验证不得修改真实设置。
- [ ] **Step 7: Verify cleanup**：用户EXE hash、PID23072creation time、HKLM32/64与原任务未变；无测试/采集孤儿；发布报告区分已验证、模拟与未授权部分；提交证据文档及最终汇报。

## Self-review (2026-10-18)

规格1–4与10由文件职责/Task1–4覆盖；5缓存/打包由Task2/3/5覆盖；6名称/默认控制由Task1/3覆盖；7协议时序/Job由Task1/3/4覆盖；8来源/状态与9显式安装由Task2/4覆盖；11测试/性能/环境与12发布由Task5覆盖。五类Review Focus各有指定测试。接口名和后续消费一致；计划不引入清理/控制/服务/UI重构。按用户“别停下来”采用本会话直接执行，不再等待重复设计审批；实际驱动安装授权门槛不变。
