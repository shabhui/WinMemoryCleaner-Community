# 致谢与许可（CREDITS）

本文件说明本仓库的来源、许可、致谢对象与社区分支的定位。条目级的上游问题跟踪见 [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md)。

---

## 1. 原创作者与上游项目

**Windows Memory Cleaner** 由 **Igor Mundstein**（GitHub [@IgorMundstein](https://github.com/IgorMundstein)）创建并长期维护。

| 项 | 值 |
|:---|:---|
| 上游仓库 | <https://github.com/IgorMundstein/WinMemoryCleaner> |
| 许可证 | GPL-3.0（仓库根 [LICENSE](LICENSE)） |
| 上游状态（本地整理快照，约 2026-10-05） | 未归档；最新正式 release `3.0.8`（2025-12-13）；`main` = `a19f25719c9b9a58a0acea99cb8adb4737d2d318`（2025-12-19） |

本仓库中的原始源代码、应用图标、本地化资源等均来自上游项目，其版权归原作者 Igor Mundstein 及各贡献者所有。

---

## 2. 本仓库的定位：非官方社区延续

- 本仓库是上游项目的**非官方社区延续**（unofficial community continuation），基于上游 3.0.8 / `main` 时期的代码继续维护。
- 本仓库与原作者**没有隶属、委托或合作关系**；**未获得原作者的授权或背书**；**不声称正式接任、接管上游项目**，也不代表上游项目发布任何声明或版本。
- 当前工作区中与上述定位一致的文案（`Constants.App.Attribution`）为：
  > Based on Windows Memory Cleaner by Igor Mundstein. Unofficial community continuation; not an endorsed successor.
- 名称与图标沿用上游资源，用于说明来源；社区构建以 `WinMemoryCleaner.Community` / `WinMemoryCleaner Community` 与上游区分。

---

## 3. 许可证与修改标注

- 本项目按 **GNU General Public License v3.0（GPL-3.0）** 分发。仓库根 `LICENSE` 为上游许可证原文；本分支的修改与派生发布同样以 GPL-3.0 提供，并保留原版权与许可声明。SPDX 标识：`GPL-3.0`。
- 按 GPL-3.0 对“修改版本须显著标注”的要求，社区构建的标注（当前分支）如下：

| 标注项 | 值 |
|:---|:---|
| 程序集 / 产品名 | `WinMemoryCleaner.Community` |
| AssemblyVersion / FileVersion | `3.0.8.1` |
| AssemblyInformationalVersion | `3.0.8-community.1` |
| 版权行（`AssemblyInfo.cs`） | `Original work: Igor Mundstein; modifications: shabhui and community contributors. GPL-3.0.` |
| 强名称签名 | 已移除（不再使用上游 `.snk` 与上游证书指纹） |
| 更新 / 发布通道 | 默认关闭（代码注释说明：在社区仓库与受验证的发布通道出现前保持关闭） |

- 因此社区构建**不是上游签名版本**，与上游官方发布物不可互换，也不应被当作上游官方产物。

---

## 4. 社区分支的基线

本社区分支的**起点**是用户分支上的 **PR #204**：

| 项 | 值 |
|:---|:---|
| 作者 | shabhui（GitHub [@shabhui](https://github.com/shabhui)） |
| 上游 PR | <https://github.com/IgorMundstein/WinMemoryCleaner/pull/204> |
| head SHA | `026771dbc81bd34a6f63738920ee6f3142d7f6e5` |
| 本地前置提交 | `56a471b09c382c2773dfb934b0590cc3dd7cba2c`、`4f103f00754a3e6848564942089d324c587420fe`（三者共同构成 PR #204 的 3 个提交） |

该基线包含托盘通知死锁（AB-BA 锁序）与监控线程忙等修复，是社区分支全部改动的起点。

---

## 5. 已采纳的上游 PR 作者

以下上游 PR（在上游仓库仍为 open 状态）已被本社区分支以 `cherry-pick -x` 采纳，在此致谢：

| 上游 PR | 作者 | 采纳内容 | 本地提交 |
|:---|:---|:---|:---|
| [#205](https://github.com/IgorMundstein/WinMemoryCleaner/pull/205) | [MataM15](https://github.com/MataM15) | 原生内存操作错误与权限错误语义修正（含回归测试） | `85a85bb6673d00af80bed2951e478eccd3483cbd` |
| [#206](https://github.com/IgorMundstein/WinMemoryCleaner/pull/206) | [MataM15](https://github.com/MataM15) | 优化耗时计入最终内存释放阶段（含测试） | `bb6f9ea878c2667392d6dfd4a7b0c3cfa51ae04e` |
| [#207](https://github.com/IgorMundstein/WinMemoryCleaner/pull/207) | [MataM15](https://github.com/MataM15) | 抽出 `AutoOptimizationPolicy`，GUI 与服务共用触发策略（含测试） | `8072550cf1234d770b0998fb04aad0aefc84ed9d` |
| [#208](https://github.com/IgorMundstein/WinMemoryCleaner/pull/208) | [MataM15](https://github.com/MataM15) | 文档 / 文案：进程排除只影响 Working Set（30 种语言 + README） | `2312ba6127a99029c901519e6e554c35d5638b64` |
| [#209](https://github.com/IgorMundstein/WinMemoryCleaner/pull/209) | [MataM15](https://github.com/MataM15) | 展开窗口可调整大小 | `121a823c94084aa866fda7b2722647e9f22cd63e` |
| [#188](https://github.com/IgorMundstein/WinMemoryCleaner/pull/188) | [VenusGirl](https://github.com/VenusGirl) | 韩语本地化更新与修正（2 个提交） | `d243c57638a0108e2e981a3586b412d9d0627c99`、`c2d0e4f07a54292a63ac0abc1e7e0f696fe00a47` |
| [#186](https://github.com/IgorMundstein/WinMemoryCleaner/pull/186) | [rammba](https://github.com/rammba) | 塞尔维亚语双语支持：新增 Serbian (Latin)、原 Serbian 更名 (Cyrillic)、README 署名（4 个提交） | `0ce815e4bea33b7627007b8e0d2e4cb21b2a651d`、`a3c65df7eff859d486d05dc1c764b4074e0571c9`、`18beb497bb285ad559ec508001638be5c8c24867`、`87adb02320fe7de0112776b74bd3ae443ed317f7` |
| [#195](https://github.com/IgorMundstein/WinMemoryCleaner/pull/195)（部分采纳） | [Y-ASLant](https://github.com/Y-ASLant) | 仅挑选 `ComputerService` 两处 `GCHandle.Alloc(0)` 哑句柄初始化修复（手动落地并保留来源标注，非整包合并） | 包含在社区修复提交中 |

另有一处整合修复：#186 的 `Serbian (Latin).json` 缺少 #208 新增的 `ProcessExclusionListDescription` 键，社区分支按 #208 自带的 Serbian (Cyrillic) 文案转写补齐，以保持 rammba 双语文件的一致性。

完整的 head SHA 与采纳源 SHA 对照见 [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md) 第 5 节。

---

## 6. 评估过但未整包采纳的上游 PR 作者

以下 PR 被逐项评估后**未整包采纳**（原因见 [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md) 第 4、6 节）。感谢其调查与实现工作，其中部分改动被用作参考或对照，其个别独立小块仍可能在后续单独评估：

- [#184](https://github.com/IgorMundstein/WinMemoryCleaner/pull/184) — [ricred](https://github.com/ricred)
- [#195](https://github.com/IgorMundstein/WinMemoryCleaner/pull/195) — [Y-ASLant](https://github.com/Y-ASLant)（其中两处 GCHandle 修复已单独采纳，见第 5 节）
- [#203](https://github.com/IgorMundstein/WinMemoryCleaner/pull/203) — [iishanmakkar](https://github.com/iishanmakkar)
- [#212](https://github.com/IgorMundstein/WinMemoryCleaner/pull/212) — [bajixx-19](https://github.com/bajixx-19)

同时感谢所有 Issue 报告者与讨论参与者，他们的复现步骤、日志与分析是上述判断的基础。

---

## 7. 上游本地化贡献者

以下表格**镜像自上游 README 的本地化贡献者表**（上游 README 为最新来源；如有差异，以上游 README 为准）：

| 语言 | 贡献者 | 语言 | 贡献者 |
|:---|:---|:---|:---|
| 🇦🇱 Albanian | [Omer Rustemi](https://github.com/omerrustemicode) | 🇯🇵 Japanese | [dai](https://github.com/dai) |
| 🇸🇦 Arabic | [Abderraouf FELLAHI](https://github.com/flh-raouf), [Abdulmajeed Al-Rajhi](https://github.com/Abdulmajeed-Alrajhi) | 🇰🇷 Korean | [VenusGirl](https://github.com/VenusGirl) |
| 🇧🇬 Bulgarian | [Konstantin](https://github.com/constantinejc) | 🇲🇰 Macedonian | [Dimitrij Gjorgji](https://github.com/Cathadox) |
| 🇨🇳 Chinese (Simplified) | [KaiHuaDou](https://github.com/KaiHuaDou), [Kun Zhao](https://github.com/kzhdev), [Rayden](https://github.com/raydenake22) | 🇳🇴 Norwegian | [Dan](https://github.com/danorse) |
| 🇨🇳 Chinese (Traditional) | [Rayden](https://github.com/raydenake22), [rtyrtyrtyqw](https://github.com/rtyrtyrtyqw) | 🇮🇷 Persian | [Kavian](https://github.com/KavianK) |
| 🇳🇱 Dutch | [Jesse](https://github.com/dragonhuntermc), [hax4dazy](https://github.com/hax4dazy) | 🇵🇱 Polish | [Patryk](https://github.com/Fresta56) |
| 🇫🇷 French | [William VINCENT](https://github.com/wixaw) | 🇵🇹 Portuguese (Portugal) | AI |
| 🇩🇪 German | [Calvin](https://github.com/Slluxx), [Niklas Englmeier](https://github.com/iamniklas), [Steve](https://github.com/uDEV2019) | 🇷🇺 Russian | [Ruslan](https://github.com/ruslooob) |
| 🇬🇷 Greek | [Theodoros Katsageorgis](https://github.com/tkatsageorgis) | 🇷🇸 Serbian (Cyrillic & Latin) | [Dragoš Milošević](https://github.com/DragorMilos), [Radoš Milićev](https://github.com/rammba) |
| 🇮🇱 Hebrew | [Eliezer Bloy](https://github.com/eliezerbloy) | 🇸🇮 Slovenian | [Jadran Rudec](https://github.com/JadranR) |
| 🇭🇺 Hungarian | [gycsisz](https://github.com/gycsisz) | 🇪🇸 Spanish | [Ajneb Al Revés](https://github.com/AjnebAlReves), [Fran](https://github.com/FrannDzs) |
| 🇮🇩 Indonesian | [Mochammad Misbahus Surur](https://github.com/Eskeyz), [Minids](https://github.com/tdnphantom) | 🇹🇭 Thai | [nongice](https://github.com/21icepril) |
| 🇮🇪 Irish | [Happygolucky254](https://github.com/Happygolucky254) | 🇹🇷 Turkish | [Rıza Emet](https://github.com/rizaemet), [Viollje](https://github.com/Viollje) |
| 🇮🇹 Italian | [Michele](https://github.com/wintrymichi) | 🇺🇦 Ukrainian | [Riebi](https://github.com/RieBi), [Oleksandr](https://github.com/Mariachi1231) |

其中 Portuguese (Portugal) 在上游 README 中标为 AI 生成的基线。上游其他历史贡献者请以上游仓库的提交历史与发布记录为准。

---

## 8. 捐赠与外部链接

- 应用内"捐赠"窗口与 README 中的捐赠链接（GitHub Sponsors、Ko-fi、BTC、ETH）**指向原作者 Igor Mundstein**，由上游项目保留。
- 本社区分支**不新设、不替换**为社区自身的捐赠渠道或社区仓库链接；工作区文案（`Constants.App.Donation.Attribution`）对此的说明为：
  > These links support the original author, Igor Mundstein, not the Community maintainers.

---

## 9. 声明范围

本文件只说明来源、许可与致谢，**不对任何上游 Issue 或 PR 作"已解决 / 已合并"的声明**。社区分支对上游 22 个开放 Issue 与 12 个开放 PR 的处置状态与依据，见 [UPSTREAM-TRACKING.md](UPSTREAM-TRACKING.md)。
