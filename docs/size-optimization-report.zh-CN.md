# 体积优化验证报告：lhm-0.9.6-p2

历史候选：应用 **3.0.8**、载荷 **`lhm-0.9.6-p2`**。本轮仅改变载荷与安装包交付，保留此前刷新优化及未提交改动；当时未签名或发布。本次只做文档脱敏，不重跑检查。下列 `build/...` 均相对仓库根，**仅本地保留、不随发布公开**；截图/日志不嵌入公开文档，旧冻结清单仍对应当时原件。

## 1. 结果：便携体积目标通过

按原目标，以 `da2cf8b` 的同工具链 Release 为正式基线，最终 EXE 与新增必需分发 DLL/运行资源合计增长不超过 1 MiB；测试工具、运行缓存和可选外部软件不进入便携运行包。

| 项目 | 实测字节数 |
| --- | ---: |
| 正式基线 Release | 434,176 |
| 上一轮刷新优化后的 Release | 5,004,800 |
| 本轮 Release / 必需分发运行文件合计 | **1,388,032** |
| 上限：正式基线 + 1 MiB | **1,482,752** |
| 本轮便携包全部文件合计，包含说明、许可与校验文件 | **1,441,798** |
| ZIP 下载大小 | **1,042,188** |

- 相比上一版减少 **3,616,768 B（72.27%）**。
- 相比正式基线增加 **953,856 B**，必需运行文件距离上限仍有 **94,720 B**。
- 即使把包内全部说明文件计入，也仍有 **40,954 B** 余量。
- 必需分发运行文件只有 EXE，**没有把必需 DLL 移到 EXE 旁边**。温度后端仍从经过校验的内嵌资源释放到版本化运行缓存；运行缓存不是另外下载的必需依赖，也未混入便携包。

本地证据：`build/size-optimization/verification-final-01/delivery-summary.json`、`payloads/size-summary.json`（后者相对同一验证目录）。

## 2. 体积减少的来源

| 不再内嵌的成员 | 原始字节数 | 原 GZip 资源字节数 | 处理方式 |
| --- | ---: | ---: | --- |
| PawnIO_setup.exe | 3,225,016 | 2,916,529 | 用户确认后获取；保留选择官方本地离线包入口 |
| DiskInfoToolkit.dll | 903,680 | 711,596 | 从 CPU/GPU 温度配置的运行载荷排除，保留包图锁定与排除依据 |

新增安装准备逻辑、说明文字和清单等会抵消少量节省，因此以上资源之和不直接充当最终 EXE 减少值。

只调整压缩算法不足以解决原超限：先前离线 round-trip 比较中，GZip9 仅省 5,992 B，LZMA1 估算省 164,066 B，且尚未计入解压器成本。本轮保持现有 GZip，不引入新解压器。

新的 p2 载荷为 **22 个文件、23 个内嵌资源**，其中有 11 个运行库；后端 EXE、config、第三方许可、锁文件和构建来源继续保留。`Storage` 及其他非 CPU/GPU 硬件组仍关闭，没有改写 LHM 采样逻辑；其余依赖未作未经验证的继续裁剪。

依赖依据：[锁文件](../sensors/dependencies.lock.json)。本地证据：`build/size-optimization/p2-01/payload/sensors-payload.manifest.json`、`build/size-optimization/payload-size-probe.json`、`build/size-optimization/dependency-probe-01/result.json`。

## 3. 安装流程的变化与边界

用户已确认采用“按需获取，保留本地离线包入口”。不再要求单 EXE 自带首次驱动安装所需的全部离线安装数据。

- 未明确确认安装，不准备安装包；日常监控、使用已有驱动不会触发此下载流程。原软件更新功能保持原设置行为。
- 先检查现有驱动。已可用、已有较新或身份不明的共享驱动，不进入覆盖安装流程。
- 本历史 p2 在确认后复用已验证缓存，或从锁定 HTTPS Git blob 获取官方 PawnIO **2.1.0**，请求/长度/准备时间有界且拒绝任意重定向。当前 p3 改为官方 **2.2.0** 固定 Release 来源，见[p3 记录](cpu-driver-prompt-report.zh-CN.md)，不要把历史入口当作当前安装说明。
- 选择本地离线包后仍需确认。取消文件选择直接返回；本地文件无效不偷偷回退下载。
- 下载和本地复制经过相同的固定长度、SHA-256、Authenticode、官方签名者、路径保护及文件/目录租约检查，再交给官方交互安装器。
- “取消准备”或关闭 ViewModel 只撤销尚未执行的准备阶段。安装器启动后不强杀，不静默安装、不自动重启、不接管已有共享驱动。
- 主程序继续保持 .NET Framework 4.0 / C# 4。独立温度后端要求 Windows 10/11 x64 和 .NET Framework **4.7.2 或更新的 4.x**，不是最低 4.8。

实现：[安装包准备](../src/Service/Monitoring/Sensors/SensorInstallerPackage.cs)、[安装工作线程](../src/Service/Monitoring/Sensors/SensorDriverInstaller.cs)、[设置入口](../src/View/Window/MainWindow.xaml.cs)、[安装包测试](../src/Test/SensorInstallerPackageTests.cs)。链接指向当前源码，历史候选行为以当时冻结记录为准。

## 4. 构建与回归验证

| 检查 | 本轮结果 |
| --- | --- |
| 最终主程序安全套件 | **643 项通过**；排除既有 Manual、Desktop 类别 |
| 独立窗口套件 | **28 项通过**；清理、进程动作与安装确认使用模拟/取消 |
| 独立托盘派发套件 | **3 项通过** |
| p2 后端安全套件 | **36 项通过**；后端源码哈希与当前 14 个来源文件一致 |
| 同体积比较工具链的 Debug / Release / Test | 全部成功 |
| 主程序产品 / 测试完整 Roslyn 分析器检查 | 9 个项目声明的分析器全部传入，均 0 警告、0 错误 |
| 后端构建 | 9 个分析器、原规则集、固定 net472 引用及 C# 4；构建信息保留在载荷中 |
| 最终三种主程序内嵌载荷 | 版本、成员、逐项长度和 SHA-256 全部通过 |
| 不含安装器的离线恢复 | 16 个锁定构建包恢复出 11 个运行库；未要求或获取驱动安装器 |
| 便携包 | 6 个文件；逐项 SHA-256、ZIP CRC 与解包内容一致性通过 |
| 绑定与清理 | 窗口测试和真实 Release 截图无绑定警告；真实截图的隔离注册表项已清理 |

主程序体积比较使用现有 .NET Framework MSBuild/Csc 工具链。另用经过来源与签名核验的 Roslyn 4.8.0 对相同产品源码及测试源码补跑完整分析器；**不把它描述为已运行完整 Visual Studio 构建环境**。

分析器发现并修正了下载请求的 `Uri` 重载。取消源归工作线程所有、测试监控引用归 ViewModel 所有，分别添加了仅作用于相应类型/字段的 CA1001、CA2213 所有权说明；有成功/取消后释放及恰好一次释放的测试支撑。未关闭全局规则集或警告即错误策略。

主要本地证据位于 `build/size-optimization/verification-final-01/`：`test-summary.json`、`main-builds/safe-tests.xml`、`window-tests-02/window-tests.xml`、`dispatch-tests/dispatch-tests.xml`、`backend-tests/results.xml`、`analyzers/{release,test}/build-info.json`、`backend-build-info.json`、`offline-restore/verification.json`、`source-state.json`。

编译器和 NuGet 构建输入的获取只用于临时构建/离线恢复验证，不属于应用运行下载，也未混入交付包。原先失败的检查记录没有覆盖；窗口重跑的一次失败来自 Git Bash 转换 `/labels` 参数，修正执行环境后 28 项通过，不是用修改产品代码绕过测试。

本历史比较显式使用 `build/size-optimization/p2-01/payload`，保留旧 p1 对照；不能误把旧默认输出当作 p2，也不能把历史 p2 强行用于当前 p3 构建。当前构建入口与 CI 要求见[根 README](../README.md#运行与构建)，必须生成同源载荷。

## 5. 实机与视觉检查

从最终 p2 载荷校验并解压运行成员，在独立进程中进行初始化、两次 GPU 采样、一次受原驱动信任检查约束的 CPU 请求及释放。全部完成，无缺库解析请求：受支持的 NVIDIA GPU Core 约 **56°C**；CPU 请求保持 **AccessDenied**，没有伪造温度。设备具体型号不公开。

另外只构造并关闭 CPU-enabled `Computer`，未调用 `Open`，**不证明可信驱动 CPU 硬件路径或其他 CPU/GPU 平台已实测**。本地证据：`build/size-optimization/verification-final-01/p2-load-probe/result.json`。

最终 Release 使用真实应用资源与隔离测试设置，在最小展开尺寸检查中文、英文和德文英文回退界面；新安装/离线按钮与说明可见且无重叠，空闲取消按钮禁用。未点击安装、清理或操作用户进程。视觉结论基于真实 Release 截图，不以夹具透明样式图代替；图像及条件记录仅本地保留于 `build/size-optimization/verification-final-01/release-ui/`，不嵌入发布说明。

## 6. 交付与尚未验收的事项

本地历史产物：`build/size-optimization/portable-p2-01/WinMemoryCleaner.Community.exe` 及其 `.sha256`、`README.zh-CN.md`；`build/size-optimization/WinMemoryCleaner.Community-size-p2-portable.zip` 及其 `.sha256`。这些不是公开下载链接，正式下载应使用社区 Releases。

EXE SHA-256：`4edca44bf522349b57a9a30bdf1b172f9fb83464627e66992fbd15ae650d8cac`。

**本轮体积目标已达成，没有剩余体积阻塞。** 安装流程以模拟和隔离文件验证，没有下载或执行真实驱动安装器；真实驱动安装、可信 CPU 温度路径及其他硬件仍未验收。本轮也未重跑原目标规定的 30 秒预热 / 60 秒采样 / 三轮中位数及 10 分钟稳定性全套性能检查，不以旧版、短测或截图替代。上一轮刷新报告及证据保留为历史记录，不自动视为此 p2 版本的完整性能验收。
