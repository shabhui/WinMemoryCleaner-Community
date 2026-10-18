# 通用 CPU/GPU 温度检测设计规格

- 日期：2026-10-18
- 状态：对话设计已获用户确认；用户随后要求“别停下来”，已据此制定计划并进入连续实施。实际系统驱动安装仍须独立明确授权。
- 项目：WinMemoryCleaner Community，`community` 分支。
- 本地代码基线：`14df3fe94396289a1968b76d5f372763d224d50a`；与已发布 `v3.0.8.3-community.3` 的远端提交 `998ad5af9e434c0b3368fcf0d4acf471709cf508` 内容一致。

## 1. 目标与已批准决定

用户要求“把检测变成通用的”，并选择允许增加通用检测组件、管理员权限和必要驱动依赖的方案。随后确认：保留主程序，增加独立采集后端；组件内嵌发布 EXE；驱动安装须明确确认；检测只读、失败诚实降级。

目标是让 Windows 10/11 x64 上受后端支持的 Intel/AMD CPU、NVIDIA/AMD/Intel GPU 自动提供真实温度，不要求用户另行启动 LibreHardwareMonitor，也不把 ASUS 机型作为检测前提。

“通用”表示跨品牌探测受支持硬件，不表示所有处理器、显卡、虚拟机或驱动都必定有读数。缺传感器、驱动不支持、权限不足或读数失效时必须如实显示原因。

### 不变项

- 主程序保持 WPF、.NET Framework 4.0、C# 4、`AllRules.ruleset` 和 warnings-as-errors。
- 保持清理、托盘、设置键、单实例身份、主题、语言和安全进程操作。
- 保持紧凑横向/竖向监控条，不增加常驻标题、大底框、阴影或新的强制弹窗。
- 保持 `WinMemoryCleaner.Community.exe`、`.sha256`、`AssemblyInfo.txt` 的更新通道资产名。
- 不覆盖用户旧 EXE，不结束受保护 PID 23072，不改变清理器的真实设置或开机任务。
- 安装驱动是独立的显式操作，不能由采样、测试、自动更新或首次启动静默触发。

## 2. 已核实的依赖与兼容性

| 项目 | 选定输入/事实 | 约束 |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6；上游提交 `3d331e3370efb858411f19511373eff65a218701` | 使用该版本的 `net472` 构建，禁止构建时浮动到最新版本 |
| 官方能力说明 | Intel/AMD CPU、NVIDIA/AMD/Intel GPU；部分传感器需要管理员权限 | 最终支持仍取决于型号、驱动和传感器是否可用 |
| 主项目 | `TargetFrameworkVersion=v4.0`、`LangVersion=4`，清理器已要求管理员权限 | 不能直接加入要求 net472 的编译引用 |
| 采集后端 | 新增 x64、net472、C# 4 可执行项目 | 自有源码按相同规则集和警告即错误检查；第三方库使用固定版本编译产物 |
| PawnIO 安装器 | 上述 LHM 提交中的 `LibreHardwareMonitor/Resources/PawnIO_setup.exe`，3,225,016 B，Git blob SHA-1 `e5d7ecd7e61d317bd8de961cead52be576be5be2` | 此值锁定官方来源字节，不等同于已完成 Authenticode 或 SHA-256 验证 |
| PawnIO 官方说明 | 官方版有数字签名；另有不检查签名的无签名版 | 本产品只允许官方签名版，不选择 unrestricted 版本 |
| 许可 | LHM 为 MPL-2.0，另附第三方许可；PawnIO 为 GPL-2.0-or-later，含上游例外条款 | 保留许可证、第三方声明和对应源码获取信息；不能仅重分发删掉许可的 DLL/安装器 |

运行时不满足 .NET Framework 4.7.2、系统不是目标 x64 环境或组件被安全策略阻止时，主程序仍可运行，通用后端显示不可用并保留原有温度备用路径。不自动安装 .NET 运行时，也不要求用户关闭安全启动、内存完整性、驱动阻止列表或反作弊。

## 3. 方案选择与范围

### 采用：独立采集后端

主程序不引用 LHM。独立进程加载 net472 库，通过受控标准输入/输出交换温度快照。这样避免升级整个 WPF 项目，也让采集库崩溃或硬件调用停滞不会直接卡住主窗口。

### 未采用：整体升级主程序并在进程内加载 LHM

这一方案少一个进程，但会改变主程序框架兼容性，并把采集异常和额外依赖带入清理器主进程。本次不采用。

### 本次不做

- 不自己开发内核驱动，不引入自有 WinRing0 设备访问或安装路径，不绕过驱动签名。
- 不增加风扇调速、超频、电压、功耗、性能模式或 GPU 控制功能。
- 不扩展磁盘、主板、控制器、内存、网络、风扇等额外硬件监控页面。
- 不新增采集服务或计划任务；仅允许经用户确认由官方安装器创建的 PawnIO 驱动服务，不接管或卸载其他软件安装的 PawnIO。
- 不改造为插件平台，不增加手动传感器编辑器，不重做 UI。
- 不宣称在没有实际硬件的情况下完成 AMD 或 Intel GPU 的实机验收。

## 4. 组件边界与数据流

1. **现有监控层**：`MonitoringViewModel` / `MonitoringPump` / `ResourceMonitor` 保持需求调度和资源快照行为。
2. **温度协调层**：`TemperatureSampler` 按 CPU/GPU 分别选择通用后端或备用来源，输出现有 `MetricValue`。
3. **后端控制器**：负责组件准备、启动、受限 IPC、快照缓存、健康监测、退出和重试；不把 DLL 加载进主程序。
4. **共享纯逻辑**：C# 4 的协议 DTO、读数校验和传感器选择规则以链接源码供两个项目使用，不产生需要主程序引用的 net472 程序集。
5. **采集进程**：`WinMemoryCleaner.Sensors.exe` 只启用 LHM 的 CPU/GPU 两类硬件，读取温度后发送快照。
6. **组件载荷管理器**：从主 EXE 的资源准备版本化缓存，校验完整性后交给后端控制器。
7. **驱动安装入口**：只响应主界面的明确用户操作；与正常采样代码分离。

现有 `IResourceMonitor.Sample(bool includeProcesses, bool includeTemperatures)` 的行为保持不变。刷新间隔通过可选内部温度配置接口传到真实监控器，不要求现有测试替身和其他实现增加驱动/进程逻辑。

## 5. 内嵌载荷与旧更新器兼容

### 发布形式

继续发布单个主 EXE。它内嵌后端、必要的固定版本依赖、后端配置、许可证/源码来源说明、载荷清单及官方安装器。手动下载 ZIP 也附带许可证和说明。

因为旧更新器只替换主 EXE，后端不能依赖与 EXE 并排下载的额外文件。后端和依赖版本必须随主 EXE 同时更新。

### 打包规则

- 自有后端由同一 canonical CI 构建；第三方 NuGet 输入固定版本并使用内容哈希锁文件。
- 组件清单由 CI 从最终载荷字节生成，列出载荷版本、协议版本、文件相对路径、长度、资源名称和 SHA-256。
- 官方安装器来源同时核对上述 Git blob SHA-1，并在纳入发布载荷前验证有效 Authenticode 签名；纳入清单后再使用 SHA-256 固定其实际字节。
- 使用 net40 可读取的逐文件压缩资源（框架自带 GZipStream），不为解包另加 ZIP 运行时库。
- 清单不能含任意外部 URL、绝对路径、父目录跳转或未列出的可执行入口。

### 缓存与执行安全

缓存位于 `%LOCALAPPDATA%\WinMemoryCleaner.Community\Sensors\<payload-hash>\`。

- 在独立临时目录完整解包并校验后再发布为可用缓存；不执行半成品。
- 拒绝路径逃逸、载荷路径中的重解析跳转和超出清单的文件内容。
- 在启动前重新校验执行文件、配置和依赖；对这些文件保留只共享读取的文件句柄直至该次后端退出，避免校验后被替换。
- 使用完整路径、固定工作目录和 `UseShellExecute=false`；不从工作区、PATH 或其他进程提供的路径发现 DLL。
- 缓存损坏可从内嵌资源重建；不能通过校验时拒绝启动，并诚实使用备用来源或显示不可用。
- 不在清理器安装目录写入 DLL，不碰用户旧 EXE。无活动句柄时只清理本应用产生的旧载荷缓存。

## 6. 只读采集与传感器选择

### 库配置

只允许 CPU/GPU 两类硬件。GPU 可独立启用；CPU 类仅在官方 PawnIO 驱动验证通过时启用，否则关闭该类并保留已有可信 CPU 备用路径。所有主板、控制器、风扇、磁盘、网络、内存等其他类别关闭。

提供专用 `ISettings` 适配器：`GetValue` 始终返回请求的默认值，`SetValue` 和删除操作不持久化任何内容。不能读取外部 LHM 的配置。

这是安全要求而非可选优化：上游 NVIDIA/AMD GPU 构造和关闭逻辑可能根据已保存的 `ControlMode` 设置/恢复风扇；返回默认 `ControlMode.Undefined` 并且从不调用控制接口，才能避免重放历史风扇设置。

自有代码只调用枚举、更新、传感器读取和资源关闭，不调用 `IControl.SetDefault`、`SetSoftware` 或其他风扇/电压/功耗/性能模式接口。允许后端内部为读取传感器所必需的硬件访问，不把这描述成所有底层 IOCTL 都没有写访问权限。

### 通用读数校验

- 必须是已识别 CPU/GPU 硬件拥有的 `SensorType.Temperature`。
- 值必须存在、有限且在 1–150 °C 范围内；零、NaN、Infinity、缺值及越界均不能作为可用温度。
- 保留硬件标识、品牌/硬件类型、设备名、传感器名、测量类别、采样时间和来源版本。
- 数值转换使用 invariant culture，不根据系统语言改变含义。
- 不把 WMI 的任意 Temperature 字段、ACPI thermal zone、主板区域、显存、热点或到阈值的距离当成 CPU/GPU 核心温度。

### CPU 选择规则

先在每个 CPU 设备内部按下列优先级选值，同一优先级有多个有效候选时取最高值。名称用 OrdinalIgnoreCase 全名或完整数字模式匹配，不作任意子串猜测；数值相同时按设备标识、传感器标识的 Ordinal 顺序决定，保证来源显示稳定：

| 优先级 | 认可的传感器类别/名称 | 显示来源含义 |
| --- | --- | --- |
| 1 | `CPU Package` | 封装温度 |
| 2 | AMD `Core (Tdie)` / `CPU (Tdie)` | 芯片温度 |
| 3 | AMD `Core (Tctl/Tdie)` / `CPU (Tctl/Tdie)` | 上游报告的合并 Tctl/Tdie，不自行增加/减去偏移 |
| 4 | AMD `CPU Die (average)` | 芯片平均温度，明确标注 |
| 5 | AMD `CCDs Max (Tdie)`，或有效 `CCD<n> (Tdie)` 的最高值 | CCD 最高温度，明确标注而非封装温度 |
| 6 | `Core Max`、明确标识的逐核心温度（`Core #<n>` / `CPU Core #<n>` 等固定模式） | 核心最高温度 |

明确拒绝单独 `Tctl`、`Distance to TjMax`、未识别的控制温度、主板 CPU 区域和任意名字含 CPU 的其他字段。Intel 上游将 `Distance to TjMax` 也标为 Temperature，因此不能仅检查传感器类型或对所有温度取最大值。

多个 CPU 各自产生一个有效代表值，再取最高代表值；详情列出具体设备和测量类别。没有认可候选就保留不可用，不用不明确的传感器凑数。

### GPU 选择规则

仅接受识别为 NVIDIA/AMD/Intel GPU 的核心温度，如 `GPU Core` 或明确的 `GPU Temperature`。拒绝 `GPU Hot Spot`、`Memory Junction`、`GPU Memory` 和阈值距离，不将它们作为核心温度的备用值。

每个 GPU 先选有效核心温度；多个 GPU 延续现有“最高有效 GPU 温度”语义，并在详情中标注对应显卡。未提供核心温度的设备不参与比较。

共享选择规则也应用于外部 WMI 备用来源。NVAPI 的 GPU target 和 ASUS 固定 CPU 传感器保留其已核实语义，不重新按自由文本猜测。

## 7. 协议、时序与进程生命周期

### 协议

使用父子进程专属的重定向 stdin/stdout，UTF-8 JSON Lines，协议版本 1；诊断输出只能到 stderr。

- 主进程消息仅允许配置（刷新间隔、经验证的 CPU 驱动可用状态、会话标识）和停止；不支持执行 shell、安装驱动、任意文件或寄存器命令。
- 后端握手包含协议/载荷版本和会话标识；快照包含递增序号、UTC 采样时间、后端/驱动状态以及每个已选 CPU/GPU 的读数或失败状态。
- 每帧最多 64 KiB、递归深度最多 16、设备代表值最多 64 个，设备名/传感器名最多 256 字符、标识最多 512 字符。
- 读取器必须在换行前执行长度上限，不能用无限制的 ReadLine 累积任意输出。
- 不兼容协议、未知会话、损坏 JSON、结构/类型错误或超长帧终止本会话并按 30 秒退避；乱序/重复序号及异常时间的帧丢弃且不刷新新鲜度。单个不合规传感器读数只排除该项，不牺牲同帧其他有效设备。
- 共享协议 DTO 不包含直接加载程序集、反序列化类型或执行代码的能力。
- stderr 在后台持续排空，仅保留最近 16 KiB 的诊断；超出保留量的内容丢弃，不无限增长日志或阻塞子进程。

### 时序

- 后端采样间隔跟随已有 1/2/5 秒选项；间隔修改发配置消息，不重新启动一个进程。
- 准备组件和启动握手在后台执行，首次握手/有效状态帧的预算为 10 秒。
- 快照最大年龄为 `max(10 秒, 3 × 刷新间隔)`；年龄按有效新帧的本地单调接收时间及其采样时间共同验证。采样时间比本机 UTC 超前超过 5 秒，或其年龄超过该上限时拒绝快照。
- 心跳/快照超过同一预算、管道断开或后端崩溃时，立即使通用数据失效并切到有效备用来源。
- 后端启动/故障重试间隔为 30 秒，不逐采样创建新进程，也不密集探测缺失驱动。
- 驱动安装或驱动状态变化后，主动重置退避并重新启动后端，让静态驱动检测重新执行。

### 生命周期

- 一个主实例最多拥有一个采集后端；仅当现有监控需求要求温度时启动。
- 需求关闭、监控暂停和 Dispose 时撤销会话/快照并停止该后端。主窗口隐藏但监控条仍需要温度时继续采样。
- 后端位于本实例自己的、关闭时结束成员的 Windows Job 中；分配失败则拒绝保留一个脱离管理的后台进程。
- 正常退出发送停止，后台最多等 2 秒，随后仅终止本实例拥有且身份仍匹配的后端；不按通用进程名批量结束进程。
- 后端也观察 stdin EOF 和父进程身份，父进程退出则关闭资源退出。主程序崩溃时 Job 收尾，不能留下孤儿采集进程。
- 采样、握手、等待退出和安装器等待均不得在 WPF UI 线程阻塞。

## 8. 自动来源选择与状态

CPU/GPU 独立选择，不要求两个值来自同一个提供者：

1. 新鲜、有效的通用后端温度。
2. 已有只读 ASUS CPU / NVIDIA NVAPI GPU 来源。
3. 已运行的 LibreHardwareMonitor WMI；同时扩展 OpenHardwareMonitor WMI 的兼容探测。
4. 没有有效来源时显示不可用及有限长度的原因。

通用后端启动过程中可以先显示有效的原生备用值。即使 ASUS/NVAPI 可用，也不能跳过通用后端探测，否则仍会把该机型当作默认前提。

WMI 保留“先判断提供者存在再加载 System.Management”的轻量策略；新增 OpenHardwareMonitor 使用独立存在判断与退避。不能在没有来源时每一帧扫描 WMI，也不能把进程默认名称检测描述成覆盖所有改名版本。

继续使用现有 `MetricStatus`：

| 状态 | 示例 |
| --- | --- |
| Available | 经过有效性/新鲜度校验的通用或备用温度 |
| WarmingUp | 后端正在启动，没有有效备用值 |
| Unsupported | 型号未提供认可传感器、运行时不满足、缺兼容驱动且没有备用值 |
| AccessDenied | 所需设备/来源访问被拒绝且没有有效备用值 |
| Error | 完整性失败、协议异常、崩溃、超时或异常读数且没有有效备用值 |
| Disabled | 温度采样不在当前需求中 |

详情包含提供者、设备、传感器/测量类别及失败原因。新后端失败但备用来源有效时，值仍是 Available，并把降级事实注明在详情中。监控条仍仅显示数值或“—”，原因留在主窗口及已有提示中。

## 9. PawnIO 安装与最小 UI

在既有资源监控的温度/设置区域增加一行通用组件状态和“安装通用温度驱动”操作，不改变监控条布局，不新增开机自启项或逐次采样弹窗。

### 安装流程

1. 用户点击操作；确认框解释将安装系统范围、可被其他软件共享的 PawnIO 驱动，说明只用官方签名版、不控制风扇/功耗以及可能需要重启。
2. 用户取消则不启动安装器、不写服务/注册表，采样继续诚实降级。
3. 只允许固定载荷中的安装器：核对哈希和有效 Authenticode 签名，并在安装进程存活期间保持对应文件的防替换读取句柄。
4. 启动官方可交互安装器，使用必要管理员权限；不传静默、unrestricted、关闭安全功能或自动重启参数。
5. 只允许一个安装操作；UI 显示进行中，不阻塞清理/监控。不得从后端或其 IPC 消息发起安装。
6. 安装器结束后重新检查驱动就绪及签名状态，再重启后端验证真实读取；不把退出码 0 或单个注册表标记直接当成温度可用。
7. 取消、失败、需要重启或安全策略阻止时给出原因。需要重启只能提示，不替用户重启。

不能把无签名/unrestricted 安装识别成受支持驱动，也不能自动覆盖或降级一个已安装的更新版本。签名/驱动模式无法可靠确认时，通用 CPU 路径保持不可用；GPU 及已有可信备用接口仍可独立工作。

正常运行只读检查 PawnIO 状态。温度关闭、卸载清理器或清理缓存时不卸载共享驱动；用户通过 PawnIO 官方安装器或 Windows 的应用卸载入口处理驱动。

本规格审批不等同于授权在当前机器立即运行安装器。真实安装验证必须由用户在新版操作入口确认，或另行明确授权本次测试执行；没有授权不得写系统驱动状态。

## 10. 实施边界

计划新增/调整的区域：

- 独立后端项目及其 C# 4 源码、依赖版本/内容哈希锁文件、后端测试项目。
- 共享温度协议、候选读数、选择与新鲜度纯逻辑。
- `TemperatureSampler`、后端控制器、内嵌载荷管理器、只读驱动状态/签名检查、显式安装服务。
- `ResourceMonitor` / `MonitoringViewModel` 的内部配置与状态接线；主窗口现有温度/设置区域的最小入口。
- 中英文本及 fallback 文案，不改其他语言的既有加载机制。
- `build-community.ps1`、监控构建工具、Community CI / release 工作流：同一 CI 构建后端、生成载荷和主 EXE，验证包完整性。
- README、CHANGELOG、监控指南及单独的新版本验收报告；旧版证据保持历史身份。

不把第三方现代 C# 源码直接复制到主项目，不放宽主项目的 LangVersion/规则集来迁就它。第三方二进制依赖不宣称已经接受本仓库的 C# 4 分析器检查。

## 11. 测试与验收标准

### 自动化测试

- 使用固定候选数据覆盖 Intel Package、Core Max、Distance to TjMax 排除，AMD Tdie/Tctl 合并/CCD 及偏移语义，多 CPU、多 GPU、Intel GPU Core，热点/显存排除。
- 覆盖缺值、零、边界、NaN/Infinity、硬件身份不匹配、非 invariant culture、所有来源失效及独立 CPU/GPU 降级。
- 覆盖配置默认值适配器，不重放控制设置，且自有代码没有控制命令调用。
- 覆盖协议版本/会话、重复序号、过期/异常时间、超长帧、损坏 JSON、后端拒绝配置、退出/超时/崩溃和 30 秒退避。
- 覆盖载荷缺失、哈希不符、路径逃逸、原子准备、并发准备以及旧更新器只替换 EXE 后的后端准备。
- 覆盖安装取消、无效签名、未授权、重复安装、已存在新版本、失败/需重启及完成后重新检测；CI 使用替身，不安装驱动。
- 既有安全套件继续通过；新增纯逻辑和进程测试不得通过扩大 Desktop 排除范围来隐藏失败。
- 后端自有源码与主源码均由 canonical Visual Studio MSBuild 按 C# 4、warnings-as-errors 和 `AllRules.ruleset` 构建。

### 运行验收

- 温度需求关闭时没有常驻后端；切换 1/2/5 秒、隐藏主窗保留监控条、暂停/恢复和退出均按生命周期工作。
- 当前 Intel/NVIDIA 机器验证设备名、传感器名和连续真实温度；有可用且授权的 PawnIO 才计入通用 CPU 实机验证。
- 没有相应物理硬件时，AMD CPU/GPU 和 Intel GPU 只能报告为算法/协议夹具验证，不写成实测通过。
- 禁止以旧 ASUS/NVAPI 读数冒充新通用后端的验收；记录每个样本的真实提供者。
- 后端强制挂起/退出后，主窗口继续响应，旧通用值到期失效，备用温度或明确不可用状态正确显示。
- 自有 UI 文案中英切换、主题切换和新状态行不产生绑定错误，不改变紧凑条的布局和焦点/穿透行为。
- 未安装驱动测试与已安装驱动测试分开记录；驱动安装缺用户授权时明确列出未验部分。

### 性能与收尾

- 温度开启的 2 秒模式运行三次 30 秒预热/60 秒测量，分别记录主进程、后端及合计 CPU/私有内存；合计 CPU 中位数低于全机归一化 1%，UI 调度 p95 不高于 50 ms。
- 做一次 10 分钟留存，预热结束至结束的合计私有内存增长不超过 10 MiB；同时记录首启和稳态开销，不用旧版本报告代替。
- 温度关闭时不出现新的常驻进程；开关、刷新修改、连续启动/退出没有孤儿后端或残留测试进程。
- 验证用户旧 EXE、PID 23072 身份、清理器 HKLM 设置及原开机任务保持原样。若用户另行批准安装，新增 PawnIO 自有系统状态单独记录，不混作清理器设置修改。

## 12. 发布与完成定义

- 发布二进制只能来自 `community-release.yml`，后端及载荷必须属于同一次构建。
- 下一版从高于当前 3.0.8.3 的 AssemblyVersion 发布；预定为 `3.0.8.4-community.4`，发布前检查远端 tag 未占用，已发布 tag/资产不覆盖。
- 保持 EXE 校验和、版本公告及 ZIP；CI 额外验证主 EXE 内嵌载荷与本次后端输出及依赖锁定输入相符。
- 下载最终 Release 的主 EXE，在没有旁置后端的干净目录验证载荷准备、版本资源、EXE 哈希与公告；不得仅用本地开发二进制宣称发布版通过。
- 验证 `releases/latest`、`AssemblyInfo.txt` 和 `.sha256`；README/CHANGELOG 写明后端运行时、额外开销、一次性驱动安装及非全硬件保证。
- 通过相关构建、自动化测试和已授权实机验收后推送 GitHub 并发布。网络不通时仍可使用逐 blob/tree 核对的 Git Data API，禁止强推或覆盖用户改动。
- 未完成的真实硬件/驱动/独立启动条件如实列出，不能以模拟数据、备用路径或历史报告填补。

## 13. 本次规格阶段的验证事实

目前只做了仓库及官方资料的只读核查：主项目/发布脚本、现有 MetricValue/监控需求、上游版本/框架/传感器命名、控制默认行为、官方驱动说明及安装器来源元数据。

尚未安装依赖、运行后端、校验安装器实际签名、安装驱动、修改程序或对新版执行硬件/性能测试。上述验证属于后续实施及验收门槛，不是本规格已经完成的动作。

## 14. 主要来源

- [LHM 0.9.6 发布](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6)
- [固定提交的 README：支持硬件、框架与管理员权限](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/README.md)
- [固定提交的库项目与依赖](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj)
- [PawnIO 设备访问与安装状态](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/PawnIo/PawnIo.cs)
- [Control 的默认 Undefined 行为](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Control.cs)
- [NVIDIA GPU 控制初始化/关闭](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs)
- [AMD GPU 控制初始化/关闭](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Gpu/AmdGpu.cs)
- [Intel 温度传感器命名](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs)
- [AMD 温度传感器命名](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs)
- [官方安装器来源](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitor/Resources/PawnIO_setup.exe)
- [PawnIO 官网：官方签名版与 unrestricted 版](https://pawnio.eu/)
- [PawnIO 官方源码与许可](https://github.com/namazso/PawnIO)
