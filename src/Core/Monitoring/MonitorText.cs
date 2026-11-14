using System;
using System.Collections.Generic;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    // Existing languages stay intact; new labels use English fallback outside Chinese.
    public sealed class MonitorText : ObservableObject
    {
        private static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
        {
            {"Overview", new[]{"资源概览", "Overview"}}, {"Processes", new[]{"进程", "Processes"}}, {"Settings", new[]{"设置", "Settings"}},
            {"CpuColumn", new[]{"CPU %", "CPU %"}}, {"GpuColumn", new[]{"GPU %", "GPU %"}}, {"FontSize", new[]{"字号", "Font size"}},
            {"DragHint", new[]{"拖动此区域定位；松开后恢复鼠标穿透。", "Drag this panel; release to restore mouse-through."}},
            {"DragShort", new[]{"拖动定位", "Drag to position"}},
            {"Layout", new[]{"排列方式", "Layout"}}, {"Horizontal", new[]{"横向监控条", "Horizontal strip"}}, {"Vertical", new[]{"竖向监控条", "Vertical strip"}},
            {"MemoryShort", new[]{"内存", "RAM"}},
            {"OnlyMonitor", new[]{"仅显示监控条", "Show monitor only"}},
            {"MonitorOnlyNote", new[]{"隐藏主窗口，仅保留监控条；点击托盘图标可重新打开设置。", "Hide the main window and keep the strip. Click the tray icon to reopen settings."}},
            {"Trimmed", new[]{"已释放工作集；程序后续可能重新载入内存页", "Working set released; pages may be loaded again"}},
            {"Terminated", new[]{"已请求结束进程", "Process termination requested"}},
            {"InvalidTarget", new[]{"无法确认进程身份，已拒绝操作", "Cannot verify process identity; refused"}},
            {"Failed", new[]{"操作失败", "Action failed"}},
            {"Cpu", new[]{"CPU 使用率", "CPU usage"}}, {"Gpu", new[]{"GPU 使用率", "GPU usage"}}, {"Memory", new[]{"物理内存", "Physical memory"}},
            {"CpuTemperature", new[]{"CPU 温度", "CPU temperature"}}, {"GpuTemperature", new[]{"GPU 温度", "GPU temperature"}},
            {"TemperatureShort", new[]{"温度", "Temp"}}, {"CpuScope", new[]{"全机占用", "Whole-machine usage"}},
            {"Details", new[]{"采集详情与说明", "Sources and details"}},
            {"ResourceShortNote", new[]{"按需采样 · 后台暂停", "On-demand sampling · Paused in background"}},
            {"Fps", new[]{"游戏 FPS（提交）", "Game FPS (presents)"}}, {"FpsShort", new[]{"FPS", "FPS"}},
            {"FpsTarget", new[]{"FPS 监控目标", "FPS target"}}, {"FpsForeground", new[]{"自动跟随前台", "Follow foreground"}},
            {"FpsManual", new[]{"手动选择进程", "Selected process"}}, {"FpsChoose", new[]{"选择游戏 / 进程…", "Choose game / process…"}},
            {"FpsTrackSelected", new[]{"选为 FPS 目标", "Use as FPS target"}}, {"FpsClearTarget", new[]{"清除目标", "Clear target"}},
            {"FpsNote", new[]{"FPS 统计选定 DirectX 进程成功提交 Present 的速率，不是显示器 Hz、界面刷新或补帧后的上屏帧率。默认关闭；不注入游戏、不安装驱动。无有效事件时显示 —，不保证 OpenGL/Vulkan、独占全屏或反作弊应用。", "FPS measures successful DirectX Present submissions by the selected process, not display Hz, UI refresh or displayed/generated frames. Off by default; no injection or driver installation. — means no valid events. OpenGL/Vulkan, exclusive fullscreen and anti-cheat applications are not guaranteed."}},
            {"FpsSelectionNote", new[]{"手动目标请在进程页选中游戏，再点“选为 FPS 目标”。只高亮行不会切换目标；选目标不会自动开启 FPS，程序重启后需重新选择。", "For a manual target, select the game on Processes and click “Use as FPS target”. Highlighting a row alone does not switch targets. Choosing a target does not enable FPS; select again after restarting the tool."}},
            {"FpsTargetSelected", new[]{"FPS 目标已选择", "FPS target selected"}}, {"FpsTargetSelectedOff", new[]{"目标已选择；开启 FPS 显示后开始采集", "Target selected; enable FPS display to start capture"}},
            {"FpsDisabled", new[]{"FPS 未开启或已暂停", "FPS off or paused"}}, {"FpsChooseTarget", new[]{"请选择游戏进程或切换到游戏窗口", "Select a game process or focus its window"}},
            {"FpsWarmingUp", new[]{"等待有效出帧事件", "Waiting for valid presents"}}, {"FpsAvailable", new[]{"DirectX Present 提交速率", "DirectX Present rate"}},
            {"FpsNoFrames", new[]{"近期没有有效出帧事件", "No recent valid presents"}}, {"FpsAccessDenied", new[]{"FPS 采集权限不足", "FPS capture access denied"}},
            {"FpsUnsupported", new[]{"系统、接口或事件格式不支持", "Unsupported system, API or event format"}}, {"FpsTargetExited", new[]{"目标进程已退出，请重新选择", "Target exited; select again"}},
            {"FpsIdentityChanged", new[]{"进程身份已变化，已停止采集", "Process identity changed; capture stopped"}}, {"FpsError", new[]{"FPS 采集失败", "FPS capture failed"}},
            {"FpsEventLoss", new[]{"出帧事件丢失，当前读数无效", "Present events lost; reading invalid"}},
            {"ResourceNote", new[]{"按需监控；关闭悬浮并隐藏窗口后，仅保留托盘与自动清理所需的内存检查。", "On-demand monitoring. Hiding the window with the overlay off retains only minimal tray / auto-clean memory checks."}},
            {"OptimizationElapsed", new[]{"已用 {0}", "Elapsed {0}"}},
            {"Normalized", new[]{"CPU 按全机归一化（全机总计 100%）；GPU 显示最忙引擎。", "CPU normalized to the whole machine (100% total); GPU shows busiest engine."}},
            {"TemperatureNote", new[]{"温度仅来自可识别的硬件传感器或显卡驱动。缺少来源时明确不可用，不使用 ACPI 区域温度冒充 CPU。", "Temperatures require an identified sensor or graphics driver. ACPI zone readings are not labelled CPU temperature."}},
            {"SensorStatus", new[]{"通用温度：{0} · 驱动：{1}", "Universal temperature: {0} · Driver: {1}"}},
            {"InstallSensorDriver", new[]{"安装 PawnIO 驱动…", "Install PawnIO driver…"}},
            {"SelectSensorInstaller", new[]{"选择离线安装包…", "Choose offline package…"}},
            {"SensorInstallerFileFilter", new[]{"官方 PawnIO 安装包 (*.exe)|*.exe", "Official PawnIO installer (*.exe)|*.exe"}},
            {"CancelSensorPreparation", new[]{"取消准备", "Cancel preparation"}},
            {"SensorPreparingInstaller", new[]{"准备并校验官方安装包", "Preparing and verifying official package"}},
            {"SensorInstallerSourceNote", new[]{"CPU 温度的通用采集需要 PawnIO；开启时会检查并提示，不会自动安装。确认后才下载，也可选择官方 2.2.0 离线包。日常监控无需联网。", "Universal CPU temperature reading needs PawnIO. Enabling it checks and prompts, never installs automatically. Download only after confirmation, or choose an official 2.2.0 offline package. Monitoring needs no network access."}},
            {"DownloadSensorInstallerNote", new[]{"若没有已校验的缓存，确认后将从官方 GitHub 固定版本下载约 3.3 MiB 安装包。下载、长度、SHA256 和官方签名校验成功后才会启动安装；可在准备阶段取消。", "If no verified cached package exists, confirmation downloads about 3.3 MiB from the pinned official GitHub release. Installation starts only after length, SHA256 and official signature verification. Preparation can be cancelled."}},
            {"LocalSensorInstallerNote", new[]{"将复制并校验你选择的官方 PawnIO 2.2.0 离线安装包，不会下载或修改原文件。长度、SHA256 或官方签名不匹配时会拒绝安装。", "The selected official PawnIO 2.2.0 offline package will be copied and verified, without downloading or changing the original. An incorrect length, SHA256 or official signature blocks installation."}},
            {"ConfirmSensorDriverTitle", new[]{"确认安装官方 PawnIO", "Confirm official PawnIO installation"}},
            {"ConfirmSensorDriver", new[]{"是否启动官方签名版 PawnIO 2.2.0 安装器？\n\n这是系统范围、可被其他软件共享的硬件访问驱动，本程序仅用它读取 CPU/GPU 温度，不控制风扇、功耗或性能模式。安装需要管理员权限，可能需要你自行重启。\n\n如果已有 PawnIO，本程序会保留现有驱动，不覆盖、不降级。不会关闭 Secure Boot、内存完整性或驱动阻止列表。取消不会安装驱动，监控将继续使用可用的备用来源。", "Start the official signed PawnIO 2.2.0 installer?\n\nThis is a system-wide hardware-access driver shared with other software. This app uses it only to read CPU/GPU temperatures, not to control fans, power or performance modes. Administrator permission is required; you may need to restart manually.\n\nExisting PawnIO drivers are kept, never replaced or downgraded. Secure Boot, memory integrity and driver blocklists remain enabled. Cancel installs nothing; monitoring continues with available fallback sources."}},
            {"TemperatureDriverTitle", new[]{"CPU 温度驱动", "CPU temperature driver"}},
            {"TemperatureDriverAcknowledge", new[]{"知道了", "OK"}},
            {"TemperatureDriverNeeded", new[]{"已开启 CPU 温度显示，但本机尚未安装 PawnIO。没有可用的原生 CPU 传感器时，通用采集需要这个驱动。你可以现在安装，或取消并继续使用可用的备用来源。", "CPU temperature display is enabled, but PawnIO is not installed. Universal reading needs this driver when no native CPU sensor is available. You can install now or cancel and keep available fallback sources."}},
            {"TemperatureDriverAdministrator", new[]{"CPU 温度驱动访问被拒绝。请以管理员身份重新启动 WinMemoryCleaner 后重试，不要因此重复安装驱动。\n\n本程序不会修改驱动权限或关闭 Windows 安全功能。", "Access to the CPU temperature driver was denied. Restart WinMemoryCleaner as administrator and retry; do not reinstall the driver for this error.\n\nThis app does not change driver permissions or disable Windows security features."}},
            {"TemperatureDriverNotReady", new[]{"已有 PawnIO，但驱动设备尚未就绪。请稍后重试；如果官方安装器明确要求重启，请自行重启后再试。\n\n本程序不会重复安装、替换驱动或自动重启。", "PawnIO is installed, but its device is not ready. Retry later; if the official installer explicitly requested a restart, restart manually first.\n\nThis app will not reinstall, replace the driver or restart Windows automatically."}},
            {"TemperatureDriverUnverified", new[]{"检测到已有 PawnIO，但未能验证其签名、固定版本或设备状态。支持的驱动为官方 x64 标准版 2.1.0 和 2.2.0。\n\n请保留现有共享驱动并核对官方版本；本程序不会自动覆盖或降级。", "Existing PawnIO could not be verified against its signature, pinned version or device state. Supported drivers are the official x64 standard builds 2.1.0 and 2.2.0.\n\nKeep the shared driver and check its official version; this app will not replace or downgrade it automatically."}},
            {"TemperatureDriverInspectionFailed", new[]{"暂时无法确认 PawnIO 驱动状态，本次不会启动安装。请稍后重试，或以管理员身份重新启动 WinMemoryCleaner。可用的备用监控来源不受影响。", "PawnIO driver state could not be confirmed, so installation will not start. Retry later or restart WinMemoryCleaner as administrator. Available fallback monitoring sources remain usable."}},
            {"SensorDisabled", new[]{"未请求", "Not requested"}}, {"SensorNotChecked", new[]{"未检查", "Not checked"}},
            {"SensorPreparing", new[]{"准备组件", "Preparing"}}, {"SensorRunning", new[]{"运行中", "Running"}},
            {"SensorNotNeeded", new[]{"无需通用后端", "Not needed"}}, {"SensorWaitingForDriver", new[]{"等待可信 CPU 驱动", "Waiting for trusted CPU driver"}},
            {"SensorError", new[]{"失败，等待重试", "Failed; waiting to retry"}}, {"SensorReady", new[]{"官方驱动就绪", "Official driver ready"}},
            {"SensorNotInstalled", new[]{"未安装", "Not installed"}}, {"SensorUnverifiedDriver", new[]{"已保留现有驱动，无法验证", "Existing driver kept; unverified"}},
            {"SensorKeepExistingDriver", new[]{"已保留现有共享驱动", "Existing shared driver kept"}}, {"SensorDriverNotReady", new[]{"已安装但设备未就绪", "Installed; device not ready"}},
            {"SensorAccessDenied", new[]{"访问被拒绝", "Access denied"}}, {"SensorInspectionFailed", new[]{"无法确认驱动状态", "Cannot verify driver state"}},
            {"SensorInstalling", new[]{"安装器运行中", "Installer running"}}, {"SensorNotStarted", new[]{"尚未启动安装", "Installation not started"}},
            {"SensorRestartRequired", new[]{"安装器要求手动重启", "Installer requests a manual restart"}}, {"SensorCancelled", new[]{"安装已取消", "Installation cancelled"}},
            {"SensorInstalledNotReady", new[]{"安装器已退出，驱动未验证就绪", "Installer exited; driver not verified ready"}},
            {"SensorSignatureInvalid", new[]{"安装器签名校验失败", "Installer signature rejected"}}, {"SensorInstallationBusy", new[]{"已有安装操作", "Another installation is active"}},
            {"SensorInstallFailed", new[]{"安装失败", "Installation failed"}},
            {"Refresh", new[]{"刷新间隔（秒）", "Refresh interval (seconds)"}}, {"RefreshNow", new[]{"立即刷新", "Refresh now"}},
            {"Search", new[]{"搜索名称或 PID", "Search name or PID"}}, {"Name", new[]{"进程名称", "Process name"}}, {"Pid", new[]{"PID", "PID"}},
            {"ClearSearch", new[]{"清空", "Clear"}}, {"ClearSearchName", new[]{"清空进程搜索", "Clear process search"}},
            {"ProcessMatches", new[]{"匹配：{0} / 总数：{1}", "Matches: {0} / Total: {1}"}},
            {"ProcessLoading", new[]{"正在加载进程…", "Loading processes…"}},
            {"ProcessEmpty", new[]{"没有可显示的进程", "No processes available"}},
            {"ProcessNoMatches", new[]{"没有匹配的进程", "No matching processes"}},
            {"ProcessFiltering", new[]{"正在筛选进程…", "Filtering processes…"}},
            {"ProcessFailed", new[]{"进程列表读取失败；操作已暂停", "Could not read the process list; actions paused"}},
            {"ProcessStale", new[]{"显示上次成功数据；操作已暂停", "Showing last successful data; actions paused"}},
            {"ProcessTargetUnavailable", new[]{"当前进程数据或筛选已变化；操作已拒绝，请重新选择", "Process data or filtering changed; action refused. Select again."}},
            {"WorkingSet", new[]{"工作集内存", "Working set"}}, {"Private", new[]{"私有内存", "Private memory"}}, {"GpuMemory", new[]{"专用显存", "Dedicated VRAM"}},
            {"ProcessNote", new[]{"— 表示采样预热、权限受限、进程已退出或驱动不支持；不是零占用。单击列标题排序。", "— means warming up, restricted, exited or unsupported, not zero usage. Click column headers to sort."}},
            {"Trim", new[]{"释放选定内存", "Release selected memory"}}, {"Terminate", new[]{"结束选定进程…", "End selected process…"}},
            {"TrimNote", new[]{"释放工作集不会保证降低 CPU/GPU 占用，也不会保证提升帧率。", "Releasing a working set does not guarantee lower CPU/GPU usage or higher frame rates."}},
            {"NoSelection", new[]{"请先选择一个进程。", "Select a process first."}}, {"ConfirmTitle", new[]{"确认结束进程", "Confirm process termination"}},
            {"Confirm", new[]{"结束 {0}（PID {1}）？\n未保存的数据可能丢失。该操作不会卸载程序或删除文件。", "End {0} (PID {1})?\nUnsaved data may be lost. This does not uninstall software or delete files."}},
            {"ActionFailed", new[]{"操作已拒绝或失败", "Action refused or failed"}}, {"ActionSucceeded", new[]{"操作成功", "Action succeeded"}},
            {"Overlay", new[]{"悬浮监控", "Monitoring overlay"}}, {"OverlayEnabled", new[]{"启用悬浮监控（默认关闭）", "Enable overlay (off by default)"}},
            {"ClickThrough", new[]{"鼠标穿透（不抢焦点）", "Mouse-through (no focus stealing)"}}, {"Display", new[]{"显示屏", "Display"}}, {"Position", new[]{"位置", "Position"}},
            {"PrimaryDisplay", new[]{"主显示器（默认）", "Primary display (default)"}},
            {"TopLeft", new[]{"左上角", "Top left"}}, {"TopRight", new[]{"右上角", "Top right"}}, {"BottomLeft", new[]{"左下角", "Bottom left"}}, {"BottomRight", new[]{"右下角", "Bottom right"}}, {"Custom", new[]{"自定义拖动位置", "Custom dragged position"}},
            {"Drag", new[]{"拖动定位（完成后恢复穿透）", "Drag to position (then restore mouse-through)"}},
            {"OverlayNote", new[]{"支持普通桌面与无边框全屏窗口；不保证独占全屏、受保护桌面或特殊反作弊应用。监控条无需注入；部分通用温度来源需要经确认安装的驱动。", "Supports desktop and borderless fullscreen; exclusive fullscreen, protected desktops and anti-cheat environments are not guaranteed. The strip uses no injection; some universal temperature sources require a confirmed driver installation."}},
            {"Items", new[]{"悬浮显示项", "Overlay items"}}, {"Appearance", new[]{"外观与既有设置", "Appearance and existing settings"}},
            {"Dark", new[]{"深色", "Dark"}}, {"Light", new[]{"浅色", "Light"}}, {"Help", new[]{"帮助 / 社区", "Help / community"}}, {"Compact", new[]{"紧凑模式", "Compact mode"}},
            {"Unavailable", new[]{"不可用", "Unavailable"}}, {"WarmingUp", new[]{"等待下一次采样", "Waiting for next sample"}}, {"Unsupported", new[]{"系统、驱动或传感器不支持", "Unsupported system, driver or sensor"}},
            {"AccessDenied", new[]{"权限受限", "Access denied"}}, {"Error", new[]{"读取失败", "Read failed"}}, {"Disabled", new[]{"未启用采样", "Sampling disabled"}},
            {"NoCpuSensor", new[]{"未发现已安装且可识别归属的 CPU 传感器来源", "No installed, identified CPU sensor source"}},
            {"ProcessCount", new[]{"可枚举进程：{0}", "Enumerable processes: {0}"}},
            {"Protected", new[]{"关键 / 受保护进程或工具自身，已拒绝操作", "Critical / protected process or this tool: refused"}},
            {"IdentityChanged", new[]{"PID 创建时间已变化；请刷新后重新选择", "PID creation time changed; refresh and select again"}},
            {"Exited", new[]{"进程已退出；请刷新列表", "Process has exited; refresh the list"}}, {"NotConfirmed", new[]{"未确认结束，未执行操作", "Termination not confirmed; no action taken"}}
        };
        public string this[string key]
        {
            get
            {
                string[] value;
                if (!Text.TryGetValue(key, out value)) return key;
                return value[Localizer.Culture != null && Localizer.Culture.TwoLetterISOLanguageName == "zh" ? 0 : 1];
            }
        }
        public void Refresh() { RaisePropertyChanged("Item[]"); }
    }
}
#pragma warning restore 1591
