# 通用 CPU/GPU 温度检测验收记录

> 进行中：Task 1–3 已完成本地实施与安全检查；Task 4 协调/设置接线、Task 5 canonical CI/性能/发布尚未完成。本文不能作为已发布新版或全部硬件实测通过的声明。

## 固定架构与安全边界

- 主程序 WPF / .NET Framework 4.0 / C# 4；独立后端 x64 / .NET Framework 4.7.2 / C# 4。
- 固定 LibreHardwareMonitorLib 0.9.6（`3d331e3370efb858411f19511373eff65a218701`），12 个运行时 DLL 的长度和 SHA-256 由 `sensors/dependencies.lock.json` 锁定。
- 所有组件、配置、许可证、锁文件和官方 PawnIO 2.1.0 安装器以逐文件 GZip 内嵌；无需 EXE 旁置后端目录。压缩资源合计 **4,392,443 B**，这不是最终发布主 EXE 的大小。
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
| 受控子进程 / Job | RED 18 后实现；19 个控制器测试覆盖有界帧/序号/UTC/过龄/退避/stderr/准备取消/刷新/Job失败/后台停止 |
| 后端完整分析器 | Roslyn **4.8.0-7.23558.1**，C#4、x64、固定 net472 references、`AllRules.ruleset`、9 个声明分析器、warnings-as-errors；产品及测试编译通过 |
| 后端完整安全测试 | **35/35，0.203679 s**，0 errors / 0 failures |
| 两后端 csproj 结构检查 | 本地 in-box MSBuild + Roslyn 编译器均 exit 0；测试链接 AssemblyInfo，版本 3.0.8.4；配置 1,840 B；此项不冒充 canonical VS 分析器构建 |
| 载荷构建 | **24 个文件 / 25 个内嵌资源**；逐个 GZip round-trip、长度/哈希、锁定 DLL/安装器一致；失败编译器退出42时无新 staging 且旧载荷字节不变；manifest 最后发布 |
| 真实主程序集内嵌验证 | 从当前程序集真实 manifest/gz 在隔离 scratch 根 Prepare；逐文件长度/SHA/lock/build-info、后端 metadata、资源名全部验证；不写真实用户缓存、不运行 EXE/安装器 |
| 当前主安全套件 | 本地 net40/C#4/warnings-as-errors 重建后 **516/516，12.8692845 s**，0 errors / 0 failures；完整主程序集 canonical 分析器仍待 CI |

分析器例外在 `sensors/GlobalSuppressions.cs` 逐项给出理由：协议/硬件边界的一般异常隔离、旧 FxCop 的弃用提示、后端未实例化的共享新鲜度类型、借入流的所有权，以及 net40 兼容证书 `Reset()` 的 native handle 释放。未修改 `AllRules.ruleset`，未扩大 Desktop/Manual 排除。

### 验证中的真实异常

- 一次真实载荷新建目录的 `Directory.Move` 抛 IOException：当次 516 项中 1 error、0 failures。相同程序集定向复跑 2/2、完整复跑 516/516，最终 fresh payload + 重建 516/516；未因该暂态错误放宽路径/句柄保护。
- 上次外层调用重定向与脚本内部 `safe-tests.log` 冲突，没有有效新 XML。6,019,444,228 B 的 scratch 日志已保留有界头尾诊断后删除；后续外层日志与内部日志分开。
- 后端测试分析器实际发现并修复四个空数组分配、缺少测试版本 metadata 和 NUnit `params object[]` 特性的 CLS 声明问题；未跳过测试或弱化分析器。

## 保护环境复核

- 用户原 EXE `D:\下载\WinMemoryCleaner.Community.exe` SHA-256 与基线一致：`E13B383CF807D379B1B18878CA411A2CD4A2131CD28C9A6815F205CC076B4114`；未覆盖。
- 原 PID 23072 在记录到的系统重启（UTC `2026-10-19T22:05:24.5Z`）后已不存在，不能声称仍是同一存活实例；当前用户进程不结束。
- 原开机任务仅 `RegistrationInfo/Date` 从 `2026-10-17T12:33:41` 变成 `2026-10-20T06:06:45`；其他 XML 字段一致。不恢复或改写用户任务。
- HKLM 32/64 位清理器设置、共享 PawnIO 注册项与驱动字节仍匹配原基线；共享驱动 SHA-256：`E19F274B9F3C917445BFF19A80BBE2EE34A54995E62BE50073122B20D86AF900`。
- 当前安全测试后无本任务拥有的后端/测试/构建孤儿；失败构建 staging 已清理。

## 尚待完成 / 不得混用的证据

1. 通用 → ASUS/NVAPI → 独立 LHM/OHM WMI 协调、刷新/暂停/设置状态与明确安装确认入口。
2. canonical `community-release.yml` 的同次后端/载荷/主 EXE 构建与两套完整安全测试，最终内嵌比对。
3. 新版实机来源、三轮短时性能与十分钟留存；历史 v3.0.8.3、ASUS/NVAPI 备用、接口替身不能替代新版通用实测。
4. 未授权可信 CPU 驱动安装；无对应 AMD CPU/GPU、Intel GPU 实物证据，必须标注未验证。
5. 新版本文档/版本号、一次独立审查及 CI 发布/单 EXE 更新通道验收；当前尚无新版发布。
