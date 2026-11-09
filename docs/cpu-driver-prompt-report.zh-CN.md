# CPU 温度驱动支持与开启提示：p3 历史验证摘要

> 这是既有 `cpu-driver-prompt-01`、最终 attempt **05** 的去标识化记录，不是本次文档整理新跑的验证。显示版本 **3.0.8**，主程序/后端程序集 **3.0.8.4**，载荷 **`lhm-0.9.6-p3`**；当时为未签名、未提交/推送或正式发布的本地候选。以下 `build/...` 路径均相对仓库根，仅为本地留存证据，**不随发布公开**，原始日志、截图及环境信息不得直接上传。

## 结论与边界

- 测试环境原已有官方 PawnIO **2.2.0**，并非没有驱动。旧信任表仅接受已知 2.1.0；本轮增加 2.2.0 的精确版本/哈希配对，未重装、降级、停止服务或修改 ACL/Windows 安全设置。
- 显式开启 CPU 温度或包含 CPU 温度的悬浮显示时异步检查驱动；只有 `NotInstalled` 且服务不存在才询问安装。`AccessDenied`、`DriverNotReady`、未知/检查失败分别说明，不授权覆盖安装；Ready 不重复提示。
- 正常 UAC 授权的 x64 探测器加载最终生产 Release，实际获得 CPU Package **79–83°C**。直接后端样本 **79、79、79°C**，完整 `ResourceMonitor.Sample(false,true)` 样本 **83、81、80°C**；来源为 LibreHardwareMonitor 的 CPU Package，不使用 ACPI 区域或虚假 0°C。
- 最终回归 **811 项通过**，必需运行文件较同工具链基线增长 **964,608 B**，满足 +1 MiB 门槛。但 **p3 完整正式性能、十分钟稳定性及完整生产 GUI 冷启动尚未验证**；本报告不等于全部发布验收通过。

## 用户确认与安全边界

触发绑定用户 `Click`，不绑定启动、周期刷新或属性回填。重复点击合并；异步回调和确认后均重新核对窗口仍可见、未最小化、处于设置页且选项仍开启。离页、取消勾选、隐藏、关闭或释放使迟到回调失效。温度模态提示仅在自身路径临时监听 owner `Closing`，按取消结束并在 `finally` 解绑，未扩展修改其他历史对话框。

实现：[窗口交互](../src/View/Window/MainWindow.xaml.cs)、[状态分类](../src/Service/Monitoring/Sensors/SensorDriverNotice.cs)、[精确信任](../shared/Sensors/SensorDriverTrust.cs)、[安装包准备](../src/Service/Monitoring/Sensors/SensorInstallerPackage.cs)。

安装器仅在明确确认后准备。离线包仍走相同校验，无效包不自动联网；取消选择直接返回。下载只允许固定 HTTPS 初始地址及官方资产域名，最多 **3** 次受控跳转，单请求/读取 **15 秒**、总下载 **60 秒**。固定长度、SHA-256、Authenticode、签名者、路径保护和只读租约全部保留。准备可取消，官方交互安装器启动后不强杀；不静默安装、不自动重启、不覆盖/降级/卸载共享驱动、不关闭系统防护。

## 公开驱动来源

| 项目 | 固定值 |
|---|---|
| 官方来源 | [PawnIO.Setup 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)，asset **374375077** |
| 2.2.0 AMD64 驱动 | **136,504 B**；签名有效，Microsoft Windows Hardware Compatibility Publisher |
| 驱动 SHA-256 | `fca6e7d58b0cf38dbb913a2b9e532f48629145d395f454b16a9f58e97b8d3940` |
| 2.2.0 安装器 | **3,410,960 B** |
| 安装器 SHA-256 | `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032` |
| 安装器签名者指纹 | `F380DCC9F706E2756A5047B832FFE719E1BC35F5` |

当时只在临时目录验证官方安装器签名、提取 `RCDATA/105/1033` 中 Zstandard CAB 的 AMD64 字节，并与已安装驱动匹配，**未执行安装器或包内工具**。匿名 API 限流后通过既有官方 API 客户端读取同一来源，未降低校验。已知 2.1.0 / 2.2.0 都须精确配对版本、哈希、签名及设备状态，不放行任意更新版本。前后驱动身份、启动类型与服务状态一致。

本地证据：`build/cpu-driver-prompt-01/finalization-01/official-evidence/`、`build/cpu-driver-prompt-01/installed-driver-after.json`。

## 最终构建与回归

| 套件 | 通过数 | 本地证据（相对 `build/cpu-driver-prompt-01/`） |
|---|---:|---|
| 主安全回归 | 711 | `regression/safe-05/safe-tests.xml` |
| `MonitoringWindowTests` | 61 | `regression/window-05/results.xml` |
| `NotificationDispatchTests` | 3 | `regression/dispatch-05/results.xml` |
| 后端安全回归 | 36 | `regression/backend-05/results.xml` |
| **合计** | **811** | `finalization-01/regression-summary.json` |

811 个不同用例与 XML 计数一致，`errors/failures/not-run/ignored/skipped/invalid/inconclusive` 均为 **0**；窗口绑定日志为 **0 字节**。安装/进程交互使用受控状态和假执行器，没有操作真实驱动或用户进程。覆盖启用/取消/重复点击、Ready/权限/未知/null/异常、非用户操作、迟到回调、确认后复核、owner 关闭，以及 zh-Hans/en/de 文案和最小窗口。

Debug / Release / Test 及基线构建通过；主程序保持 net40 / C# 4，独立后端要求 Windows 10/11 x64 + net472 或更新 4.x。Roslyn **4.8.0** 分析器记录均包含完整 **9** 个分析器；该记录与本地 Framework 工具链 EXE 分开，**不声称完成完整 Visual Studio/CI 构建**。

最终 EXE ReflectionOnlyLoad 核验为 **22 个载荷文件、23 个资源、14 个后端源码哈希、9 个分析器记录**；22 个 GZip 长度/哈希通过。manifest SHA-256 `116f12dd829917f3e390b65071af6d95ccabc43af10431b4cfb217c28e485856`。本地证据：`release-payload-verification.json`、`finalization-01/build-summary.json`、`analyzers-05/`。

真实温度链路从 Preparing/WarmingUp 转到 Running/Ready，前后均为 2.2.0、Ready、CpuAllowed=true。探测器正常退出 **0**，直接后端释放后按已记录身份确认不存在；完整 ResourceMonitor 已释放，但没有全后代逐进程计量，**不能据此声称全后代性能/清理验收通过**。未请求进程列表、全局清理或进程动作。证据留在 `cpu-elevated-01/` 和 `finalization-01/driver-and-cpu-verification.json`；这是温度链路验证，不是完整 GUI 启动/迁移/单实例/展示验收。

## 体积与历史产物身份

| 项目 | 字节 |
|---|---:|
| 同工具链基线 `da2cf8b` | 434,176 |
| 最终候选必需运行文件（仅 EXE） | 1,398,784 |
| 增长 / 上限 / 余量 | **964,608（942 KiB） / 1,048,576 / 83,968** |
| 便携 ZIP | 1,075,718 |
| 对应源码及固定构建输入 ZIP | 2,519,117 |

- EXE SHA-256：`031deddf875664cdd6b50bf3c909e1415b9a1b72ceaa9c22318a454075bd23d6`。
- 便携 ZIP SHA-256：`82b11c40ad89ad0cbc33ed26a74edd5df62a0790e864f140a208c7d7123af485`。
- 源码 ZIP SHA-256：`a54cc9770149dc02504675cc27771f4238294c549f536fb591a12109112e7688`。

后端与依赖仍内嵌，使用时校验并释放，不靠外置必需 DLL 满足门槛；驱动/安装器不打入默认便携包。冻结 **270 个文件、2,163,874 B**；当时 HEAD 为 `e476d2d55c577c10d78b70a3551ef2e0d0c24249`，保留未提交改动与失败记录。ZIP 逐项复读、CRC、内部清单及外部哈希核验保留于 `finalization-01/package-verification.json`、`delivery-summary.json`、`source-state.json`。

保留的三个源码档案为 **PawnIO 2.1.0、PawnPP、PawnIO.Modules 0.2.2**，不是 2.2.0 驱动源码包；[2.2.0 精确源码](https://github.com/namazso/PawnIO/tree/5cdf470831fdfff3f7f1d06363ca6b230f3bf35a)仅提供公开链接，不能当作已打包源码。官方安装器外壳源码/签名凭据未提供，IOCTL 例外不豁免 Pawn 模块。文件冻结/封存清单针对当时字节；本次脱敏后的 Markdown 不再与历史报告哈希相同，不回写旧清单冒充原件。

## 失败与未覆盖项（保留）

- 早期旧按钮文案预期造成回归失败；窗口/关闭诊断超时揭示温度模态关闭缺陷；两项悬浮用例补齐先开启 CPU 温度的正确前置条件。
- attempt **04** 的 811 项虽通过，随后 **CA2234 分析器失败**；改用 `Uri` 重载后完整执行 attempt **05**。未增加用例超时、删用例、抑制规则或放宽安全校验。超时包装器仅回收自建 Job 内进程。
- 一次限定只读复核无新增安全阻断；其他历史 `ShowDialog` 的 owner 关闭问题未另行实机复现。盘上 2.2.0 与运行中 2.1.0 不一致时仍保守返回 `UnverifiedDriver`，不是“需重启”提示，并禁止 CPU 访问/重复安装。
- 工作区曾临时不可访问；恢复后核对 EXE 与两个 ZIP 长度/哈希未变，没有重打包覆盖产物。
- **未重跑**正式基线三轮对照（预热 **30 秒**、测量 **60 秒**、第二轮反序）、全后代 Job CPU/私有内存、**≥200** 进程/P95、连续 **10 分钟**结束与峰值增长检查。旧 p2、memory-demand-02 不能移作 p3 通过；独占全屏、受保护桌面、其他 DPI/显示器、反作弊、其他硬件及完整 VS/CI/签名发布仍未覆盖。
