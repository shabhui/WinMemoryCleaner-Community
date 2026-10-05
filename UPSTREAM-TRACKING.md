# 上游 Issue / PR 跟踪（UPSTREAM-TRACKING）

本文件记录社区分支 `community` 对上游 [IgorMundstein/WinMemoryCleaner](https://github.com/IgorMundstein/WinMemoryCleaner) 开放条目的处置状态与依据。
数据来自一次本地整理的 GitHub REST API 快照（issues / pull requests / releases）以及本地 git 历史。

> **阅读前提**：本文件**不声称**任何已挂起问题被"完全解决"。除状态写作**接纳**的条目外，其余条目只是被部分覆盖、被答复、需澄清或需复现。表内"覆盖"仅指某条已知路径被改动覆盖，不等于整条 Issue 关闭。

---

## 1. 基线与快照范围

| 项 | 事实 |
|:---|:---|
| 上游仓库 | <https://github.com/IgorMundstein/WinMemoryCleaner>（GPL-3.0；快照时 `archived=false`，未归档） |
| 上游 `main` | `a19f25719c9b9a58a0acea99cb8adb4737d2d318`（提交时间 2025-12-19；仓库 `pushed_at` 2025-12-19T23:20:22Z） |
| 上游最新正式 release | `3.0.8`（published 2025-12-13T23:46:59Z）。快照中未见 3.0.9 正式发布 |
| 快照时上游开放条目数 | 34（22 个 Issue + 12 个 PR），与本文条目数一致 |
| 社区分支 | `community`，基线 = PR #204 的 head `026771dbc81bd34a6f63738920ee6f3142d7f6e5`（用户全部已有修复） |
| 社区分支现状 | 基线之上 `cherry-pick -x` 接纳 7 个上游 PR（#205 / #206 / #207 / #208 / #209 / #188 / #186），共 11 个提交，cherry-pick 序列结束于 `87adb02320fe7de0112776b74bd3ae443ed317f7`；其后为社区自有改动（独立身份与禁用更新通道、编译期测试隔离、#174 / #179 修复、#195 两处 GCHandle 挑选修复、Serbian Latin 补齐、文档与本地构建脚本），按主题分为多个本地提交；仅存在于本地，无远端跟踪分支，尚未发布 |
| 快照时间与局限 | 本地整理快照，约 **2026-10-05**（API 响应中仓库 `updated_at` = 2026-10-04T23:08:00Z，资料文件时间戳为 2026-10-05）。快照**只涵盖收集时仍处于 open 状态的条目**，不是上游全部历史：已关闭的 Issue、已合并/已关闭的 PR、更早的讨论均不在范围内 |

**关于 PR 合并状态的提醒**：快照中的 12 个 PR 全部 `state=open`、`merged=false`、`merged_at` 为空。API 响应里出现的 `merge_commit_sha` 是 GitHub 计算的**候选**合并提交，不能作为"已合并"的证据。本文所有"接纳"均指**本社区分支采纳其代码**，与上游是否合并无关。

---

## 2. 状态图例

| 状态 | 含义 |
|:---|:---|
| **接纳** | 改动已在本社区分支落地；括注区分来源：上游 PR 的 cherry-pick / 社区自研补丁 / 仅工作区已改未测试 |
| **重复覆盖** | 该条目的部分内容已被其它改动或已有答复覆盖；括注给出覆盖范围，**不表示整条问题解决** |
| **暂缓** | 当前不处理、未排期，或按决策明确不修 |
| **待复现** | 现有证据不足以定位，需日志/环境复现后再决策 |

---

## 3. Issue 状态总表（22 项）

| Issue | 原作者 | 原链接 | 内容摘要 | 状态 | 依据 / 说明 |
|:---|:---|:---|:---|:---|:---|
| #109 | racuna | [issues/109](https://github.com/IgorMundstein/WinMemoryCleaner/issues/109) | 功能请求：指定进程启动时触发优化（反向排除） | 暂缓 | 维护者称可实现，但需先设计参数与启动延迟（评论 3118444407）；未排期 |
| #131 | Jef-Z | [issues/131](https://github.com/IgorMundstein/WinMemoryCleaner/issues/131) | 定时清理 Working Set 导致 Chrome / VSCode 卡顿甚至崩溃 | 暂缓 | 属最激进清理功能的已知行为；维护者建议排除 Working Set，并提出 safe mode / 前台进程排除设想（3239314085、3247285854） |
| #146 | concept1961 | [issues/146](https://github.com/IgorMundstein/WinMemoryCleaner/issues/146) | 功能请求：主窗口位置保存 / 指定 | 暂缓 | 维护者称可实现保存并恢复窗口位置（3494407370）；已接纳的 #209 只做"展开窗口可调整大小"，**不含**位置持久化 |
| #170 | kwanice | [issues/170](https://github.com/IgorMundstein/WinMemoryCleaner/issues/170) | 高 DPI 下托盘内存百分比数字模糊 / 像素化 | 暂缓（明确不修） | 维护者重开并给出根因：16×16 图标被 Windows 拉伸、关闭抗锯齿后像素化，需多尺寸处理（3720405917）；本分支按决策不修 |
| #174 | gycsisz | [issues/174](https://github.com/IgorMundstein/WinMemoryCleaner/issues/174) | Windows 10 LTSC 2019 每次登录报 "The requested security protocol is not supported" | 接纳（社区补丁；已随测试版编译并通过安全测试，LTSC 2019 实机未测） | main 仍为 `Settings.AutoUpdate` 与间隔的早退条件（AutoUpdate=false 时反而继续检查），且 SecurityProtocol 仍 OR 上 TLS 1.3 标志；社区分支改为「不受支持 / AutoUpdate 关闭 / 间隔未到 → 直接返回」并只保留 TLS 1.2，默认 AutoUpdate=false。上游维护者已在 **3.0.9 beta** 中修复并由报告者验证通过（3675508704），但 3.0.8 / main 未见该修复 |
| #177 | ohault | [issues/177](https://github.com/IgorMundstein/WinMemoryCleaner/issues/177) | 功能请求：分页 / 虚拟内存可视化 | 暂缓 | 维护者：此类内存分析开销大，若要实现需置于 feature flag 之后（3704106904） |
| #179 | guimlherme | [issues/179](https://github.com/IgorMundstein/WinMemoryCleaner/issues/179) | 计划任务 XML 头声明 UTF-16，写入却用默认编码，含重音路径出现乱码 | 接纳（社区补丁；XML 构建与编码已有单元测试覆盖，真实 schtasks 注册未实测） | 维护者确认根因并承诺显式传 `Encoding.Unicode`（3704090009）；社区分支新增 `StartupTask.CreateXml(...)`（含 XML 转义、非 ASCII 路径/用户名、不变文化日期）与 `App.WriteStartupTaskXml(...)`（`Encoding.Unicode`），临时 XML 在 finally 中删除 |
| #182 | anderlli0053 | [issues/182](https://github.com/IgorMundstein/WinMemoryCleaner/issues/182) | 附 `Slovenian.json` 请求加入斯洛文尼亚语 | 重复覆盖 | 上游仓库已包含 `Slovenian.json`，README 署名 Jadran Rudec（JadranR），文件来自更早的历史提交；本 Issue 的附件未被采用 |
| #183 | mrxxx66 | [issues/183](https://github.com/IgorMundstein/WinMemoryCleaner/issues/183) | 间歇冻结，GUI 无响应，无法从托盘退出 | 重复覆盖（**仅部分**） | #204 覆盖其中一条已定位的锁序（AB-BA）死锁路径；维护者另指旧框架多任务处理弱等其它因素，且 #204 正文明确自述不解释该类线程中的全部报告。**不能宣称已解决** |
| #185 | IceMetalPunk | [issues/185](https://github.com/IgorMundstein/WinMemoryCleaner/issues/185) | 命令行参数不生效：只进托盘、不优化、不退出的报告行为 | 待复现 | 静态阅读未发现必然的解析失败点（报告所用参数均为合法枚举成员，解析路径统一走 `Enum.TryParse`）；维护者称"本应工作，可能有坏代码"（4432852046）。需带日志复现 |
| #189 | hl2guide | [issues/189](https://github.com/IgorMundstein/WinMemoryCleaner/issues/189) | 请求显示上次优化状态 / 历史 | 重复覆盖（**仅部分**） | 托盘通知已存在；维护者说明应用没有自动分析逻辑、释放量只是估算，应用内日志可视化仍是待办（4432831450） |
| #190 | QuidditySolutions | [issues/190](https://github.com/IgorMundstein/WinMemoryCleaner/issues/190) | 频繁挂起 | 重复覆盖（**仅部分**） | 与 #183 同族，只被 #204 的一条死锁路径覆盖；维护者称会调查（4432804765），`/reset` 对部分用户有效但仍有人复现。**不能宣称已解决** |
| #191 | Shivamvrewa | [issues/191](https://github.com/IgorMundstein/WinMemoryCleaner/issues/191) | 询问如何发布 / 构建应用 | 重复覆盖 | 维护者已答复：项目没有发布或 CI/CD 流程，用 Visual Studio Community 2022 克隆后构建（4432774179） |
| #192 | stderr-to-devnull | [issues/192](https://github.com/IgorMundstein/WinMemoryCleaner/issues/192) | 高度不稳定：冻结、优化后僵死、托盘图标消失 | 重复覆盖（**仅部分**） | 维护者归因旧框架对多任务处理不佳与现代 Windows 内存压缩，提到可能升级框架并放弃 XP/2003（4432756306、4463995413）；#204 只覆盖一条死锁路径。**不能宣称已解决** |
| #193 | abrar783 | [issues/193](https://github.com/IgorMundstein/WinMemoryCleaner/issues/193) | 启动后托盘图标不出现（由计划任务触发时正常） | 待复现 | 维护者称会看（4432726487）；报告缺少日志与环境细节 |
| #194 | infowehsner-cyber | [issues/194](https://github.com/IgorMundstein/WinMemoryCleaner/issues/194) | SBS2003 / Server 2003 启动即 WPF 图像解码失败，HRESULT 0x88982F60 | 待复现 | 维护者推测某次更新破坏了该平台兼容性并称会修（4675538166）；需在旧系统上实测 |
| #196 | welovfree | [issues/196](https://github.com/IgorMundstein/WinMemoryCleaner/issues/196) | 功能请求：托盘内存百分比文字字号可调 | 暂缓 | 未排期。#204 只重排了图标渲染的并发（渲染锁），并未加入字号设置 |
| #197 | mangoshaper | [issues/197](https://github.com/IgorMundstein/WinMemoryCleaner/issues/197) | 卸载后每次登录弹出 `%TEMP%\WinMemoryCleaner.exe.3.0.8.0.new` 的"选择打开方式"窗口 | 待复现 | 未定位到卸载清理路径；上游历史提交信息称在 #168 中加入了删除孤儿旧更新文件的逻辑，但**是否覆盖卸载场景未证实**，需复现 |
| #198 | WeberRess | [issues/198](https://github.com/IgorMundstein/WinMemoryCleaner/issues/198) | 事件日志不可机器读取：Event ID 恒为 0、payload 本地化、JSON 多行 | 暂缓（明确不修） | 代码事实成立：`EventLogTraceListener` + `Trace.TraceInformation` 传 ID 0、序列化默认多行、时长按当前 Culture 格式化；本分支按决策不修 |
| #199 | threethreenine | [issues/199](https://github.com/IgorMundstein/WinMemoryCleaner/issues/199) | 询问能否清理虚拟内存 | 暂缓 | 与 #200 同源，需先澄清口径再给设计答复；该 Issue 无评论 |
| #200 | jiyexing | [issues/200](https://github.com/IgorMundstein/WinMemoryCleaner/issues/200) | 自动优化在"占用未到阈值"时触发；虚拟内存数字与任务管理器不一致 | 暂缓（待澄清） | 触发条件按已文档化语义（空闲物理内存低于设定值即触发）；虚拟内存取自 `MEMORYSTATUSEX` 的提交口径，通知数字取释放前后差值，属**语义 / 文案**问题，**未证实为计算缺陷** |
| #211 | Jojo2507 | [issues/211](https://github.com/IgorMundstein/WinMemoryCleaner/issues/211) | 优化流程卡死，进程成为僵尸 | 待复现 | 0 评论、无日志；与 #183 / #190 / #192 同族，无法定位 |

**合计：22 个 Issue。** 其中状态为“接纳”的只有 #174、#179（均为社区自研补丁，已随测试版编译并通过安全测试套件；真实计划任务注册与旧系统实机行为未实测）；其余条目没有被宣称修复。

---

## 4. PR 状态总表（12 项）

| PR | 原作者 | 原链接 | 上游 head SHA（完整） | 内容摘要 | 处置状态 |
|:---|:---|:---|:---|:---|:---|
| #184 | ricred | [pull/184](https://github.com/IgorMundstein/WinMemoryCleaner/pull/184) | `622ff16f817b022c77b0a5a7995cd365a2a95286` | GUI 响应性与窗口状态：`RaisePropertyChanged` 跨线程 marshal、`Thread.Sleep`→`DispatcherTimer`、窗口恢复重构、删除临时 `Topmost` 赋值 | **不整包接纳**：DispatcherTimer 部分与 #204 重复；阻塞式 `Dispatcher.Invoke` 与删除 `Topmost` 未采纳（无复现证据，需 Windows 实测） |
| #186 | rammba | [pull/186](https://github.com/IgorMundstein/WinMemoryCleaner/pull/186) | `068f9781c254c797c77414430db35b61c315555f` | 塞尔维亚语双语支持：新增 Serbian (Latin)、原 Serbian 更名 (Cyrillic)、README 贡献者署名更新 | **接纳**（`cherry-pick -x`，4 个提交） |
| #188 | VenusGirl | [pull/188](https://github.com/IgorMundstein/WinMemoryCleaner/pull/188) | `03c1301f2a0e1d075589d8826f4f53575dd49b89` | 韩语本地化更新与字符串修正 | **接纳**（`cherry-pick -x`，2 个提交） |
| #195 | Y-ASLant | [pull/195](https://github.com/IgorMundstein/WinMemoryCleaner/pull/195) | `4e5813c59c055b2fcf8346f85c59c09c31f9b215` | 性能 / 线程安全混合包（Localizer 缓存、GCHandle、图标字体缓存、Brushes 缓存、WinService Interlocked、Optimize 锁粒度等）；正文自述 Closes #168/#181/#183/#190/#192 | **部分接纳**：仅挑选 `ComputerService` 中 `OptimizeCombinedPageList` 与 `OptimizeSystemFileCache` 的两处 `GCHandle.Alloc(0)` 哑句柄初始化，改为 `default(GCHandle)`（源码内标注作者与 PR 号）；其余改动与 #204 设计冲突或收益未证实，未采纳 |
| #203 | iishanmakkar | [pull/203](https://github.com/IgorMundstein/WinMemoryCleaner/pull/203) | `1275e071758489d4560fdfd03756de14565c26a0` | .NET Framework 4.0 → .NET 8 重写（44 个文件，+1746 / −6353） | **暂缓**：决策保留 .NET Framework 4.0。另注意该 PR 正文把 issue 编号与主题错位列举（编号与主题不匹配），**不构成任何 issue 已修复的证据** |
| #204 | shabhui | [pull/204](https://github.com/IgorMundstein/WinMemoryCleaner/pull/204) | `026771dbc81bd34a6f63738920ee6f3142d7f6e5` | 托盘通知死锁（AB-BA）与监控线程忙等修复（3 个提交） | **接纳（用户基线，本分支起点）**：社区全部已有修复以此为 HEAD。正文提及 #183/#190/#192，但明确不认为能解释所有报告 |
| #205 | MataM15 | [pull/205](https://github.com/IgorMundstein/WinMemoryCleaner/pull/205) | `16f20260e8375700a8bec09543285cb6a2a726d6` | 原生内存操作错误与权限错误语义修正（`RtlNtStatusToDosError`、`AdjustTokenPrivileges` 校验），附 7 个回归测试 | **接纳**（`cherry-pick -x`） |
| #206 | MataM15 | [pull/206](https://github.com/IgorMundstein/WinMemoryCleaner/pull/206) | `2d02579242c339f84ea854783e87f076fecf4e48` | 优化耗时计入最终内存释放阶段，附 10 个测试 | **接纳**（`cherry-pick -x`） |
| #207 | MataM15 | [pull/207](https://github.com/IgorMundstein/WinMemoryCleaner/pull/207) | `516907a2d772c4c567a16cc42e13f8516f24ec43` | 抽出 `AutoOptimizationPolicy`，GUI 与 Windows 服务共用自动优化触发策略，附 9 个边界测试 | **接纳**（`cherry-pick -x`） |
| #208 | MataM15 | [pull/208](https://github.com/IgorMundstein/WinMemoryCleaner/pull/208) | `df18c58b3e7be4ece71c52a26c95f8ce454eb150` | 文案 / 文档：进程排除只影响 Working Set（30 种语言 + README），不改清理行为 | **接纳**（`cherry-pick -x`） |
| #209 | MataM15 | [pull/209](https://github.com/IgorMundstein/WinMemoryCleaner/pull/209) | `1ca4c458d1c74fb2abb645151be047e2fe850bb6` | 展开窗口可调整大小（缩放手柄、最小尺寸、会话内记忆尺寸），不含位置持久化 | **接纳**（`cherry-pick -x`；上游标记为 **draft**） |
| #212 | bajixx-19 | [pull/212](https://github.com/IgorMundstein/WinMemoryCleaner/pull/212) | `60d8821f8137fd9641492b2c2904b7349db5f01c` | NotificationService `Invoke`→`BeginInvoke`、MainWindow `Thread.Sleep`→`DispatcherTimer`、MonitorComputer 协作等待 | **不整包接纳**：4 处实质改动中 3 处与 #204 等价或更弱；独有部分（删除 `Keyboard.ClearFocus`）价值低且未验证 |

**合计：12 个 PR**，全部在上游仍未合并。

---

## 5. 已接纳改动的 SHA 对照（上游 head / 采纳源 SHA / 本地提交）

| 上游 PR | 上游 head SHA（完整） | 采纳的上游提交 SHA（完整） | 本地提交（`cherry-pick -x`，完整 SHA） | 本地提交标题 |
|:---|:---|:---|:---|:---|
| #205 | `16f20260e8375700a8bec09543285cb6a2a726d6` | `16f20260e8375700a8bec09543285cb6a2a726d6` | `85a85bb6673d00af80bed2951e478eccd3483cbd` | fix: report native memory and privilege errors accurately |
| #206 | `2d02579242c339f84ea854783e87f076fecf4e48` | `2d02579242c339f84ea854783e87f076fecf4e48` | `bb6f9ea878c2667392d6dfd4a7b0c3cfa51ae04e` | fix: include final memory release in optimization duration |
| #207 | `516907a2d772c4c567a16cc42e13f8516f24ec43` | `516907a2d772c4c567a16cc42e13f8516f24ec43` | `8072550cf1234d770b0998fb04aad0aefc84ed9d` | refactor: share automatic optimization trigger policy |
| #208 | `df18c58b3e7be4ece71c52a26c95f8ce454eb150` | `df18c58b3e7be4ece71c52a26c95f8ce454eb150` | `2312ba6127a99029c901519e6e554c35d5638b64` | docs: clarify working-set-only process exclusions |
| #209 | `1ca4c458d1c74fb2abb645151be047e2fe850bb6` | `1ca4c458d1c74fb2abb645151be047e2fe850bb6` | `121a823c94084aa866fda7b2722647e9f22cd63e` | fix: make the expanded main window resizable |
| #186 | `068f9781c254c797c77414430db35b61c315555f` | `ee1bb6648919538a13b6519ce5bea5f2a78bce34` | `0ce815e4bea33b7627007b8e0d2e4cb21b2a651d` | Update existing Serbian (Cyrillic) translation |
| #186 | （同上 head） | `44d9deebb370710448354b3e05c3c34ac4db7839` | `a3c65df7eff859d486d05dc1c764b4074e0571c9` | Add Serbian Latin translation |
| #186 | （同上 head） | `7da19102cd9220980602b6d906cb6b5b59ec6758` | `18beb497bb285ad559ec508001638be5c8c24867` | Rename existing Serbian to Serbian (Cyrillic) |
| #186 | （同上 head） | `068f9781c254c797c77414430db35b61c315555f` | `87adb02320fe7de0112776b74bd3ae443ed317f7` | Update README.md |
| #188 | `03c1301f2a0e1d075589d8826f4f53575dd49b89` | `06092234f5556568ed68a9eeb8addb7cf7984e8b` | `d243c57638a0108e2e981a3586b412d9d0627c99` | Update Korean |
| #188 | （同上 head） | `03c1301f2a0e1d075589d8826f4f53575dd49b89` | `c2d0e4f07a54292a63ac0abc1e7e0f696fe00a47` | Fix Korean localization strings |

基线本身：#204 的 head `026771dbc81bd34a6f63738920ee6f3142d7f6e5` 就是 `community` 分支的基线提交；本地分支上其前置提交为 `56a471b09c382c2773dfb934b0590cc3dd7cba2c`、`4f103f00754a3e6848564942089d324c587420fe`，三者共同构成 PR #204 的 3 个提交（本地 SHA）。

`cherry-pick -x` 接纳顺序（自基线起）：#205 → #206 → #207 → #208 → #209 → #188 → #186。

整合修复：#186（Serbian Latin）与 #208（新增本地化键）交叉时，`Serbian (Latin).json` 缺少 `ProcessExclusionListDescription` 且 `ProcessExclusionList` 未对齐 #208 的“工作集排除”措辞；社区分支按 #208 自带的 Serbian (Cyrillic) 文案转写补齐（保持双语一致），由本地化完整性测试覆盖。

---

## 6. 未接纳 PR 的结论摘要

| PR | 作者 | head SHA | 结论 |
|:---|:---|:---|:---|
| #184 | ricred | `622ff16f817b022c77b0a5a7995cd365a2a95286` | 不整包接纳。其 `Thread.Sleep`→`DispatcherTimer` 一处与 #204 逐处等价（重复）；改为**阻塞式** `Dispatcher.Invoke` 与删除 `Topmost` 激活技巧收益未证实、风险与控制目标相反（详见第 4 节） |
| #195 | Y-ASLant | `4e5813c59c055b2fcf8346f85c59c09c31f9b215` | 部分接纳。仅落地两处 `GCHandle.Alloc(0)` → `default(GCHandle)`（避免在真正 pin 缓冲前分配哑句柄），源码内保留作者与 PR 标注；其余（图标字体缓存、Brushes 缓存、Optimize 锁粒度、WinService Interlocked 等）未采纳。正文的 `Closes #183/#190/#192` 无依据，本文不作沿用 |
| #212 | bajixx-19 | `60d8821f8137fd9641492b2c2904b7349db5f01c` | 不整包接纳。NotificationService 三处改为 `BeginInvoke` 已被 #204 完整覆盖，MainWindow 与 MonitorComputer 两处是 #204 的子集或更弱实现；独有改动未验证 |
| #203 | iishanmakkar | `1275e071758489d4560fdfd03756de14565c26a0` | 暂缓。决策为保留 .NET Framework 4.0 运行时与 XP/2003–2025 的兼容目标，不做整包 .NET 8 迁移；其正文的 issue 编号与主题错位，不能作为"修复了某条 issue"的证据 |

---

## 7. 待验证 / 待澄清清单（当前不可对外宣称的部分）

1. **#179（任务 XML 编码）与 #174（更新检查守卫 + TLS 1.2）**：社区补丁已落地并随测试版编译；安全测试套件 305 项全部通过（含 XML 转义 / UTF-16 BOM / 更新策略间隔用例）。**未实测**部分：真实 schtasks 注册、Windows 10 LTSC 2019 实机登录行为。两者都不是来自已合并的上游 PR。
2. **#174 的语义变化**：`AutoUpdate=false` 从“仍会执行一次更新检查”改为“不检查”，且社区构建更新通道整体未配置（`Helper.IsAutoUpdateSupported` 恒为 false），属有意行为变更。
3. **#198（事件日志）与 #170（高 DPI 托盘）**：按决策**不修**；#198 的代码事实成立，但本分支不做 Event ID / 序列化 / 文化不变量的改造。
4. **#185（CLI 参数）**：需要带日志复现；在复现前不得把任何 diff 当作该问题的修复。
5. **#200 / #199（虚拟内存口径与触发语义）**：属口径与文案澄清问题，**未证实为计算缺陷**；不得表述为"修复了错误的触发计算"。
6. **#195**：仅采纳两处 GCHandle 初始化修复（标注来源 Y-ASLant / PR #195）；不得表述为整包采纳，其 `Closes #183/#190/#192` 也不成立。
7. **#203**：暂缓，保留 .NET Framework 4.0。
8. **挂起问题 #183 / #190 / #192 / #211**：仅被 #204 覆盖**一条已定位的死锁路径**；其余报告（例如线程中提到的 dispatcher "Not enough quota" 异常等）未定位，**不得宣称已修复或已关闭**。
9. **上游侧事实边界**：#174 的上游修复只存在于 3.0.9 **beta**（由报告者验证）；3.0.8 与 `main` 中未见。除此之外，不应声称任何开放 Issue 已被上游修复。

---

## 8. 复核命令

```powershell
# 基线之后的本地提交（含 cherry-pick -x 的来源行）
git log --format='%H %s%n%b' 026771dbc81bd34a6f63738920ee6f3142d7f6e5..HEAD

# 已接纳提交的改动范围
git show --stat 85a85bb bb6f9ea 8072550 2312ba6 121a823 d243c57 c2d0e4f 0ce815e a3c65df 18beb49 87adb02

# 上游 refs（含各 PR 的 head）
git for-each-ref refs/remotes/upstream
```

数据说明：本文依据的 GitHub API 快照（issues / PR / release）为一次性本地整理材料，未随仓库发布；快照只含收集时 open 的 34 项条目，因此**本文不代表上游全部历史**。
