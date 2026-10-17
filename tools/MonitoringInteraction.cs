// Real-window acceptance checks. Reflection keeps the same harness buildable against
// the pre-monitoring baseline. Input is guarded to hit ONLY windows created here.
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinMemoryCleaner;
using Forms = System.Windows.Forms;

internal static class MonitoringInteraction
{
    private static MainWindow _window;
    private static object _monitor;
    private static string _output;
    private static readonly List<object> Checks = new List<object>();
    private static readonly List<object> Events = new List<object>();
    private static int _clicks;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    internal static void Run(MainWindow window, string output)
    {
        _window = window; _output = output;
        string failure = null;
        try
        {
            _monitor = Get(window, "Monitoring");
            Check("default interval 2s / overlay off", (int)Get(_monitor, "RefreshSeconds") == 2 && !(bool)Get(_monitor, "OverlayEnabled"));
            Check("refresh choices exactly 1,2,5", string.Join(",", Array.ConvertAll((int[])Get(_monitor, "RefreshChoices"), delegate(int i) { return i.ToString(CultureInfo.InvariantCulture); })) == "1,2,5");
            Until(delegate { return ((string)Get(_monitor, "CpuText")).EndsWith("%", StringComparison.Ordinal); }, 12000, "real overview CPU reading");
            Capture("overview-default");
            Select(1);
            Until(delegate { return (int)Get(_monitor, "ProcessCount") >= 20 && (int)Get(_monitor, "ProcessSampleCount") >= 2; }, 12000, "real process rows");
            CheckProcessView();
            Capture("process-default");
            CheckCancellation();
            CheckLayouts();
            CheckDemand();
            OverlayScenario(false);
            OverlayScenario(true);
            CheckPlacementAndPersistence();
            Set(_monitor, "OverlayEnabled", false); _window.Hide();
            Pump(600); var stopped = Counters(); Pump(5000);
            Check("final hidden/off stops all detail counters", EqualCounters(stopped, Counters()));
            Check("minimum memory monitoring remains 5s", (int)Get(_window.DataContext, "MemoryRefreshSeconds") == 5);
        }
        catch (Exception ex) { failure = ex.ToString(); throw; }
        finally
        {
            var summary = new Dictionary<string, object> { { "mode", "interaction" }, { "passed", failure == null }, { "failure", failure }, { "checks", Checks }, { "events", Events } };
            File.WriteAllText(Path.Combine(_output, "summary.json"), new JavaScriptSerializer().Serialize(summary));
        }
    }

    private static object Get(object instance, string property)
    {
        var info = instance.GetType().GetProperty(property);
        if (info == null) throw new InvalidOperationException("Missing production property: " + property);
        return info.GetValue(instance, null);
    }
    private static void Set(object instance, string property, object value) { instance.GetType().GetProperty(property).SetValue(instance, value, null); }
    private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name).Invoke(instance, args); }
    private static void Select(int page) { Call(_window, "SelectPage", page); Pump(180); _window.UpdateLayout(); }
    private static void Check(string name, bool passed)
    {
        Checks.Add(new Dictionary<string, object> { { "name", name }, { "passed", passed } });
        File.WriteAllText(Path.Combine(_output, "checks.json"), new JavaScriptSerializer().Serialize(Checks));
        if (!passed) throw new InvalidOperationException("Acceptance check failed: " + name);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Until(Func<bool> predicate, int timeout, string name)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate() && clock.ElapsedMilliseconds < timeout) Pump(40);
        Check(name, predicate());
    }
    private static IntPtr Handle(Window window) { return new WindowInteropHelper(window).Handle; }
    private static NativeRect Bounds(Window window)
    {
        NativeRect bounds;
        if (!GetWindowRect(Handle(window), out bounds)) throw new InvalidOperationException("Cannot read owned window bounds.");
        return bounds;
    }
    private static Window Overlay()
    {
        foreach (Window window in Application.Current.Windows) if (window.GetType().Name == "MonitoringOverlay") return window;
        throw new InvalidOperationException("Enabled overlay window is absent.");
    }
    private static void Capture(string name)
    {
        Pump(140); _window.UpdateLayout();
        MonitoringHarness.CaptureWindow(_window, name + ".png"); MonitoringHarness.RecordLayout();
        File.Copy(Path.Combine(_output, "layout.csv"), Path.Combine(_output, name + "-layout.csv"), true);
    }
    private static void CheckInside(string name)
    {
        var control = (FrameworkElement)_window.FindName(name);
        Point p = control.TranslatePoint(new Point(), _window);
        Check(name + " visible inside " + _window.ActualWidth + "x" + _window.ActualHeight,
            control.IsVisible && control.ActualWidth > 1 && control.ActualHeight > 1 && p.X >= 0 && p.Y >= 0 && p.X + control.ActualWidth <= _window.ActualWidth + 1 && p.Y + control.ActualHeight <= _window.ActualHeight + 1);
    }
    private static List<object> VisibleRows()
    {
        var rows = new List<object>(); foreach (object row in (IEnumerable)Get(_monitor, "ProcessesView")) rows.Add(row); return rows;
    }
    private static void CheckProcessView()
    {
        var rows = VisibleRows(); var ids = new HashSet<int>(); int duplicateNames = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) { Check("unique PID " + Get(row, "Id"), ids.Add((int)Get(row, "Id"))); if (!names.Add((string)Get(row, "Name"))) duplicateNames++; }
        Events.Add(new Dictionary<string, object> { { "processRows", rows.Count }, { "duplicateNameRows", duplicateNames } });
        var search = (TextBox)_window.FindName("ProcessSearch");
        string query = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
        search.Text = query; search.GetBindingExpression(TextBox.TextProperty).UpdateSource();
        var filtered = VisibleRows();
        bool selfFound = false, allMatch = filtered.Count > 0;
        foreach (var row in filtered)
        {
            string id = Get(row, "Id").ToString(), name = (string)Get(row, "Name");
            if (id == query) selfFound = true;
            if (!id.Contains(query) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) allMatch = false;
        }
        Check("UI PID search keeps self and only matching rows", selfFound && allMatch);
        search.Text = "monitoringharness"; search.GetBindingExpression(TextBox.TextProperty).UpdateSource();
        Check("UI case-insensitive name search", VisibleRows().Count > 0);
        search.Text = string.Empty; search.GetBindingExpression(TextBox.TextProperty).UpdateSource();
        foreach (string field in new[] { "WorkingSetBytes", "CpuUsage", "GpuUsage", "GpuMemoryBytes" })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool descending = Call(_monitor, "Sort", field).ToString() == "Descending";
                double? previous = null; bool missing = false, sorted = true;
                foreach (var row in VisibleRows())
                {
                    object value = Get(Get(row, "Snapshot"), field);
                    if (value == null) { missing = true; continue; }
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (missing || (previous.HasValue && (descending ? number > previous.Value : number < previous.Value))) sorted = false;
                    previous = number;
                }
                Check("real rows sorted " + field + (descending ? " descending" : " ascending") + ", unavailable last", sorted);
            }
        }
    }
    private static void CheckCancellation()
    {
        object self = null; foreach (var row in VisibleRows()) if ((int)Get(row, "Id") == Process.GetCurrentProcess().Id) self = row;
        Check("self row available for cancellation-only dialog", self != null);
        Set(_monitor, "SelectedProcess", self); string prompt = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += delegate
        {
            foreach (Window dialog in Application.Current.Windows)
            {
                if (!(dialog is MessageDialog)) continue;
                timer.Stop(); prompt = (string)Get(dialog.DataContext, "Message");
                MonitoringHarness.CaptureWindow(dialog, "termination-cancel-dialog.png");
                dialog.DialogResult = false; break;
            }
        };
        timer.Start();
        try { ((Button)_window.FindName("TerminateSelected")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { timer.Stop(); }
        Check("confirmation includes selected name/PID and unsaved-data warning", prompt != null && prompt.Contains((string)Get(self, "Name")) && prompt.Contains("PID " + Get(self, "Id")) && prompt.Contains("未保存的数据可能丢失"));
        Check("real dialog cancellation performs no action", ((string)Get(_monitor, "ActionStatus")).Contains("未确认"));
    }
    private static void CheckLayouts()
    {
        _window.Width = _window.MinWidth; _window.Height = _window.MinHeight; Pump(200);
        Select(0); CheckInside("Optimize"); CheckInside("OverviewNav"); Capture("overview-minimum");
        Select(1); CheckInside("ProcessGrid"); CheckInside("TrimSelected"); CheckInside("TerminateSelected"); Capture("process-minimum");
        Select(2); CheckInside("SettingsNav"); CheckInside("Optimize"); Capture("settings-minimum");
        _window.Width = 960; _window.Height = 700; Pump(200); Capture("settings-default");
        var settings = (ScrollViewer)_window.FindName("SettingsPage"); settings.ScrollToEnd(); Pump(200); Capture("settings-legacy-bottom"); settings.ScrollToTop();
        Select(0); ThemeManager.Theme = Enums.Theme.Light; Capture("overview-light"); ThemeManager.Theme = Enums.Theme.Dark;
        Select(1); Localizer.Language = new Language(CultureInfo.GetCultureInfo("en")); Pump(100);
        Check("live English process-column translation", (string)((DataGrid)_window.FindName("ProcessGrid")).Columns[0].Header == "Process name"); Capture("process-english");
        Localizer.Language = new Language(CultureInfo.GetCultureInfo("zh-Hans"));
        ((MainViewModel)_window.DataContext).CompactMode = true; Pump(200); CheckInside("Optimize"); Check("compact window below 350 DIP", _window.ActualHeight < 350); Capture("compact");
        ((MainViewModel)_window.DataContext).CompactMode = false; Select(0);
    }
    private static int[] Counters() { return new[] { (int)Get(_monitor, "SampleCount"), (int)Get(_monitor, "ProcessSampleCount"), (int)Get(_monitor, "TemperatureSampleCount") }; }
    private static bool EqualCounters(int[] a, int[] b) { return a[0] == b[0] && a[1] == b[1] && a[2] == b[2]; }
    private static void CheckDemand()
    {
        Set(_monitor, "OverlayEnabled", false); _window.Hide(); Pump(800);
        var before = Counters(); Pump(5000); var after = Counters();
        Events.Add(new Dictionary<string, object> { { "state", "hidden, overlay off, 5 seconds" }, { "before", before }, { "after", after } });
        Check("hidden/off stops CPU GPU process and temperature sampling", EqualCounters(before, after));
        Check("hidden releases all process rows", (int)Get(_monitor, "ProcessCount") == 0);
        _window.Show(); Select(0);
        Until(delegate { return (int)Get(_monitor, "SampleCount") >= after[0] + 2 && ((string)Get(_monitor, "CpuText")).EndsWith("%", StringComparison.Ordinal); }, 10000, "overview resumes real sampling");
        Events.Add(new Dictionary<string, object> { { "state", "resumed overview" }, { "counters", Counters() }, { "gpu", Get(_monitor, "GpuText") }, { "gpuDetail", Get(_monitor, "GpuDetail") } });
        _window.Hide();
    }

    private static void OverlayScenario(bool fullscreen)
    {
        string scenario = fullscreen ? "borderless-fullscreen" : "ordinary";
        var screen = Forms.Screen.PrimaryScreen;
        var surface = new Grid { Background = new SolidColorBrush(Color.FromRgb(40, 54, 69)) };
        surface.Children.Add(new TextBlock { Text = "WMC 验证专用窗口\n" + scenario + "\n点击应穿透悬浮层抵达此窗口；用户程序不参与操作", Foreground = Brushes.White, FontSize = 25, Margin = new Thickness(45, 230, 20, 20), TextWrapping = TextWrapping.Wrap });
        var target = new Window { Title = "WMC verification-only input target", Width = 1000, Height = 650, Content = surface, WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow, ResizeMode = ResizeMode.NoResize };
        surface.MouseLeftButtonDown += delegate { _clicks++; };
        NativePoint cursor; GetCursorPos(out cursor);
        try
        {
            Set(_monitor, "OverlayEnabled", false); target.Show();
            // Keep verification-owned windows in the central work area: screen corners are where
            // users pin third-party always-on-top OSD/widget windows, which would sit above the
            // non-topmost owned target and break the position-independent hit-test assertions.
            if (fullscreen) SetWindowPos(Handle(target), IntPtr.Zero, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height, 0x14);
            else SetWindowPos(Handle(target), IntPtr.Zero, screen.WorkingArea.X + 320, screen.WorkingArea.Y + 260, 0, 0, 0x15);
            target.Activate(); SetForegroundWindow(Handle(target)); Pump(250);
            if (GetForegroundWindow() != Handle(target)) AcquireOwnedForeground(target);
            Check(scenario + " test target owns foreground", GetForegroundWindow() == Handle(target));
            var rect = Bounds(target);
            Call(_monitor, "SavePosition", screen.DeviceName, (double)rect.Left + (fullscreen ? 400 : 50), (double)rect.Top + (fullscreen ? 300 : 90));
            Set(_monitor, "ShowCpuTemperature", true); Set(_monitor, "ShowGpuTemperature", true);
            Set(_monitor, "ClickThrough", true); Set(_monitor, "OverlayEnabled", true); Pump(450);
            var overlay = Overlay(); int style = GetWindowLong(Handle(overlay), -20);
            Check(scenario + " visible independent topmost overlay", overlay.IsVisible && overlay.Owner == null && overlay.Topmost && IsAbove(Handle(overlay), Handle(target)));
            Check(scenario + " native noactivate/toolwindow/transparent styles", (style & 0x080000A0) == 0x080000A0);
            Check(scenario + " enabling overlay does not steal focus", GetForegroundWindow() == Handle(target));
            var bounds = Bounds(overlay); var point = new NativePoint { X = bounds.Left + 28, Y = bounds.Top + (bounds.Bottom - bounds.Top) / 2 };
            Check(scenario + " hit-test reaches underlying owned target", GetAncestor(WindowFromPoint(point), 2) == Handle(target));
            int before = _clicks;
            GuardedClick(target, point); Pump(180);
            Check(scenario + " real mouse click reaches target without focus change", _clicks == before + 1 && GetForegroundWindow() == Handle(target));
            CaptureDesktop(target, scenario + "-overlay-desktop.png");
            MonitoringHarness.CaptureWindow(overlay, scenario + "-overlay.png");
            Set(_monitor, "OverlayLayout", 1); Pump(200);
            Check(scenario + " narrow vertical strip", overlay.ActualWidth <= 160 && overlay.ActualHeight >= 60 && overlay.ActualHeight <= 100);
            Events.Add(new Dictionary<string, object> { { "scenario", scenario }, { "layout", "vertical" }, { "widthDip", overlay.ActualWidth }, { "heightDip", overlay.ActualHeight } });
            MonitoringHarness.CaptureWindow(overlay, scenario + "-overlay-vertical.png");
            CaptureDesktop(target, scenario + "-overlay-vertical-desktop.png");
            bounds = Bounds(overlay); point = new NativePoint { X = bounds.Left + 28, Y = bounds.Top + (bounds.Bottom - bounds.Top) / 2 };
            Check(scenario + " vertical hit-test reaches owned target", GetAncestor(WindowFromPoint(point), 2) == Handle(target));
            before = _clicks; GuardedClick(target, point); Pump(150);
            Check(scenario + " vertical click-through retains focus", _clicks == before + 1 && GetForegroundWindow() == Handle(target));
            Set(_monitor, "OverlayLayout", 0); Pump(200);
            Check(scenario + " compact horizontal strip", overlay.ActualHeight <= 36 && overlay.ActualWidth <= 430);
            Events.Add(new Dictionary<string, object> { { "scenario", scenario }, { "layout", "horizontal" }, { "widthDip", overlay.ActualWidth }, { "heightDip", overlay.ActualHeight } });
            if (fullscreen) CheckDrag(target, overlay);
        }
        finally { Set(_monitor, "OverlayEnabled", false); target.Close(); SetCursorPos(cursor.X, cursor.Y); Pump(100); }
    }
    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        for (IntPtr current = GetTopWindow(IntPtr.Zero); current != IntPtr.Zero; current = GetWindow(current, 2))
        { if (current == upper) return true; if (current == lower) return false; }
        return false;
    }
    private static void GuardedClick(Window target, NativePoint point)
    {
        if (GetForegroundWindow() != Handle(target) || GetAncestor(WindowFromPoint(point), 2) != Handle(target)) throw new InvalidOperationException("Input guard refused a non-owned target.");
        SetCursorPos(point.X, point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    private static void AcquireOwnedForeground(Window target)
    {
        // Windows may deny SetForegroundWindow after another app received input.
        // Briefly expose OUR target and click only after native hit-testing proves ownership.
        target.Topmost = true; Pump(100); var rect = Bounds(target);
        var point = new NativePoint { X = rect.Left + 320, Y = rect.Top + 300 };
        if (GetAncestor(WindowFromPoint(point), 2) != Handle(target)) throw new InvalidOperationException("Foreground acquisition refused: owned target is not exposed.");
        SetCursorPos(point.X, point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Pump(140); target.Topmost = false; Pump(100);
        Events.Add(new Dictionary<string, object> { { "foregroundAcquisition", "guarded click on verification-owned target" }, { "foregroundIsTarget", GetForegroundWindow() == Handle(target) } });
    }
    private static void CheckDrag(Window target, Window overlay)
    {
        Call(overlay, "BeginReposition"); Pump(180); var before = Bounds(overlay);
        Check("drag mode temporarily accepts input", (GetWindowLong(Handle(overlay), -20) & 0x20) == 0);
        var point = new NativePoint { X = before.Left + 30, Y = before.Top + 24 };
        if (GetForegroundWindow() != Handle(target) || GetAncestor(WindowFromPoint(point), 2) != Handle(overlay)) throw new InvalidOperationException("Drag guard refused a non-owned target.");
        SetCursorPos(point.X, point.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        try { Pump(100); SetCursorPos(point.X + 90, point.Y + 60); Pump(120); }
        finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
        Pump(250); var after = Bounds(overlay);
        Check("real drag moves overlay and saves custom position", Math.Abs(after.Left - before.Left - 90) <= 3 && Math.Abs(after.Top - before.Top - 60) <= 3 && (int)Get(_monitor, "OverlayCorner") == 4 && Math.Abs((double)Get(_monitor, "OverlayX") - after.Left) <= 3 && Math.Abs((double)Get(_monitor, "OverlayY") - after.Top) <= 3);
        Check("drag completion restores click-through and retains foreground", (GetWindowLong(Handle(overlay), -20) & 0x20) != 0 && GetForegroundWindow() == Handle(target));
        Events.Add(new Dictionary<string, object> { { "dragX", after.Left }, { "dragY", after.Top }, { "screen", Get(_monitor, "OverlayScreen") } });
    }
    private static void CaptureDesktop(Window target, string file)
    {
        if (GetForegroundWindow() != Handle(target)) throw new InvalidOperationException("Screenshot guard: verification window lost foreground.");
        var r = Bounds(target);
        using (var bitmap = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
            bitmap.Save(Path.Combine(_output, file), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    private static bool InsideWorkArea(Window overlay, Forms.Screen screen)
    {
        var r = Bounds(overlay); var a = screen.WorkingArea;
        Events.Add(new Dictionary<string, object> { { "screen", screen.DeviceName }, { "corner", Get(_monitor, "OverlayCorner") }, { "bounds", new[] { r.Left, r.Top, r.Right, r.Bottom } } });
        return r.Left >= a.Left && r.Top >= a.Top && r.Right <= a.Right && r.Bottom <= a.Bottom;
    }
    private static void CheckPlacementAndPersistence()
    {
        Set(_monitor, "OverlayEnabled", true); Pump(200);
        foreach (int seconds in new[] { 1, 5, 2 })
        {
            Set(_monitor, "RefreshSeconds", seconds);
            Check("live refresh " + seconds + "s also controls memory cadence", (int)Get(_window.DataContext, "MemoryRefreshSeconds") == seconds);
        }
        double x = (double)Get(_monitor, "OverlayX"), y = (double)Get(_monitor, "OverlayY"); string screenId = (string)Get(_monitor, "OverlayScreen");
        Set(_monitor, "ShowCpu", false); Set(_monitor, "ShowGpuTemperature", false);
        Set(_monitor, "OverlayLayout", 1);
        string stored;
        using (var key = Registry.LocalMachine.OpenSubKey(Constants.App.Registry.Key.Settings)) stored = Convert.ToString(key.GetValue("MonitoringOptions"), CultureInfo.InvariantCulture);
        Check("selected metrics and position saved to isolated settings", !string.IsNullOrEmpty(stored));
        typeof(Settings).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { true });
        Call(_monitor, "ReloadOptions"); Pump(180);
        Check("options survive real settings reload", !(bool)Get(_monitor, "ShowCpu") && !(bool)Get(_monitor, "ShowGpuTemperature") && (bool)Get(_monitor, "ShowCpuTemperature") && (bool)Get(_monitor, "OverlayEnabled") && (int)Get(_monitor, "RefreshSeconds") == 2 && (int)Get(_monitor, "OverlayCorner") == 4 && (string)Get(_monitor, "OverlayScreen") == screenId && (double)Get(_monitor, "OverlayX") == x && (double)Get(_monitor, "OverlayY") == y);
        Check("vertical layout survives real settings reload", (int)Get(_monitor, "OverlayLayout") == 1);
        Set(_monitor, "OverlayLayout", 0); Pump(150);
        Events.Add(new Dictionary<string, object> { { "persistedMonitoringOptions", stored } });
        foreach (var screen in Forms.Screen.AllScreens)
        {
            Set(_monitor, "OverlayScreen", screen.DeviceName);
            for (int corner = 0; corner < 4; corner++)
            {
                Set(_monitor, "OverlayCorner", corner);
                foreach (int layout in new[] { 0, 1 })
                {
                    Set(_monitor, "OverlayLayout", layout); Pump(180);
                    Check("screen/corner/layout visible " + screen.DeviceName + "/" + corner + "/" + layout, InsideWorkArea(Overlay(), screen));
                }
            }
            Set(_monitor, "OverlayLayout", 0); Pump(150);
        }
        Call(_monitor, "SavePosition", "missing-monitor-for-verification", 1000000.0, -1000000.0); Pump(180);
        Check("missing display and off-screen coordinates recover visibly", InsideWorkArea(Overlay(), Forms.Screen.PrimaryScreen));
        Set(_monitor, "ShowCpu", true); Set(_monitor, "ShowGpuTemperature", true);
        MonitoringHarness.CaptureWindow(Overlay(), "overlay-all-metrics.png");
        CheckLiveTemperatures();
        _window.Show(); Select(2); Pump(200);
        Set(_monitor, "OverlayLayout", 1);
        Call(_monitor, "SavePosition", screenId, x, y); Pump(150);
        foreach (string language in new[] { "en", "zh-Hans" })
        {
            Localizer.Language = new Language(CultureInfo.GetCultureInfo(language)); Pump(180);
            Check("language " + language + " keeps vertical layout and custom position", (int)Get(_monitor, "OverlayLayout") == 1 && (int)Get(_monitor, "OverlayCorner") == 4 && (string)Get(_monitor, "OverlayScreen") == screenId && (double)Get(_monitor, "OverlayX") == x && (double)Get(_monitor, "OverlayY") == y);
        }
        Set(_monitor, "OverlayLayout", 0); Set(_monitor, "OverlayEnabled", false);
        bool closePreference = Settings.CloseToTheNotificationArea;
        var onlyMonitor = _window.FindName("ShowMonitorOnly") as Button;
        Check("explicit monitor-only action exists", onlyMonitor != null);
        if (onlyMonitor != null) onlyMonitor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(200);
        Check("monitor-only action hides shell and keeps strip", !_window.IsVisible && !_window.ShowInTaskbar && Overlay().IsVisible);
        Check("monitor-only action does not overwrite close preference", Settings.CloseToTheNotificationArea == closePreference);
        int before = (int)Get(_monitor, "SampleCount");
        Until(delegate { return (int)Get(_monitor, "SampleCount") >= before + 2; }, 7000, "monitor-only strip continues real sampling without shell");
        Check("monitor-only memory refresh follows selected interval", (int)Get(_window.DataContext, "MemoryRefreshSeconds") == (int)Get(_monitor, "RefreshSeconds"));
        Events.Add(new Dictionary<string, object> { { "state", "monitor only" }, { "shellVisible", _window.IsVisible }, { "sampleBefore", before }, { "sampleAfter", Get(_monitor, "SampleCount") }, { "cpuTemperature", Get(_monitor, "OverlayCpuTemperatureText") } });
        MonitoringHarness.CaptureWindow(Overlay(), "monitor-only-strip.png");
    }
    private static void CheckLiveTemperatures()
    {
        var sourceType = typeof(MainWindow).Assembly.GetType("WinMemoryCleaner.AsusCpuTemperatureSource");
        using (var source = (IDisposable)Activator.CreateInstance(sourceType))
        {
            var direct = (MetricValue)sourceType.GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);
            if (direct.Status != MetricStatus.Available)
            {
                Events.Add(new Dictionary<string, object> { { "asusUnavailable", direct.Detail } });
                return;
            }
            Until(delegate { return ((string)Get(_monitor, "CpuTemperatureDetail")).IndexOf("ASUS", StringComparison.Ordinal) >= 0 && ((string)Get(_monitor, "OverlayCpuTemperatureText")).EndsWith("°C", StringComparison.Ordinal); }, 8000, "existing ASUS temperature reaches live overlay");
            int start = (int)Get(_monitor, "TemperatureSampleCount");
            for (int i = 0; i < 5; i++)
            {
                Pump(2200);
                string value = (string)Get(_monitor, "OverlayCpuTemperatureText");
                Check("live CPU temperature sample " + i, value.EndsWith("°C", StringComparison.Ordinal) && ((string)Get(_monitor, "CpuTemperatureDetail")).IndexOf("ASUS", StringComparison.Ordinal) >= 0);
                Events.Add(new Dictionary<string, object> { { "cpuTemperature", value }, { "gpuTemperature", Get(_monitor, "OverlayGpuTemperatureText") }, { "source", Get(_monitor, "CpuTemperatureDetail") }, { "temperatureSample", Get(_monitor, "TemperatureSampleCount") } });
            }
            Check("live temperature continues refreshing", (int)Get(_monitor, "TemperatureSampleCount") >= start + 4);
            MonitoringHarness.CaptureWindow(Overlay(), "overlay-live-temperature.png");
        }
    }
}
