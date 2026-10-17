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
            {"ResourceNote", new[]{"按需监控；关闭悬浮并隐藏窗口后，仅保留托盘与自动清理所需的内存检查。", "On-demand monitoring. Hiding the window with the overlay off retains only minimal tray / auto-clean memory checks."}},
            {"Normalized", new[]{"CPU 按全机归一化（全机总计 100%）；GPU 显示最忙引擎。", "CPU normalized to the whole machine (100% total); GPU shows busiest engine."}},
            {"TemperatureNote", new[]{"温度仅来自可识别的硬件传感器或显卡驱动。缺少来源时明确不可用，不使用 ACPI 区域温度冒充 CPU。", "Temperatures require an identified sensor or graphics driver. ACPI zone readings are not labelled CPU temperature."}},
            {"Refresh", new[]{"刷新间隔（秒）", "Refresh interval (seconds)"}}, {"RefreshNow", new[]{"立即刷新", "Refresh now"}},
            {"Search", new[]{"搜索名称或 PID", "Search name or PID"}}, {"Name", new[]{"进程名称", "Process name"}}, {"Pid", new[]{"PID", "PID"}},
            {"WorkingSet", new[]{"工作集内存", "Working set"}}, {"Private", new[]{"私有内存", "Private memory"}}, {"GpuMemory", new[]{"专用显存", "Dedicated VRAM"}},
            {"ProcessNote", new[]{"— 表示采样预热、权限受限、进程已退出或驱动不支持；不是零占用。单击列标题排序。", "— means warming up, restricted, exited or unsupported, not zero usage. Click column headers to sort."}},
            {"Trim", new[]{"释放选定内存", "Release selected memory"}}, {"Terminate", new[]{"结束选定进程…", "End selected process…"}},
            {"TrimNote", new[]{"释放工作集不会保证降低 CPU/GPU 占用，也不会保证提升帧率。", "Releasing a working set does not guarantee lower CPU/GPU usage or higher frame rates."}},
            {"NoSelection", new[]{"请先选择一个进程。", "Select a process first."}}, {"ConfirmTitle", new[]{"确认结束进程", "Confirm process termination"}},
            {"Confirm", new[]{"结束 {0}（PID {1}）？\n未保存的数据可能丢失。该操作不会卸载程序或删除文件。", "End {0} (PID {1})?\nUnsaved data may be lost. This does not uninstall software or delete files."}},
            {"ActionFailed", new[]{"操作已拒绝或失败", "Action refused or failed"}}, {"ActionSucceeded", new[]{"操作成功", "Action succeeded"}},
            {"Overlay", new[]{"悬浮监控", "Monitoring overlay"}}, {"OverlayEnabled", new[]{"启用悬浮监控（默认关闭）", "Enable overlay (off by default)"}},
            {"ClickThrough", new[]{"鼠标穿透（不抢焦点）", "Mouse-through (no focus stealing)"}}, {"Display", new[]{"显示屏", "Display"}}, {"Position", new[]{"位置", "Position"}},
            {"TopLeft", new[]{"左上角", "Top left"}}, {"TopRight", new[]{"右上角", "Top right"}}, {"BottomLeft", new[]{"左下角", "Bottom left"}}, {"BottomRight", new[]{"右下角", "Bottom right"}}, {"Custom", new[]{"自定义拖动位置", "Custom dragged position"}},
            {"Drag", new[]{"拖动定位（完成后恢复穿透）", "Drag to position (then restore mouse-through)"}},
            {"OverlayNote", new[]{"支持普通桌面与无边框全屏窗口；不保证独占全屏、受保护桌面或特殊反作弊应用。无需注入或额外驱动。", "Supports desktop and borderless fullscreen; exclusive fullscreen, protected desktops and anti-cheat environments are not guaranteed. No injection or added driver."}},
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
