# Process Page Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user has selected native execution: the main agent implements and verifies continuously in this session; do not delegate implementation or request routine step confirmations.

**Goal:** 优化现有进程页的搜索反馈、异常与选择安全、批量刷新成本，同时保持原有清理与监控行为。

**Architecture:** 保留现有监控采样与 generation 机制，在 UI 侧引入独立的进程呈现状态和协调对象。ProcessTable 合并成员通知并返回字段变更摘要；协调对象负责 150 ms 一次性搜索合并、条件视图刷新与有效选择。窗口只呈现反馈并在操作入口/确认后复核目标，真实操作服务安全校验不变。

**Tech Stack:** WPF / .NET Framework 4.0 / C# 4 / NUnit 2.6.4 / Windows PowerShell；沿用现有依赖、规则集与九个分析器。

**Spec:** `docs/superpowers/specs/2026-10-22-process-page-optimization-design.md`。

## Global Constraints

- 主程序保持 WPF、.NET Framework 4.0、C# 4；保留 `AllRules.ruleset`、既有九个分析器及 warnings-as-errors，不降低严格构建要求。
- 保留内存清理、托盘、设置、主题、语言、紧凑模式和校验更新行为。
- 保留横向/竖向监控条尺寸策略、保存位置、鼠标穿透与不抢焦点行为。
- 保留现有按需采样、单工作者、代际失效、后台暂停及资源释放机制。
- 保留进程操作的终止确认、同一句柄上的 PID/创建时间/退出/critical/protection 校验和安全性不确定时拒绝操作。
- 保留名称与 PID 的包含匹配、名称不区分大小写、忽略查询首尾空格、现有排序方向及不可用值排在真实零之后的规则。
- 合并静止期为 **150 ms**；500/2000 行的分场景 UI 快照应用、输入处理和实际查询提交处理 **p95 ≤50 ms**；静止期单独计时。
- 不新增依赖、不升级框架、不调整温度检测/信任、不改发布工作流、不提高版本号或发布新版本，不重复已完成的温度性能活动。
- 不覆盖 `D:\下载\WinMemoryCleaner.Community.exe`，不改真实 cleaner HKLM/启动任务，不结束用户实例，不安装、替换、降级或卸载共享 PawnIO。
- 所有操作测试用替身或取消确认；输出和临时工具放本 session scratch，保留失败/中断证据，产品与测试构建身份分别记录。

## Review Focus

1. 待提交查询的大小写/空格等效变化或返回已提交值：不重启等待或重复刷新，不丢失最终输入；Task 2 测试固定。
2. 监控器返回 null 与单项读数不可用：前者失败并关闭操作，后者仍为成功进程列表；Task 1/2 测试固定。
3. 批量 Reset 与排序同时发生：仅保留原身份且仍匹配的选择，绝不能按 PID 恢复替换行；Task 1/2 测试固定。
4. 模态确认期间成功列表变失败、目标退出/换身份或被过滤：确认后拒绝入队，取消零次执行；Task 3 的安全替身测试固定。
5. 离页后很快重进或 Dispose 时搜索回调已排队：旧 revision 不能修改新会话，后台没有新轮询；Task 2/3 测试固定。

---

## File Structure and Interfaces

| 路径 | 责任 |
| --- | --- |
| 新建 `src/Core/Monitoring/ProcessPresentationState.cs` | 无 WPF/硬件依赖的活动、成功/失败、计数和筛选待处理状态 |
| 新建 `src/Model/Monitoring/ProcessTableChanges.cs` | 成员及字段变化 flags，是否需要视图重建的纯判断 |
| 新建 `src/Model/Monitoring/ProcessRowCollection.cs` | ObservableCollection 子类，批次内合并 Count/Item[]/成员通知 |
| 修改 `src/Model/Monitoring/ProcessRow.cs` | 保留现有行差异通知与公共接口，附加内部批量 reconcile/身份查询 |
| 新建 `src/ViewModel/ProcessListPresentation.cs` | UI 线程进程表、集合视图、搜索、状态文字、有效选择与一次性合并 |
| 修改 `src/ViewModel/MonitoringViewModel.cs` | 向新协调对象转发既有属性，接入活动状态与有效快照；采样调度不变 |
| 修改 `src/Core/Monitoring/MonitorText.cs` | 新增中文/英文与英文 fallback 文字 |
| 修改 `src/View/Window/MainWindow.xaml`、`.xaml.cs`、`src/Resources/MonitoringStyles.xaml` | 清空/Esc、状态/计数呈现、操作门槛与确认后复核 |
| 新建 `src/Test/ProcessPresentationStateTests.cs`、`ProcessTableBatchTests.cs`、`ProcessListPresentationTests.cs`、`MonitoringProcessTests.cs`；修改 `src/Test/MonitoringWindowTests.cs` | 纯状态、真实集合视图、UI 协调、受控采样与真实窗口回归 |
| 新建 `tools/ProcessPageProbe.cs`、`tools/verify-process-page.ps1` | 受控 500/2000 行、基线与优化版分别运行的可复查定向测量 |
| 修改 `docs/monitoring-guide.zh-CN.md`、`CHANGELOG.md`；新建 `docs/process-page-optimization-report.zh-CN.md` | 本批使用说明、未发布变更和真实验证证据 |

项目已有 `Core\Monitoring\*.cs`、`Model\Monitoring\*.cs`、`Test\*.cs` 编译 glob；新 ViewModel 文件须在 `src/WinMemoryCleaner.csproj` 的 ViewModel/Monitoring 项添加显式 Compile 项。不增加包、引用或改变测试条件。

## Checked Local Commands

在 Windows PowerShell 中使用以下函数，保持输出目录/报告按唯一 label 分开；每次新 label 不覆盖先前失败记录。

```powershell
$R='G:\AI项目\WinMemoryCleaner-main\WinMemoryCleaner-Community'
$S=$env:PI_SCRATCH_DIR
$N=Join-Path $S 'wmc-goal\nunit\nunit-console.exe'
$B=Join-Path $S 'InvokeStrictProcessBuild.ps1'
function Build([string]$Label,[bool]$Test=$true) {
    $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$B,'-Label',$Label)
    if($Test){$arguments+='-TestBuild'}
    & powershell.exe @arguments
    if($LASTEXITCODE -ne 0){throw ('Strict build failed: '+$Label)}
}
function Cases([string]$Label,[string]$Fixtures='') {
    $exe=Join-Path $S ('wmc-goal\'+$Label+'\WinMemoryCleaner.Community.exe')
    $arguments=@($exe,'/framework:net-4.0','/exclude:Manual,Desktop','/labels','/timeout:60000','/noshadow','/nologo',('/work:'+(Join-Path $S 'universal-backend-tests')),('/xml:'+(Join-Path $S ('universal-backend-tests\'+$Label+'.xml'))))
    if($Fixtures.Length -ne 0){$arguments+=('/run:'+$Fixtures)}
    & $N @arguments
    Write-Output ('NUNIT_EXIT='+$LASTEXITCODE)
}
```

Build helper明确加载全部九个 analyzer、原 ruleset、net40 reference assemblies 和 C#4；这是严格本地开发构建，不称作 canonical release。执行前 `Test-Path` 核对 helper、compiler、runner 和全部 analyzer；缺失则报告实际问题，不降低检查要求。

Git 一律加 `-c safe.directory=G:/AI项目/WinMemoryCleaner-main/WinMemoryCleaner-Community -C $R`；每次提交只暂存本任务文件，并先运行 `diff --cached --check`。

### Task 1: Independent state and batch-aware process table

**Files:** 新建上述 Core 状态、Model 摘要/集合及两个对应 Test 文件，修改 `src/Model/Monitoring/ProcessRow.cs`。

**Interfaces:**
- `internal enum ProcessListStatus { Inactive, Loading, Ready, Empty, NoMatches, Filtering, Failed, Stale }`。
- `internal sealed class ProcessPresentationState`：只读 `IsActive`, `HasSnapshot`, `HasFailure`, `IsFiltering`, `FailureDetail`, `TotalCount`, `MatchCount`, `Status`, `CanUseCurrentData`；方法 `void SetActive(bool active)`, `void SetFiltering(bool filtering)`, `void AcceptSnapshot(int total,int matched)`, `void UpdateCounts(int total,int matched)`, `void RejectSnapshot(string detail)`。
- `[Flags] internal enum ProcessTableChange { None=0, Membership=1, Name=2, WorkingSetBytes=4, PrivateBytes=8, CpuUsage=16, GpuUsage=32, GpuMemoryBytes=64 }`；`ProcessTableChanges.RequiresRefresh(ProcessTableChange changes,string field,bool hasQuery)` 返回 bool。
- `ProcessRowCollection.BeginUpdate()` / `EndUpdate()` 为内部成对方法；批量成员变化只发一次 Reset，行级 PropertyChanged 不被吞掉。
- `ProcessTable` 保留 `public void Reconcile(IList<ProcessSnapshot>)` 与 `ObservableCollection<ProcessRow> Rows`；新增 `internal ProcessTableChange ReconcileBatch(IList<ProcessSnapshot>)`、`internal ProcessRow Find(int id)`。公共 Reconcile 调用批量入口，不改变 null/重复 snapshot 的原有忽略语义。

- [ ] **Step 1: Freeze the product baseline before editing code.** Run `Build 'process-baseline'` then `Cases 'process-baseline' 'WinMemoryCleaner.Test.ProcessPresentationTests,WinMemoryCleaner.Test.MonitoringPresentationTests,WinMemoryCleaner.Test.ProcessActionTests'`；要求构建退出0、NUNIT_EXIT=0。保存该 EXE SHA-256、当前 HEAD 与未变产品基线 `11afbfd` 到 scratch `process-baseline-identity.json`；不执行生产 App。
- [ ] **Step 2: Add the red tests.** 状态新类型用反射检查存在性，确保基线能编译并在 NUnit 正确断言失败。fixture内私有 `object NewState()` 先Assert存在 `WinMemoryCleaner.ProcessPresentationState`，再用Activator创建；`void Call(object target,string name,params object[] arguments)` 和 `object Get(object target,string name)` 分别反射调用既定非公开方法/属性。下面的计数与原行由测试的真实ListCollectionView事件和受控快照取得，不新增产品测试接口。测试名/最终断言：
  ```csharp
  var state=NewState();
  Call(state,"SetActive",true);
  Assert.AreEqual("Loading",Get(state,"Status").ToString());
  Call(state,"RejectSnapshot","fixture failure");
  Assert.AreEqual("Failed",Get(state,"Status").ToString());
  Call(state,"AcceptSnapshot",2,1);
  Call(state,"RejectSnapshot","fixture failure");
  Call(state,"SetFiltering",true);
  Assert.AreEqual("Stale",Get(state,"Status").ToString());
  Assert.IsFalse((bool)Get(state,"CanUseCurrentData"));
  // BatchMembershipAndSortChangesProduceOneViewReset
  Assert.AreEqual(1, viewResets); Assert.AreEqual(0, incrementalViewEvents);
  // IdenticalSnapshotDoesNotNotifyAndPidReuseReplacesIdentity
  Assert.AreEqual(0, identicalNotifications); Assert.AreNotSame(oldRow, replacement);
  ```
  再覆盖 inactive 忽略成功/失败、成功0行 Empty、非空0匹配 NoMatches、失败后的 UpdateCounts 不解除失败、null/重复项、名称/各指标 flags、排序相关性与不可用值。
- [ ] **Step 3: Prove red.** `Build 'process-state-red'`；`Cases 'process-state-red' 'WinMemoryCleaner.Test.ProcessPresentationStateTests,WinMemoryCleaner.Test.ProcessTableBatchTests'`，预期新增状态不存在及未合并视图事件的明确失败，保存 XML/log。
- [ ] **Step 4: Implement the defined state and batch interfaces.** 状态优先级为 inactive → failed/stale → loading/filtering → empty/no-matches/ready；从失败恢复与本地计数更新分开。批量 suppress 集合成员/集合属性通知，在 finally EndUpdate 发一次真实 Reset；变更摘要在覆盖旧快照前计算，同身份行继续 Apply。
- [ ] **Step 5: Prove green.** `Build 'process-state-green'`；`Cases 'process-state-green' 'WinMemoryCleaner.Test.ProcessPresentationStateTests,WinMemoryCleaner.Test.ProcessTableBatchTests,WinMemoryCleaner.Test.ProcessPresentationTests,WinMemoryCleaner.Test.MonitoringPresentationTests'`，要求0失败/错误、NUNIT_EXIT=0，实际 ListCollectionView 事件/排序结果满足测试。
- [ ] **Step 6: Commit only this deliverable.** `feat: add process presentation state and batched updates`，暂存本任务 Model/Core/Test 文件。

### Task 2: UI coordinator, one-shot search and monitoring integration

**Files:** 新建 `src/ViewModel/ProcessListPresentation.cs`、`src/Test/ProcessListPresentationTests.cs`、`src/Test/MonitoringProcessTests.cs`；修改 `MonitoringViewModel.cs`、`MonitorText.cs`、`WinMemoryCleaner.csproj`。

**Interfaces:**
- `internal sealed class ProcessListPresentation : ObservableObject, IDisposable`，构造 `ProcessListPresentation(Dispatcher dispatcher,MonitorText labels)`；不引用硬件/设置/监控服务。
- 属性：`ListCollectionView View`、`string SearchQuery`、`ProcessRow SelectedProcess`、`int TotalCount`、`int MatchedCount`、`bool CanClearSearch`、`bool CanActOnSelectedProcess`、`string CountText`、`string StatusText`、`string StatusDetail`、`bool HasStatus`。
- 方法：`SetActive(bool)`, `Apply(ResourceSnapshot snapshot,string failure)`, `ClearSearch()`, `ListSortDirection Sort(string field)`, `bool IsCurrentTarget(ProcessSnapshot target)`, `RefreshText()`, `Dispose()`；内部 `CommitQuery(int revision)` 被一次性回调调用并检查 revision/活动/释放状态。
- MonitoringViewModel 保留 `ProcessesView`、`SearchQuery`、`SelectedProcess`、`ProcessCount`、`ProcessCountText`、`Sort(string)`；新增 `CanClearSearch`, `CanActOnSelectedProcess`, `ProcessStatusText`, `ProcessStatusDetail`, `HasProcessStatus`、`ClearSearch()` 与内部 `IsCurrentProcessTarget(ProcessSnapshot)`，仅转发协调对象。

- [ ] **Step 1: Add red coordinator/integration tests.** 用 `[RequiresSTA]`、受控 Dispatcher 和原生零操作的监控替身；尚不存在的内部协调对象用反射获得。最终断言：
  ```csharp
  // EquivalentQueryAndFastInputDoNotRepeatRefresh
  Assert.AreEqual(1, resetsAfterTwentyInputs); Assert.AreEqual(1, view.Count);
  Assert.AreEqual(expectedId, ((ProcessRow)view.GetItemAt(0)).Id);
  Assert.AreEqual(0, resetsAfterCaseAndWhitespaceEquivalentInput);
  // FailureClearsSelectionAndNullDiffersFromUnavailableMetrics
  Assert.IsNull(model.SelectedProcess); Assert.IsFalse(model.CanActOnSelectedProcess);
  Assert.AreEqual("Stale", stateAfterFailure); Assert.AreEqual("Ready", stateAfterUnavailableMetricSnapshot);
  // BatchResetPreservesOnlyTheSameVisibleIdentity
  Assert.AreSame(oldSelected, selectedAfterMembershipChange);
  Assert.IsNull(selectedAfterPidReuseOrFilterExclusion);
  ```
  再覆盖150ms静止期、等效待提交值不重启等待、返回已提交值取消等待、空查询即时清空、等待时快照按已提交查询、失败/恢复清除旧数据选择、离页/快重进/Dispose 失效revision、搜索不增加采样次数、过期generation不能覆盖新会话；中文/英文/de fallback不引起Reset或改变选择。
- [ ] **Step 2: Prove red.** `Build 'process-coordinator-red'`；`Cases 'process-coordinator-red' 'WinMemoryCleaner.Test.ProcessListPresentationTests,WinMemoryCleaner.Test.MonitoringProcessTests'`，保存新增行为缺失的 NUnit 失败，不调用真实 installer。
- [ ] **Step 3: Implement ProcessListPresentation.** 使用 Task1 的状态/变更摘要；成员 Reset 已重建视图时不再 Refresh，否则仅在 RequiresRefresh 返回true时重建。原始与已提交查询分离；每次有效变化只留一个150ms pending回调，并捕获不可变revision。清空/撤销/失活均取消；被取消回调不能影响新会话。批次末重算 View.Count 和选择，失败恢复不恢复旧选择。
- [ ] **Step 4: Integrate without changing the pump.** MonitoringViewModel 将进程属性转发，成功/失败应用只在现有 UI generation 检查后调用 Apply；原 Sample 后继续清空 snapshot.Processes。SetViewState 同步活动状态，Dispose 取消协调对象；本地化刷新仅刷新文字。新本地化文案遵循规格“—”计数、加载/空/无匹配/失败/过期/正在筛选的优先级。
- [ ] **Step 5: Prove green.** `Build 'process-coordinator-green'`；`Cases 'process-coordinator-green' 'WinMemoryCleaner.Test.ProcessListPresentationTests,WinMemoryCleaner.Test.MonitoringProcessTests,WinMemoryCleaner.Test.ProcessPresentationStateTests,WinMemoryCleaner.Test.ProcessTableBatchTests,WinMemoryCleaner.Test.MonitoringSchedulerTests,WinMemoryCleaner.Test.TemperatureSettingsTests'`，全部通过；确认未改变监控调度/温度接口。
- [ ] **Step 6: Commit only this deliverable.** `feat: coalesce process search and guard stale selections`，暂存本任务新增/修改文件。

### Task 3: Real-window feedback and safe action entry

**Files:** 修改 `MainWindow.xaml`、`MainWindow.xaml.cs`、`MonitoringStyles.xaml`、`MonitorText.cs`、`src/Test/MonitoringWindowTests.cs`。

**Interfaces:**
- 保留公共 MainWindow() 与现有内部 `MainWindow(Func<IResourceMonitor>)`；附加内部构造 `MainWindow(Func<IResourceMonitor>,Func<ProcessSnapshot,bool,bool,ProcessActionResult> executeAction)`，只为安全替身测试提供动作注入，默认仍调用原 ProcessActionService.Execute。
- 新控件名：`ClearProcessSearch`、`ProcessStatus`、`ProcessEmptyState`；继续使用 `ProcessSearch`、`ProcessGrid`、`TrimSelected`、`TerminateSelected`。
- 新处理器 `OnClearProcessSearch(object,RoutedEventArgs)`、`OnProcessSearchKeyDown(object,KeyEventArgs)`；Esc仅清空搜索，不拦截窗口级关闭/确认。

- [ ] **Step 1: Add red window tests with safe substitutes.** CreateWindow 增加可选监控/操作替身，默认旧测试不变。新签名通过反射检查，让red失败不接触真实动作；对话相关测试始终用替身或取消。断言：
  ```csharp
  // ProcessSearchFeedbackAndEscapeUseTheRealControls
  Assert.AreEqual("", search.Text); Assert.IsTrue(search.IsKeyboardFocused);
  StringAssert.Contains("3 /", countText); Assert.AreEqual("No matching processes", emptyText);
  // ConfirmedTargetChangedWhileDialogWasOpenNeverQueuesAction
  Assert.AreEqual(0, fakeExecutions); Assert.IsFalse(trim.IsEnabled); Assert.IsFalse(terminate.IsEnabled);
  // CancelledTerminationHasZeroExecutions
  Assert.AreEqual(0, fakeExecutions); StringAssert.Contains("PID ", capturedPrompt);
  ```
  分别模拟确认期间失败/退出/PID复用/查询排除/离页；还测试按钮禁用被绕过直接调用入口仍拒绝。重写原 TerminationDialog 测试为真实成功表中的受控选择，不依赖任意外部旧行。检查加载/失败/旧表状态、长诊断、中文/en/de、主题和760×520最小布局，记录binding错误。
- [ ] **Step 2: Prove red via STA runner.** `Build 'process-window-red'`；在该scratch输出目录编译/运行以下宿主命令，预期新控件/行为缺失，所有cleanup仍执行。引用本机.NET4程序集；宿主仅编译工具，不替代产品严格构建。
  ```powershell
  $D=Join-Path $S 'wmc-goal\process-window-red'
  $F=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
  & "$F\csc.exe" /nologo /target:exe /langversion:4 "/out:$D\DesktopRunner.exe" /r:System.dll /r:System.Core.dll "/r:$F\WPF\WindowsBase.dll" "/r:$F\WPF\PresentationFramework.dll" "$S\DesktopRunner.cs"
  if($LASTEXITCODE -ne 0){throw 'STA runner compilation failed'}
  Copy-Item -LiteralPath "$S\wmc-goal\review-green\DesktopRunner.exe.config" -Destination "$D\DesktopRunner.exe.config"
  & "$D\DesktopRunner.exe" "$D\WinMemoryCleaner.Community.exe" *> "$S\process-window-red.log"
  Write-Output ('STA_EXIT='+$LASTEXITCODE)
  ```
  green及最终构建使用同样命令并替换目录/日志label。窗口fixture注册并清理 `PresentationTraceSources.DataBindingSource` 的TextWriterTraceListener，将绑定诊断保存到本次scratch日志，不能只靠runner退出码宣称无绑定错误。
- [ ] **Step 3: Implement window feedback and action gating.** 添加本地化清空按钮、表区空状态、独立失败/过期提示，诊断不撑大窗口。按钮绑定 CanActOnSelectedProcess 并保持 _actionRunning 防重入；发起前及终止确认后用捕获 target 调用 IsCurrentProcessTarget，失败给本地化拒绝原因。现有真实服务检查与取消行为不变。
- [ ] **Step 4: Prove green.** `Build 'process-window-green'`；同一STA runner运行全部 MonitoringWindowTests，要求 `FAILED=0`、非零PASSED、退出0、binding日志无新增错误；`Cases 'process-window-green' 'WinMemoryCleaner.Test.ProcessActionTests,WinMemoryCleaner.Test.ProcessListPresentationTests,WinMemoryCleaner.Test.MonitoringProcessTests'` 退出0。
- [ ] **Step 5: Commit only this deliverable.** `feat: clarify process feedback and revalidate confirmed actions`，暂存本任务窗口/样式/文案/测试文件。

### Task 4: Targeted measurement, full verification and delivery

**Files:** 新建 `tools/ProcessPageProbe.cs`、`tools/verify-process-page.ps1`、`docs/process-page-optimization-report.zh-CN.md`；修改 guide/CHANGELOG 与本计划checkbox。

**Interfaces:**
- Probe 为独立 `[STAThread]` 反射宿主，参数 `<testExe> <outputDirectory> baseline|optimized`，运行时断言 App.IsTestBuild=true；产品两个版本必须分进程加载，不能让同名程序集串用。
- baseline adapter 使用保留下来的真实 ProcessTable / ListCollectionView / ProcessRowComparer，并复现基线 Reconcile+无条件Refresh和每次查询Refresh；optimized adapter用真正 ProcessListPresentation。两者绑定相同虚拟化DataGrid，输入构造在计时外；标明“受控呈现热路径”，不冒充完整生产App或真实硬件性能。
- PowerShell入口参数 `-BaselineExe`, `-OptimizedExe`, `-OutputDirectory`，输出只能为新建session scratch目录；编译宿主/每版JSON/原始jsonl/log/身份记录都在该目录，失败不删除。

- [ ] **Step 1: Add and self-test the probe assertions.** 先用 Task1 基线运行，验证能准确记录重复刷新，并以优化验收断言得到预期red。Probe严格拒绝非测试构建/重复输出目录；脚本parse-check通过。场景为500/2000行 × identical/non-sort/sort/membership/query；快照每场景10次预热+60次记录，查询10组×20次约10ms相邻输入。每条JSONL记录 `version`、`rows`、`scenario`、`iteration`、`warmup`、`viewResets`、`comparisons`、`elapsedMilliseconds`、`finalCount`、`resultCorrect`；查询额外记录每次输入时间/处理耗时、实际提交处理耗时、静止期和端到端时间。用CountingComparer和真实View事件计Reset/排序，Dispatcher hooks识别含查询Reset的实际提交回调耗时。
- [ ] **Step 2: Run the exact comparison.** `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$R\tools\verify-process-page.ps1" -BaselineExe "$S\wmc-goal\process-baseline\WinMemoryCleaner.Community.exe" -OptimizedExe "$S\wmc-goal\process-window-green\WinMemoryCleaner.Community.exe" -OutputDirectory "$S\process-page-comparison"`。输出优化断言全部通过：相同/非排序0重建，成员/排序每批≤1，最终筛选/排序/身份正确，分场景nearest-rank p95≤50ms。独立重算原始JSONL次数/p95；任何失败先复现、局部修复与定向重测，不隐去失败结果或重跑温度活动。
- [ ] **Step 3: Run full strict builds and regression.** `Build 'process-final-tests'`；`Cases 'process-final-tests'`，要求根报告全部通过且未有新增排除/跳过，原有554基线用例仍执行；再运行该构建全部STA窗口案例。`Build 'process-final-production' $false`，生产构建也严格通过，但不启动生产App。检查git差异保证后端/锁/信任/发布输入和AllRules未改变。
- [ ] **Step 4: Perform one optional bounded final review.** 先完成自查；需要独立意见时只发一次read-only review，范围为本规格/新增进程页代码与测试，不重复旧温度release review。只修复可复现本批问题并重测，不扩成多轮广泛review。
- [ ] **Step 5: Write the delivery evidence.** 报告包括基线/优化二进制SHA、实际用例/失败/跳过数、500/2000分场景原始路径与结果、合并静止期/处理/端到端区别、binding/STA结果及保护环境检查。未手动看图则明确未做手动视觉验收；不宣称CPU/私有内存下降或canonical release。guide写明150ms合并、清空/Esc、过期数据不可操作；CHANGELOG仅记未发布条目。
- [ ] **Step 6: Final scope/cleanup/commit.** 只清理本次已确认归属的测试资源，保留失败日志；执行既有只读保护环境比较，要求无本次改变。`git diff --check` 与状态核对通过后，提交 `docs: record process page optimization verification`；所有实施checkbox完成或有真实阻塞注明。交付结果、验证与剩余限制，不push标签、不覆盖正式资产或用户EXE。

## Plan Self-Review and Execution Gate

状态/反馈对应规格4/8，Task1/2/3；搜索合并与生命周期对应规格5，Task2/3；批量与选择对应规格6/7，Task1/2/3；测试/测量/交付对应规格9/10，Task4。各任务接口名称与后续调用一致；五类Review Focus均有归属测试；不重开温度性能或扩大产品范围。

用户已明确以“是直接开始干”批准本计划；正在使用executing-plans与正常TDD流程native连续执行，不再等待审批。执行Ruling：旧PowerShell -File会将无BOM UTF-8 helper内的中文路径误解码；scratch ASCII包装用显式UTF-8 ReadAllText + ScriptBlock.Create加载原helper，保留九个分析器、规则集和全部严格构建要求。
