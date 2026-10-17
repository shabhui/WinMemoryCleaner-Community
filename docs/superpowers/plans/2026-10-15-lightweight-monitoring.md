# Lightweight resource monitoring and UI implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver the approved lightweight WPF resource monitor, safe per-process controls and optional overlay without regressing the original cleaner.

**Architecture:** Keep the existing cleaner view model/services and separate resource snapshots, native sampling, process actions, demand scheduling and presentation. A single non-overlapping background sampler produces snapshots; the dispatcher applies at most one pending result, and detailed sampling is suspended without visible consumers. Use virtualized WPF tables and lightweight native driver calls; do not bundle sensor software.

**Tech Stack:** WPF, .NET Framework 4.0, C# 4, Windows native APIs, PDH GPU counters, NVIDIA driver API, optional already-installed LibreHardwareMonitor WMI provider, existing NUnit 2.6.4.

**Spec:** `../../../.pi/goal/winmemorycleaner-轻量化监控与-ui-优化-20261015-2156.md` (workspace-root approved artifact; approved autonomous execution).

## Global Constraints

- Preserve WPF / .NET Framework 4.0 / C# 4, warnings-as-errors, settings identity, attribution and update channel.
- Default refresh 2 seconds; supported intervals exactly 1, 2, 5 seconds. Overlay default off.
- No DLL injection, anti-cheat bypass, new drivers/services, large dependencies, fake temperature, periodic forced GC or self-working-set trimming.
- Required portable runtime growth at most 1 MiB against a same-toolchain baseline.
- Do not stop the user's existing instance or test global cleanup. Only this verification's child processes may be trimmed/terminated.
- Benchmark Release builds: 30-second warm-up, 60-second measurement, three runs/state and medians; 10-minute retention and >=200-record dispatcher responsiveness checks.
- Native execution is authorized by the approved goal; a substantial, independent native-backend implementation may run in parallel. At most one optional read-only review, no recursive delegation.

## Review Focus

- PID reuse between selection and action: verify creation time on the same native handle used for the operation.
- Counter/provider failures mid-session: explicitly unavailable metrics and bounded retry; no spin or queued dispatcher backlog.
- Monitor removal/DPI changes: restore to a visible area rather than leaving the overlay off-screen.
- Hidden-window transitions during in-flight sampling: invalidate stale detailed results and release caches/provider handles.
- Language/theme switches and compact mode: preserve existing cleaner settings and correctly refresh new labels and overlay.

---

### Task 1: Isolated same-toolchain baseline and verification tooling

**Files:** Create `tools/verify-monitoring.ps1`, `tools/MonitoringHarness.cs` and `docs/monitoring-verification.md`; temporary baseline source/builds/logs live in session scratch.

**Interfaces:** Harness takes assembly path, mode, output directory, sample duration and repeat index; launches the real compiled WPF window with HKLM redirected to a disposable HKCU registry tree. No changes to the production startup identity.

- [ ] Save pristine HEAD source to scratch and build Release using net40 reference assemblies; archive baseline build log and file sizes.
- [ ] Implement the registry-isolated real-window harness, safe child-process scenario and measurements in reproducible project tools. Verify no real settings/startup keys change.
- [ ] Run three baseline trials for tray and main states using 30/60-second timings; record CPU normalized by logical processor count, private bytes and working set separately.

### Task 2: Native snapshots and safe selected-process operations

**Files:** Create `src/Model/Monitoring/{ResourceSnapshot,ProcessSnapshot,MetricValue}.cs`, `src/Interfaces/IResourceMonitor.cs`, `src/Service/Monitoring/{ResourceMonitor,ProcessActionService,GpuSampler,TemperatureSampler}.cs`, `src/Interop/MonitoringNativeMethods.cs`, `src/Test/{ResourceMonitoringTests,ProcessActionTests}.cs`. Parent alone updates csproj.

**Interfaces:** `IResourceMonitor.Sample(bool includeProcesses, bool includeTemperatures): ResourceSnapshot`, `Suspend(): void`, `Dispose(): void`; snapshot properties `CpuUsage`, `GpuUsage`, `CpuTemperature`, `GpuTemperature` (`MetricValue` with nullable `Value`, `Status`, `Detail`), `GpuName`, `Processes` (`IList<ProcessSnapshot>`). Process properties `Id`, `Name`, `CreationTime` (UTC Windows FILETIME `long`), `WorkingSetBytes`, `PrivateBytes`, `CpuUsage`, `GpuUsage`, `GpuMemoryBytes` (nullable readings). `ProcessActionService.Execute(ProcessSnapshot target, bool terminate, bool confirmed): ProcessActionResult` with typed outcome and diagnostic detail.

- [ ] Write tests for normalized deltas, first sample, rollover, unavailable values, counter aggregation, duplicate PIDs/names and provider failure, and observe red.
- [ ] Implement bulk/native process snapshots where feasible; release process/native resources; CPU delta uses elapsed sampling time and processor count. GPU counters are bulk PDH queries, cached only while demanded. Identify physical adapters and omit virtual ones.
- [ ] Read NVIDIA GPU thermal data using the existing driver; optionally read identified CPU/GPU sensors from the installed LibreHardwareMonitor provider. Never use generic ACPI temperature as CPU. Bound unsupported-provider retries.
- [ ] Write action tests for unconfirmed termination, self/critical/protected/identity-unreadable rejection, exited PID, PID reuse, access denied and the same-handle invariant; observe red.
- [ ] Implement native handle-based identity check, critical/protected checks and separate trim/terminate operations, fail closed on uncertain termination safety.
- [ ] Build backend subset with C# 4 and run owned tests; report API and measured real support to the parent.

### Task 3: Demand scheduling, settings and presentation models

**Files:** Create `src/Core/Monitoring/{MonitoringPolicy,OverlayPlacement,MonitorText}.cs`, `src/ViewModel/MonitoringViewModel.cs`, `src/Model/Monitoring/ProcessRow.cs`, `src/Test/MonitoringPresentationTests.cs`; modify `src/Core/Settings.cs`, `src/ViewModel/MainViewModel.cs`, `src/Service/NotificationService.cs`, `src/App.xaml.cs` only where necessary.

**Interfaces:** `MonitoringPolicy.GetDemand(bool mainVisible, bool processPage, bool overlayEnabled, bool temperaturesVisible)` defines requested work. `MonitoringViewModel.SetViewState(bool visible, bool processes)`, `RefreshNow()`, `Dispose()`; refresh/options persisted through existing settings. `ProcessRow` retains identity and display values; snapshot reconciliation updates rows in place and a collection view performs search/sort.

- [ ] Write failing tests for hidden demand, interval normalization, search/PID retention, unavailable sorting, off-screen positions, and cancellation of in-flight results.
- [ ] Add default-off overlay options, display flags, interval, screen, corner/custom position and mouse-through setting to settings without resetting old values; validate malformed persisted options on load.
- [ ] Implement a non-overlapping cancellable sampler and bounded dispatcher handoff; release detailed caches when hidden, retain only existing low-frequency memory monitoring.
- [ ] Remove property-getter process enumeration, cache the exclusion-picker list and refresh explicitly on demand; dispose enumerated Process objects.
- [ ] Avoid unchanged tray icon rerenders and full-root binding invalidations. Remove automatic self-trimming call sites involved in hide/refresh/monitoring; retain explicit legacy manual cleanup semantics.
- [ ] Run focused tests before proceeding.

### Task 4: Modern WPF shell and independent overlay

**Files:** Rewrite `src/View/Window/MainWindow.xaml` and its code-behind intentionally; create `src/View/Control/SettingsPanel.xaml{,.cs}`, `src/View/Window/MonitoringOverlay.xaml{,.cs}`, `src/Resources/MonitoringStyles.xaml`; adjust `src/App.xaml` and theme JSON colors; register all new project files.

**Interfaces:** Main window keeps the original `MainViewModel` DataContext and adds a named monitoring host bound to `MonitoringViewModel`. Settings panel consumes the original VM; overlay consumes the monitoring VM, is not owned by main, uses native no-activate/tool-window/mouse-through flags and persists drag coordinates.

- [ ] Build resource-overview / process-list / settings navigation with clear selected state, restrained dark/light palette, Segoe UI/system Chinese fallback, no blur/shadows/continuous animation.
- [ ] Preserve all original settings, cleaner action, compact mode, shortcuts, exclusions, tray options and help/reset/donation entrypoints in the settings panel.
- [ ] Use a virtualized fixed-header table with aligned columns, search, resource sorting, unavailable statuses, explicit trim and danger-colored confirmed termination.
- [ ] Add display-item switches, 1/2/5 refresh control, screen/corner selection, reposition mode and click-through default overlay. Clamp to available monitor and normalize DPI.
- [ ] Build and inspect real windows at default/minimum sizes, dark/light, compact, all pages and Chinese; record screenshots and control bounds. Verify confirmation cancellation does nothing.

### Task 5: Full verification, performance correction and delivery

**Files:** Complete `tools/verify-monitoring.ps1`, `tools/MonitoringHarness.cs`, `docs/monitoring-verification.md`, `docs/monitoring-guide.zh-CN.md`; output portable build and verification evidence under `dist/monitoring` and `build/monitoring-verification`.

- [ ] Build Debug, Release and isolated test build; run the complete safe NUnit suite excluding only existing Manual/Desktop categories, plus new tests.
- [ ] Verify actual sampling, all PID enumeration, unavailable values, owned-process trim/terminate and denial paths; overlay normal and borderless fullscreen mouse-through/no-focus behavior; demand shutoff.
- [ ] Run new Release benchmarks (three 30/60 trials for tray/main/process/overlay), >=200-record dispatcher probe and 10-minute memory-growth run. Compare exact approved thresholds; investigate and fix concrete regressions before retesting.
- [ ] Optionally request one bounded read-only review of native safety/lifecycle changes; only fix verified in-scope defects, no broad re-review.
- [ ] Package only EXE and required runtime resources, preserve license/attribution, produce SHA-256 and size comparison. Do not publish or replace the user's running binary.
- [ ] Write an eight-row acceptance report with observed commands/data/screenshots and hardware limitations, mark each met/unmet honestly.

## Plan self-check

All eight approved criteria map to Tasks 1–5; exact public types are declared once in Task 2. Lifecycle/off-screen/PID-reuse tests are owned by Tasks 2–3 and real-window checks by Tasks 4–5. This plan changes only the requested tool; sensor capability and exclusive-fullscreen limits remain those of the approved spec.
