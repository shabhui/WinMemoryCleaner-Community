# 通用 CPU/GPU 温度检测验收记录

> 发布完成：`v3.0.8.4-community.4` 已由 canonical 发布工作流 `39567637435` 于提交 `9e4876dc56cb43959e29226bff8ae9227870ca6e` 构建并发布（稳定、非草稿、未签名）。该次运行记录主套件 **554/554**、后端 **36/36**（零错误/失败/未运行）及源码包 2/2；七项正式资产经独立下载审计，最终 EXE 的内嵌载荷与源码指纹复核通过，并用该发布 EXE 完成隔离功能回归 **75通过/0失败/1不可验证**。本机通用 CPU 的受信驱动路径、AMD CPU/GPU 与 Intel GPU 实物仍未验证，不代表全部硬件通过。

## 固定架构与安全边界

- 主程序 WPF / .NET Framework 4.0 / C# 4；独立后端 x64 / .NET Framework 4.7.2 / C# 4。
- 固定 LibreHardwareMonitorLib 0.9.6（`3d331e3370efb858411f19511373eff65a218701`），12 个运行时 DLL 的长度和 SHA-256 由 `sensors/dependencies.lock.json` 锁定。
- 所有组件、配置、许可证、锁文件和官方 PawnIO 2.1.0 安装器以逐文件 GZip 内嵌；无需 EXE 旁置后端目录。Task 3 压缩资源合计 **4,392,443 B** 为阶段数据；最终发布主 EXE 为 **4,957,184 B**（SHA-256 `a2ca707d…a122780`），其 24 个内嵌文件已与同次 canonical 报告逐项核对。
- 安装器仅为数据；开发、单测、采样、构建和更新都不执行。实际驱动安装仍未获用户独立授权。既存共享 PawnIO 不覆盖、不降级，不使用 unrestricted/无签名模式。
- 自有代码只启 CPU/GPU，不调用风扇、功耗、电压、超频或性能模式控制接口。上游底层硬件访问并不等同于所有 IOCTL 都具有“只读访问权限”。
- CPU 只有父/子两端都通过固定官方标准驱动信任检查才启用。当前 Flydigi 共享驱动不是接受的标准镜像，CPU 通用来源必须诚实降级；不能描述为“机器未安装 PawnIO”。
- 固定 LHM 的 Intel GPU 分组依赖 CPU 分组，因此相同可信驱动门控未通过时，Intel GPU 通用来源也不可用；NVIDIA/AMD GPU 分组仍可独立启用。

## 本地红绿与当前证据

| 检查 | 证据 / 结果 |
| --- | --- |
| 共享选择规则 | 17 个断言失败后实现；协议 21 个断言失败后实现；共享 GREEN 47/47 |
| 可信载荷 / 显式安装替身 | 14 个断言失败后实现；安装/信任 GREEN 21/21；固定恢复 16 包 / 12 DLL；未运行安装器 |
| 后端适配器 / 宿主 | adapter RED 16、host RED 7 后实现；固定父身份/EOF/配置/无设备状态帧通过 |
| 设备级异常隔离 | 真实 `ReadDevice` + 固定 IHardware/ISensor 接口替身；禁用 collector 的 mutation RED 6，恢复后 GREEN；没有打开真实硬件 |
| 受控子进程 / Job | 初始RED 18后实现；当前23个控制器测试覆盖有界帧/序号/UTC/过龄/退避/stderr/准备取消租约释放/慢准备预算/暂停恢复/刷新/Job失败/正常stop与无响应停止 |
| 后端完整分析器 | Roslyn **4.8.0-7.23558.1**，C#4、x64、固定 net472 references、`AllRules.ruleset`、9 个声明分析器、warnings-as-errors；产品及测试编译通过 |
| 后端完整安全测试 | **35/35，0.203679 s**，0 errors / 0 failures |
| 两后端 csproj 结构检查 | 本地 in-box MSBuild + Roslyn 编译器均 exit 0；测试链接 AssemblyInfo，版本 3.0.8.4；配置 1,840 B；此项不冒充 canonical VS 分析器构建 |
| 载荷构建 | **24 个文件 / 25 个内嵌资源**；逐个 GZip round-trip、长度/哈希、锁定 DLL/安装器一致；失败编译器退出42时无新 staging 且旧载荷字节不变；manifest 最后发布 |
| 真实主程序集内嵌验证 | 从当前程序集真实 manifest/gz 在隔离 scratch 根 Prepare；逐文件长度/SHA/lock/build-info、后端 metadata、资源名全部验证；不写真实用户缓存、不运行 EXE/安装器 |
| Task 3 主安全套件（阶段历史） | 本地 net40/C#4/warnings-as-errors 重建后 **516/516，12.8692845 s**；不是当前工作树最终结果 |
| Task 4 协调/WMI/生命周期/调度/控制器 | 定向 **44/44，14.261 s**；驱动 reset 不恢复取消需求，慢暂停不占 UI 状态锁 |
| Task 4 设置状态与显式确认替身 | 四项状态/中英/de fallback、取消零创建、进度失败码5、下一后台样本 reset、迟到通知不恢复需求；最终完整安全套件中 **4/4** |
| Task 4 正确无头同场回归 | 旧 `WinMemoryCleaner.CommunityTests.HeadlessConcurrencyTests` 两项与设置四项同场；修复旧测试自建 Application 残留后 **6/6，0.2663944 s**，保留逐测试60秒超时 |
| Task 4 完整主安全套件（阶段历史） | **542/542，15.4915701 s**，零错误/失败/未运行；包括 ASUS15、原生采样11、控制器20、真实内嵌2、旧无头2及新增设置4；仅排除原有 Manual/Desktop |
| Task 4 独立实窗回归 | 独立单STA、不加逐测试timeout的 Desktop runner **14/14，3.0066368 s**；验证绑定、紧凑横竖布局、穿透/位置/语言、真实确认对话框取消。没有本轮新截图人工视觉验收 |
| Task 4 生产配置 / 后端复验 | 最新本地生产 Release/Debug 与隔离 Test 重建 exit0（in-box开发检查）；后端完整9分析器编译及 **35/35，0.1955534 s**；主完整分析器仍待 canonical VS CI |
| Task 5 当前主完整分析器 | 本地 Roslyn **4.8.0-7.23558.1**、显式完整9分析器、C#4/net40、AllRules、warnings-as-errors，审查修复后生产/测试两构建 exit0；生产EXE SHA `97a4fc282eb30471192378e6480e6da026f819fb8cbe522c2fa6906a736a7234`，不是 canonical VS 发布二进制 |
| Task 5 最新主安全复测 | **551/551，20.9468874 s**，0 errors/failures/not-run；完整9分析器的隔离Test构建，原Manual/Desktop排除及60秒单测试界限保留；同源后端完整9分析器新编译后 **35/35，0.23846 s** |
| Task 5 最新真实内嵌与元数据 | 完整主套件含真实Prepare **2/2**；生产EXE独立静态核对主/后端3.0.8.4、24文件/25资源/14源码指纹/9分析器；清单SHA `781f9abfb1e5a59413c60bd6dcb7d982688f2e746d350b2251972e9020fc37b7`；静态验证不运行EXE/安装器，不写用户缓存 |
| Task 5 对应源码 | 缺producer的实际RED后实现；三份固定档案与四份许可的真实字节、10条目报告通过；同长度损坏非零退出且无ZIP；仓库内行为测试2/2，联网新下载三份也全部匹配固定长度/SHA |
| Task 5 构建契约 | 8/8 本地入口契约通过；源码包接入同次 `-Tests`，`-Package` 要求VS及完整测试，便携ZIP附六许可，release交付独立源码ZIP/校验/第三方说明；远端执行仍待验证 |
| Task 5 准备取消真实红绿 | 两项受控后端回归先取得 **2/2失败、0errors，10.4192462 s**：Dispose/Suspend后租约未释放；清理移入finally后 **2/2通过，0.8150747 s**，再跑545/545完整套件 |
| Task 5 最新隔离生产实窗 | `final-functional-3`：**75通过 / 0失败 / 1不可验证**（含4项清理检查），退出码0；生产EXE SHA `484a3273e22ffe1d415be53a532b8d7e98797ca314e6b22aaf87a1496bf10b29`。1/2/5秒连续帧、单后端、暂停/恢复/设置/最小化/退出、挂起/退出故障、中英选择文字和确认宽度均通过；绑定日志0 B，UI调度p95 **0.3744 ms**。宿主不激活并在屏幕外创建真实窗口，不冒充人工视觉检查 |
| Task 5 界面回归红绿 | 新断言先暴露布局翻译后选中项为空及确认框954.67 DIP过宽；固定选项对象、仅本入口限制宽度后独立STA实窗 **14/14**，包含中英文显示文字、竖向布局/自定义位置和取消零安装 |
| Task 5 独立审查修复 | 单次只读审查无Critical/Important；父会话复现6处失败（慢准备预算、stop、三种非法路径、遗留暂存），修复后551/551。新增清理边界用例确认目录联接目标及其他版本不被删除；发布分支和标签404门控、安装器系统工作目录同步收尾 |

分析器例外在 `sensors/GlobalSuppressions.cs` 逐项给出理由：协议/硬件边界的一般异常隔离、旧 FxCop 的弃用提示、后端未实例化的共享新鲜度类型、借入流的所有权，以及 net40 兼容证书 `Reset()` 的 native handle 释放。未修改 `AllRules.ruleset`，未扩大 Desktop/Manual 排除。

### 验证中的真实异常

- Task 3/5 完整套件反复出现真实 `Directory.Move` IOException（1 error、0 failures），单独内嵌测试能通过。没有扩大排除或只反复重跑：实际 NTFS 探针确认解包文件的非删除共享读取句柄会拒绝父目录移动，目录元数据句柄本身不会。最终回归在最后资源关闭后持有真实文件句柄，先取得2测试/1断言失败/0errors的RED，再实现最多1秒、100ms间隔的访问/共享/锁定错误重试；每次检查重解析及目标不存在，移动后仍锁定并SHA校验全部文件。永久占用有限失败且无ready目录；最新544/544全绿。
- 上次外层调用重定向与脚本内部 `safe-tests.log` 冲突，没有有效新 XML。6,019,444,228 B 的 scratch 日志已保留有界头尾诊断后删除；后续外层日志与内部日志分开。
- 后端测试分析器实际发现并修复四个空数组分配、缺少测试版本 metadata 和 NUnit `params object[]` 特性的 CLS 声明问题；未跳过测试或弱化分析器。
- 实窗工具第一轮在会话已放弃/后台清理时尝试观察旧Process，第二轮可见消费状态中途丢失（原记录没有WindowState证据，不能断言根因）。观察仅接受未放弃的Running会话，并新增窗口状态/消费诊断；最新完整轮次零失败，旧两轮失败日志保留，不当作最终证据。外层先以受控退出码7的空进程验证保留Handle/Refresh的退出采集，空退出码现在明确失败。
- `RunSession`原先的提前return跳过尾部Cleanup，准备取消会永久持有租约并阻断后续会话。先用真实受控Prepare+可写锁检验得到两项行为RED，再移入finally；没有靠强制GC或PID=0冒充释放。
- 翻译时替换ComboBox字符串列表使WPF保留SelectedIndex=1却清空SelectedItem；仅过滤中间语言通知仍失败。实际控件诊断证明重复UpdateTarget无效，最终保留固定ComboBoxItem、只绑定翻译Content，布局及位置文字回归通过。安装确认框仅本入口增加最大600 DIP及工作区限制，中英文实际宽度588.67/598 DIP。
- 2026-10-21生产功能复验中记录到意外最小化，按产品设计停止了无悬浮层的主窗需求；失败轮次保留。宿主原先仅Show而不恢复Normal，现明确恢复可见场景所需状态；不能把这些失败轮次计作通过。
- 独立审查指出准备耗时误计入首帧预算、正常停止直接Kill、非法路径异常外泄及暂存遗留。先取得63项定向回归中的3 errors/3 failures，再修复：计时从子进程启动后开始，正常stop后台最多等2秒后才终止所属子进程，非法路径返回不可信/验证失败；暂存名按清单哈希前80位隔离并在完整哈希互斥下回收，正式缓存、清单和逐文件验证仍使用完整SHA-256。80位仅用于短临时目录前缀，以保持.NET4路径长度兼容。
- 源码行为测试以正式入口 `powershell.exe -File` 调用，两项均通过且进程退出0；此前外层的退出1来自直接调用后残留的预期损坏子进程退出码，未证实canonical调用存在缺陷，不修改生产者或放宽门槛。

### Task 4 实现与测试宿主修正

- 通用后端始终优先探测；CPU/GPU 分别降级 ASUS/NVAPI、独立 LHM WMI、独立 OHM WMI，已有有效指标不会被后续来源覆盖。失败/过期不重放旧值；两个提供者分别使用30秒存在/失败门控，未发现提供者不加载查询。
- `MonitoringSession` 在后台串行懒创建、配置、采样、暂停与释放；UI 只用短状态锁撤销需求，旧样本和迟到安装完成事件不能重新开启已取消的温度。
- 起初协调红灯为15个行为断言失败及1个真实载荷目录移动错误；生命周期五个断言红灯、取消后 reset 的独立失败均保留。新增内部工厂入口先取得两项CS1729编译RED，不能当作全部行为RED。
- 正确选择旧无头夹具后，六项同场首次出现四个60秒超时：旧测试留下 WPF Application，设置测试构造时等待已不泵送的 Dispatcher。隔离探针证实根因；旧测试现在仅关闭自己创建的 Application，并泵送关闭回调。产品构造/语言逻辑未改，安全套件策略未改。
- Desktop 逐测试STA线程与共享 Application 不兼容，因此本地实窗证据来自独立单STA runner，外层仍有时间界限；不是 canonical 安全套件放宽超时。`de-DE` 未内嵌区域语言文件的测试宿主错误改为仓库实际支持的 `de`，新设置标签英文fallback得以验证。

### Task 5 源码交付与工具链说明

- 固定源码档案来自 `PawnIO@e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6`、独立 `PawnPP@e64e4c37b2d8ba0d8ee57205faf8183aee12c438`、`PawnIO.Modules@e12a858d952461ee2e919897cacbff7f905fe370`。`sensors/sources.lock.json` 保留真实下载长度及SHA；PawnIO tar不含gitlink内容，因此PawnPP必须单独交付。
- GPL/LGPL对应源码、模块编译器RPM输入、完整IOCTL linking exception和MPL文本均保留。例外不豁免经Pawn接口加载的模块。官方安装器外壳源码未公开，签名脚本/凭据也不可获得；只声明对应源码，不声称可逐字节重建签名安装器。
- 本地示例源码ZIP **1,098,220 B**、SHA `da6478410d8ce4d47e45e07e451d6ac932ad3860f4b65c355abf809375e14f74`，仅是开发检查产物；最终CI包哈希另行确认。生成过程把验证过的字节数组写入临时ZIP，成功后原子移动，未执行档案中的程序。
- 新专用NUnit副本固定CLR4，仅改生成的 `build/nunit` 配置，不改恢复包。临时脚本含中文路径时需显式UTF8加载；NUnit XML采用 `XmlDocument.Load()`按声明编码解析，不使用PS5默认文本编码。

### Task 5 新版实机口径（2026-10-20）

- 最新实窗使用生产Release **9bf97e580a38da69c2112abc57a2baebf011db4ad5a589a084257b559e65dca3**。外部VerificationApp宿主加载真实MainWindow/ResourceMonitor/TemperatureSampler/Controller及当前内嵌后端；外部宿主开销计入。不是直接生产App启动成本，也不是canonical CI二进制。
- 先在真实HKLM下捕获生产`SensorDriverTrust.Inspect`，再只对本宿主重定向HKLM到一次性HKCU键；捕获的原对象传给真实父控制器，子后端独立检查真实系统。真实共享驱动状态为 **AccessDenied / servicePresent=true / cpuAllowed=false**，不能报告未安装；没有执行安装器。
- 固定LHM通用NVIDIA RTX4080 Laptop **GPU Core 53–68°C**（62条Available记录）；ASUS CPU备用 **65–92°C**（104条选中Available记录）。NVAPI只在通用故障/准备期间独立备用。通用CPU、AMD硬件、Intel GPU通用路径没有实物通过证据。
- 1/2/5秒各观察5.029/8.025/17.029秒，序号前进5/4/4次；保持同一PID且未重叠。关闭温度、隐藏且无条、设置页、最小化的子进程退出测量分别 **14.2021 / 9.5551 / 6.6450 / 10.7641 ms**，均从需求变更分派计时。恢复及两个重新创建窗口会话均成功。
- 后端故障仅对已核实PID+创建时间+scratch EXE+Controller/Job的自有身份执行挂起或终止；过期/EOF撤销通用值，ASUS/NVAPI可用时显示实际备用并注明通用故障。最终自有子进程0、一次性注册项及scratch载荷已移除、真实HKLM设置指纹未变。新截图已生成但本轮人工视觉检查仍待执行。

### Task 5 本地性能与独立重算（2026-10-20）

测量生产EXE SHA-256为 `0a45a1e9e78afa8864f76d455bdc43bff05bf0878b59cf76c43c5db1dab690b4`，工具源码为 `b8b3bb32023786a416d9c7846211302c1e0c8134d5a2b3506a24ad98f8d9bd60`。这是界面最终修复之前的本地开发构建，**不是最终canonical发布EXE**。UTC 2026-10-20 14:22:41至14:38:02，2秒温度需求；外部真实窗口宿主开销计入主进程，不代表生产App直接启动成本。

| 轮次（预热/实测秒） | 主/后端CPU中位数 | 合计CPU中位数 | 主/后端私有内存中位数 MiB | 合计私有内存中位数 MiB | 合计增长 MiB | UI调度p95 ms |
| --- | --- | --- | --- | --- | --- | --- |
| 1（30.260/60.543） | 0.048476% / 0% | 0.095994% | 71.035 / 44.113 | 115.184 | 0.656 | 0.5846 |
| 2（30.285/60.530） | 0.048461% / 0% | 0.096521% | 72.656 / 44.236 | 116.904 | 1.090 | 0.5908 |
| 3（30.305/60.486） | 0.048505% / 0.023857% | 0.096461% | 72.039 / 43.953 | 116.076 | 1.863 | 0.7428 |
| 留存（30.280/604.893） | 0.048343% / 0% | 0.048673% | 72.207 / 47.422 | 119.535 | 5.266 | 0.8464 |

CPU按进程CPU时间增量/实际单调时长/32逻辑处理器归一化，合计先逐样本相加再取中位数。主/后端各自中位数不能直接相加代替合计中位数；内存增长从预热最后样本到测量最后样本计算。独立Python审计逐条重算 **780个测量样本**、实际时长、CPU/内存、最近秩p95、固定PID/创建时间/执行路径、所有检查及证据文件SHA；三短轮各13项、留存14项通过。三轮合计CPU均<1%、所有UIp95≤50ms、十分钟增长5.266MiB≤10MiB；四轮均无自有子进程残留、一次性注册项与scratch载荷均移除，安装器均未执行。

## 正式发布与最终资产验证（2026-10-21）

- **发布身份**：`v3.0.8.4-community.4`，[Release](https://github.com/shabhui/WinMemoryCleaner-Community/releases/tag/v3.0.8.4-community.4)，`draft=false`、`prerelease=false`，发布 UTC `2026-10-21T08:15:20Z`；标签引用为 commit `9e4876dc56cb43959e29226bff8ae9227870ca6e`，与 `target_commitish` 一致。正式发布工作流 [39567637435](https://github.com/shabhui/WinMemoryCleaner-Community/actions/runs/39567637435)（`workflow_dispatch`）结论 `success`；同提交的 CI 39567029870 与无发布产物门禁 39567068285 亦通过。
- **同次构建测试**：主套件 **554/554，14.106 s**、后端 **36/36，0.869 s**，`errors/failures/not-run/skipped/invalid/inconclusive` 全为 0；对应源码行为测试 **2/2**（3 份固定档案、10 条目，无安装器、未打开硬件）。生产、测试、打包三份载荷报告字节完全一致（SHA-256 `14c3d7dcc8f754cdf5163b98738cdc7c0a946b04c9aee0896e70a0aef9b41092`）：主/后端 `3.0.8.4`、24 文件、25 资源、14 源码指纹、9 分析器，反射式验证未运行 EXE。
- **七项正式资产**（每项均核对 GitHub 公布的大小与 `sha256:` 摘要，并已完整下载到会话 scratch；此前中断的部分下载保留为证据）：

| 资产 | 字节 | SHA-256 |
| --- | ---: | --- |
| `WinMemoryCleaner.Community.exe` | 4,957,184 | `a2ca707d438ebd7fb7165cb2716231e2569f1aeda7459af8599e97f29a122780` |
| `WinMemoryCleaner.Community.exe.sha256` | 98 | `d97f8ff7ea5d3b28b517bb995ff76e1e972ec6d856874963e4f37b1d0f8efc19` |
| `AssemblyInfo.txt` | 40 | `893d3b1dca63e809ea83f5e474988dd9d4791998693f8e9aaf59399fd668bf4e` |
| `WinMemoryCleaner.Community-3.0.8.4.zip` | 4,734,768 | `e94ec744c2bb267049d5ba2c84bbe472f44e7a756f13081ac95c7b4bc6b0ccb6` |
| `WinMemoryCleaner.Community-3.0.8.4-sensor-sources.zip` | 1,098,239 | `25d33808b4b261e8e6b4138c8330b5b4940203f3717e40809c5dd4dd5b4ee071` |
| `...-sensor-sources.zip.sha256` | 121 | `601f5c138c46f958f1e06551f05170eab061b3b6ffe2e6d4f57507cd5e75c036` |
| `SENSOR-THIRD-PARTY-NOTICES.md` | 4,670 | `6785d72d3b76809a0b172c4289d0c8e161072e04960783bf75240d15bc55cc43` |

- **内容一致性**：EXE 与源码 sidecar 均与各自资产匹配；便携 ZIP 18 个文件、17 项校验全覆盖且与独立 EXE 字节相同，6 份许可与依赖锁逐字节一致；源码 ZIP 的 3 份固定档案与 4 份许可匹配 `sources.lock.json` / `dependencies.lock.json`，`README.md`/通知与仓库文本一致；`AssemblyInfo.txt` 公告 `AssemblyVersion("3.0.8.4")`。
- **更新通道实测**：`GET /releases/latest` 返回同一稳定版本；经公共 HTTPS（命令级地址覆盖，未改系统 DNS/Git 配置/TLS 校验）下载 `releases/latest/download/WinMemoryCleaner.Community.exe`、`.exe.sha256` 与 `AssemblyInfo.txt`，重定向到 `v3.0.8.4-community.4` 后字节与上表完全一致——这是更新通道实际字节，而非仅元数据推断。
- **最终 EXE 载荷**：反射式提取并逐文件比对正式 `packaged-payload.json`，manifest `053a5e9be337b5f3162a5de46f1ce6138c2c0e32340e562b5eed391b609f96d0`；随后在按发布提交 detached、命令级 `core.autocrlf=true` 的 canonical 源码工作树上运行仓库验证器，**24 文件 / 25 资源 / 14 源码指纹 / 9 分析器** 全部通过（exit 0），未执行 EXE、后端或安装器。
- **最终发布 EXE 隔离功能回归**：`published-final-functional`，UTC `2026-10-21T14:26:51`–`14:28:32`，宿主退出 0；绑定记录 `productCanonicalRelease=true`、标签 `v3.0.8.4-community.4`、加载字节 SHA-256 与上表 EXE 相同。结果 **76 项检查：75 通过 / 0 失败 / 1 不可验证**（通用 CPU），UI 调度 p95 **0.5012 ms**，最多 1 个自有后端，8 个自有子进程全部 `alive=false`、`remainingOwnedChildren=0`，一次性注册表覆盖与 scratch 载荷均移除，绑定日志 0 B，未使用用户缓存、未执行安装器、未触碰受保护 PID。
- **本轮真实硬件读数（仍限本机）**：固定 LHM 通用 **RTX 4080 Laptop GPU Core 85–89 °C** 多次可用；后端通用 CPU 保持 `Disabled`（`driver=AccessDenied`，`servicePresent=true`，`cpuAllowed=false`），选中 CPU 走已安装 ASUS `0x00120094` 备用（故障注入后实测 80 °C）。通用 CPU 的可信驱动路径必须标为未验证，不能写作“未安装 PawnIO”。
- 宿主 `summary.json` 中 `canonicalReleaseEvidence` 是该工具固定的静态字段（工具本身不读取发布审计），发布谱系以同目录 `build-identity.json` 的 `productCanonicalRelease`、`canonicalReleaseTag` 与 EXE SHA-256 三重一致为准。

## 保护环境复核

- 用户原 EXE `D:\下载\WinMemoryCleaner.Community.exe` SHA-256 与基线一致：`E13B383CF807D379B1B18878CA411A2CD4A2131CD28C9A6815F205CC076B4114`；未覆盖。
- 原 PID 23072 在记录到的系统重启（UTC `2026-10-19T22:05:24.5Z`）后已不存在，不能声称仍是同一存活实例；当前用户进程不结束。
- 原开机任务仅 `RegistrationInfo/Date` 从 `2026-10-17T12:33:41` 变成 `2026-10-20T06:06:45`；其他 XML 字段一致。不恢复或改写用户任务。
- HKLM 32/64 位清理器设置、共享 PawnIO 注册项与驱动字节仍匹配原基线；共享驱动 SHA-256：`E19F274B9F3C917445BFF19A80BBE2EE34A54995E62BE50073122B20D86AF900`。
- Task 4 最终安全/实窗测试后无 session scratch 可执行进程（只读检查0）；当前保护用户PID14816，创建UTC `2026-10-19T22:06:40.5181131Z`，未结束。失败构建 staging 已清理；原任务raw XML检查仍仅报告上述注册时间差异，不能称其完全相同。

## 边界与未验证项

1. **未授权/未验证的硬件路径**：实际驱动安装仍未获授权（所有确认测试都取消，未执行官方安装器）。因此通用 CPU 的受信 PawnIO 路径、AMD CPU、AMD GPU、Intel GPU 均无实物通过证据；通用 Intel GPU 分组还受固定 LHM 与 CPU 分组耦合的门控限制。本轮只实测了 NVIDIA RTX 4080 的通用 GPU 核心温度与 ASUS CPU 备用。
2. **性能口径**：780 样本三轮短测与十分钟留存属于本地开发构建 `0a45a1e9…690b4`，不重跑、不冒充发布版数字；发布版仅记录 UI 调度 p95 0.5012 ms 与生命周期/清理结果，未重复整轮性能测量。
3. **人工视觉检查**：本会话 Browser 面板仍不可用，未做新的人工截图目检；最终功能轮次已生成截图文件与真实窗口几何/样式断言（横向 316×26、竖向 118×78 DIP，确认框 588.67/598 DIP），但不宣称替代人工目检。
4. **历史证据等级**：`final-functional-3`（75/0/1）针对审查前本地生产构建，仅作历史记录；审查修复后的 canonical 结果即上表 554/36 与发布运行。全部失败/中断轮次（`final-functional`、`final-functional-2`、早期 canonical 失败与两次被取消的诊断运行）均保留，不计为通过。
5. **受保护环境**：用户 EXE `D:\下载\WinMemoryCleaner.Community.exe` 未改动；PID 23072 是系统重启后被复用的编号，不是原清理器实例；开机任务仅有已记录的注册时间差异，其余字段一致，未改写。
