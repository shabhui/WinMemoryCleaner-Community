// Compile against a Release WMC assembly; this hosts its REAL windows without
// invoking production startup/migration/single-instance or changing user settings.
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Xml.Linq;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinMemoryCleaner;
using Forms = System.Windows.Forms;

public sealed class VerificationApp : WinMemoryCleaner.App
{
    protected override void OnStartup(StartupEventArgs e) { }
}

public static class MonitoringHarness
{
    private static string _assemblyPath;
    private static string _applicationXamlPath;
    private static string _output;
    private static string _mode;
    private static int _warmup, _seconds, _trial;
    private static readonly List<double> _cpu = new List<double>();
    private static readonly List<long> _private = new List<long>();
    private static readonly List<double> _latency = new List<double>();
    private static Stopwatch _clock;
    private static Process _self;
    private static double _lastTime, _lastCpu;
    private static long _firstPrivate, _lastPrivate;
    private static StreamWriter _csv;
    private static System.Threading.Timer _timer;
    private static int _sampling;
    private static bool _finished;
    private static VerificationApp _app;
    private static MainWindow _window;
    private static bool _captureScreenshots;
    private static double _measureStart = -1, _measureCpu;
    private static int _minimumRows = int.MaxValue, _maximumRows;
    private static StreamWriter _dispatchCsv;
    private static object _monitorObject;

    [DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(IntPtr key, IntPtr newKey);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 7) { Console.Error.WriteLine("assembly mode output warmup seconds trial application-xaml [capture-screenshots:0|1]"); return 2; }
        _assemblyPath = Path.GetFullPath(args[0]); _mode = args[1]; _output = Path.GetFullPath(args[2]);
        _warmup = int.Parse(args[3], CultureInfo.InvariantCulture);
        _seconds = int.Parse(args[4], CultureInfo.InvariantCulture);
        _trial = int.Parse(args[5], CultureInfo.InvariantCulture);
        _applicationXamlPath = Path.GetFullPath(args[6]);
        _captureScreenshots = args.Length > 7 && args[7] == "1";
        Directory.CreateDirectory(_output);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        Assembly.LoadFrom(_assemblyPath);
        try { return RunCore(); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(_output, "harness-error.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs e)
    {
        var name = new AssemblyName(e.Name).Name;
        if (name == "WinMemoryCleaner.Community") return Assembly.LoadFrom(_assemblyPath);
        var path = Path.Combine(Path.GetDirectoryName(_assemblyPath), name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunCore()
    {
        string isolatedKey = "Software\\WinMemoryCleaner.Verification\\" + Guid.NewGuid().ToString("N");
        using (var machine = Registry.CurrentUser.CreateSubKey(isolatedKey))
        {
            var handleObject = typeof(RegistryKey).GetProperty("Handle").GetValue(machine, null);
            var safeHandle = handleObject as SafeHandle;
            var handle = safeHandle == null ? (IntPtr)handleObject : safeHandle.DangerousGetHandle();
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\.NETFramework");
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts");
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\FontLink");
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP");
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\Cryptography\\Defaults");
            // PDH resolves counter names/providers via Perflib. Preserve this read-only
            // system metadata in the disposable hive so isolation does not fake unsupported GPUs.
            CloneRegistry(Registry.LocalMachine, machine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Perflib");
            File.WriteAllText(Path.Combine(_output, "isolated-registry-path.txt"), isolatedKey);
            int code = RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)), handle);
            if (code != 0) throw new InvalidOperationException("Registry isolation failed: " + code);
            try
            {
                using (var settings = machine.CreateSubKey(Constants.App.Registry.Key.Settings))
                {
                    settings.SetValue("Language", "zh-Hans"); settings.SetValue("FontSize", 14);
                    settings.SetValue("CreateStartMenuShortcut", 0); settings.SetValue("RunOnStartup", 0);
                    settings.SetValue("AutoUpdate", 0); settings.SetValue("AutoOptimizationInterval", 0);
                    settings.SetValue("AutoOptimizationMemoryUsage", 0); settings.SetValue("UseHotkey", 0);
                    settings.SetValue("CompactMode", 0); settings.SetValue("StartMinimized", 0);
                    settings.SetValue("CloseToTheNotificationArea", 1); settings.SetValue("ShowVirtualMemory", 0);
                    settings.SetValue("TrayIconShowMemoryUsage", 1); settings.SetValue("OverlayEnabled", 0);
                }
                DependencyInjection.Container.Register<IComputerService, ComputerService>();
                DependencyInjection.Container.Register<IHotkeyService, HotkeyService>();
                DependencyInjection.Container.Register<INotificationService, NotificationService>();
                var icon = new Forms.NotifyIcon(); DependencyInjection.Container.Register(icon);
                typeof(WinMemoryCleaner.App).GetProperty("Path").SetValue(null, _assemblyPath, null);
                typeof(WinMemoryCleaner.App).GetProperty("Version").SetValue(null, typeof(MainWindow).Assembly.GetName().Version, null);
                _app = new VerificationApp();
                var bindingTrace = new TextWriterTraceListener(Path.Combine(_output, "binding.log"));
                PresentationTraceSources.DataBindingSource.Listeners.Add(bindingTrace);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                LoadApplicationResources();
                _window = new MainWindow(); _app.MainWindow = _window;
                _window.Show();
                ConfigureMode();
                if (_mode == "interaction")
                {
                    MonitoringInteraction.Run(_window, _output);
                    bindingTrace.Flush(); bindingTrace.Close();
                    _app.Shutdown(0); icon.Dispose();
                    return 0;
                }
                _self = Process.GetCurrentProcess(); _clock = Stopwatch.StartNew();
                _lastCpu = _self.TotalProcessorTime.TotalSeconds;
                _csv = new StreamWriter(Path.Combine(_output, "samples.csv"), false);
                _csv.WriteLine("elapsedSeconds,cpuMachinePercent,privateBytes,workingSetBytes,measurement");
                _dispatchCsv = new StreamWriter(Path.Combine(_output, "dispatcher.csv"), false);
                _dispatchCsv.WriteLine("elapsedSeconds,latencyMs,processRows");
                _timer = new System.Threading.Timer(Sample, null, 1000, 1000);
                if (_captureScreenshots)
                {
                    var screenshot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    screenshot.Tick += delegate { screenshot.Stop(); CaptureVisibleWindows("window"); };
                    screenshot.Start();
                }
                _app.Run();
                _timer.Dispose(); _csv.Dispose(); _dispatchCsv.Dispose(); _self.Dispose();
                bindingTrace.Flush(); bindingTrace.Close();
                var locator = _app.Resources["ViewModelLocator"] as IDisposable;
                if (locator != null) locator.Dispose();
                icon.Dispose();
                return _finished ? 0 : 1;
            }
            finally { RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)), IntPtr.Zero); }
        }
        // Disposable registry tree is removed by verification script after process exit.
    }

    private static void LoadApplicationResources()
    {
        // Load the exact application's resource dictionary rather than passing a
        // cross-assembly derived App to LoadComponent (WPF rejects that identity).
        var document = XDocument.Load(_applicationXamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = document.Root.Element(presentation + "Application.Resources").Element(presentation + "ResourceDictionary");
        resources.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:WinMemoryCleaner");
        resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        resources.SetAttributeValue(XNamespace.Xmlns + "System", "clr-namespace:System;assembly=mscorlib");
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/WinMemoryCleaner.Community;component/app.xaml", UriKind.Absolute) };
        string xaml = resources.ToString().Replace("clr-namespace:WinMemoryCleaner", "clr-namespace:WinMemoryCleaner;assembly=WinMemoryCleaner.Community");
        _app.Resources = (ResourceDictionary)XamlReader.Parse(xaml, context);
    }

    private static void ConfigureMode()
    {
        ThemeManager.Theme = Enums.Theme.Dark;
        var type = _window.GetType();
        var select = type.GetMethod("SelectPage", BindingFlags.Instance | BindingFlags.Public);
        if ((_mode == "process" || _mode == "stress" || _mode == "retention") && select != null) select.Invoke(_window, new object[] { 1 });
        if (_mode == "settings" && select != null) select.Invoke(_window, new object[] { 2 });
        if (_mode == "minimum") { _window.Width = _window.MinWidth; _window.Height = _window.MinHeight; }
        if (_mode == "light") ThemeManager.Theme = Enums.Theme.Light;
        if (_mode == "compact") ((MainViewModel)_window.DataContext).CompactMode = true;
        var monitorProperty = type.GetProperty("Monitoring");
        _monitorObject = monitorProperty == null ? null : monitorProperty.GetValue(_window, null);
        if (_mode == "overlay" && monitorProperty != null)
        {
            var monitor = monitorProperty.GetValue(_window, null);
            monitor.GetType().GetProperty("OverlayEnabled").SetValue(monitor, true, null);
        }
        if (_mode == "tray" || _mode == "overlay") _window.Hide();
    }

    private static void CloneRegistry(RegistryKey sourceRoot, RegistryKey targetRoot, string path)
    {
        using (var source = sourceRoot.OpenSubKey(path))
        {
            if (source == null) return;
            using (var target = targetRoot.CreateSubKey(path))
            {
                foreach (string name in source.GetValueNames())
                    target.SetValue(name, source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), source.GetValueKind(name));
            }
            foreach (string child in source.GetSubKeyNames()) CloneRegistry(sourceRoot, targetRoot, path + "\\" + child);
        }
    }

    private static void Sample(object state)
    {
        if (Interlocked.Exchange(ref _sampling, 1) != 0) return;
        try
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            _self.Refresh(); double totalCpu = _self.TotalProcessorTime.TotalSeconds;
            double cpu = (totalCpu - _lastCpu) / Math.Max(0.001, elapsed - _lastTime) / Environment.ProcessorCount * 100;
            long privateBytes = _self.PrivateMemorySize64;
            bool boundary = _measureStart < 0 && elapsed >= _warmup;
            if (boundary) { _measureStart = elapsed; _measureCpu = totalCpu; _firstPrivate = privateBytes; }
            bool measured = _measureStart >= 0 && !boundary;
            _csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F6},{2},{3},{4}", elapsed, cpu, privateBytes, _self.WorkingSet64, measured ? 1 : 0));
            _csv.Flush();
            if (measured)
            {
                _cpu.Add(cpu); _private.Add(privateBytes);
                // First private bytes were captured at the end of warm-up, before this interval.
                _lastPrivate = privateBytes;
                var queueClock = Stopwatch.StartNew();
                _app.Dispatcher.BeginInvoke(new Action(delegate
                {
                    double latency = queueClock.Elapsed.TotalMilliseconds;
                    _latency.Add(latency);
                    int rows = _monitorObject == null ? 0 : (int)_monitorObject.GetType().GetProperty("ProcessCount").GetValue(_monitorObject, null);
                    _minimumRows = Math.Min(_minimumRows, rows); _maximumRows = Math.Max(_maximumRows, rows);
                    _dispatchCsv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F6},{2}", elapsed, latency, rows));
                    _dispatchCsv.Flush();
                }), DispatcherPriority.Input);
            }
            _lastTime = elapsed; _lastCpu = totalCpu;
            if (_measureStart >= 0 && elapsed - _measureStart >= _seconds)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _app.Dispatcher.BeginInvoke(new Action(Finish), DispatcherPriority.ContextIdle);
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(_output, "sampling-errors.txt"), ex + Environment.NewLine);
        }
        finally { Interlocked.Exchange(ref _sampling, 0); }
    }

    private static void Finish()
    {
        _private.Sort(); _latency.Sort();
        var summary = new Dictionary<string, object>();
        summary["assembly"] = _assemblyPath; summary["mode"] = _mode; summary["trial"] = _trial;
        summary["warmupSeconds"] = _warmup; summary["measurementSeconds"] = _seconds;
        summary["samples"] = _cpu.Count; summary["logicalProcessors"] = Environment.ProcessorCount;
        summary["actualWarmupSeconds"] = _measureStart;
        summary["actualMeasurementSeconds"] = _lastTime - _measureStart;
        summary["meanCpuMachinePercent"] = (_lastCpu - _measureCpu) / (_lastTime - _measureStart) / Environment.ProcessorCount * 100;
        summary["minimumProcessRows"] = _minimumRows == int.MaxValue ? 0 : _minimumRows;
        summary["maximumProcessRows"] = _maximumRows;
        summary["captureScreenshots"] = _captureScreenshots;
        summary["medianPrivateBytes"] = _private.Count == 0 ? 0 : _private[_private.Count / 2];
        summary["startPrivateBytes"] = _firstPrivate; summary["endPrivateBytes"] = _lastPrivate;
        summary["privateGrowthBytes"] = _lastPrivate - _firstPrivate;
        summary["dispatcherP95Ms"] = _latency.Count == 0 ? -1 : _latency[Math.Min(_latency.Count - 1, (int)Math.Ceiling(_latency.Count * 0.95) - 1)];
        var monitorProperty = _window.GetType().GetProperty("Monitoring");
        if (monitorProperty != null)
        {
            var monitor = monitorProperty.GetValue(_window, null);
            foreach (string property in new[] { "CpuText", "GpuText", "CpuTemperatureText", "GpuTemperatureText", "CpuDetail", "GpuDetail", "CpuTemperatureDetail", "GpuTemperatureDetail", "GpuName", "RefreshSeconds", "ProcessCount", "SampleCount", "ProcessSampleCount", "TemperatureSampleCount" })
            {
                var info = monitor.GetType().GetProperty(property);
                if (info != null) summary[property] = info.GetValue(monitor, null);
            }
        }
        File.WriteAllText(Path.Combine(_output, "summary.json"), new JavaScriptSerializer().Serialize(summary));
        if (_captureScreenshots) { CaptureVisibleWindows("window-final"); if (_window.IsVisible) RecordLayout(); }
        if ((_mode == "stress" || _mode == "retention") && _minimumRows < 200)
        {
            File.WriteAllText(Path.Combine(_output, "verification-error.txt"), "Fewer than 200 real process rows during the stress/retention measurement.");
            _app.Shutdown(1); return;
        }
        _finished = true; _app.Shutdown(0);
    }

    internal static void RecordLayout()
    {
        var lines = new List<string>();
        lines.Add(string.Format(CultureInfo.InvariantCulture, "window,{0},{1}", _window.ActualWidth, _window.ActualHeight));
        RecordElement(_window, lines);
        File.WriteAllLines(Path.Combine(_output, "layout.csv"), lines.ToArray());
    }

    private static void RecordElement(DependencyObject element, List<string> lines)
    {
        var framework = element as FrameworkElement;
        if (framework != null && framework.IsVisible && !string.IsNullOrEmpty(framework.Name))
        {
            try
            {
                Point position = framework.TranslatePoint(new Point(0, 0), _window);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3:F2},{4:F2},{5}", framework.Name, position.X, position.Y, framework.ActualWidth, framework.ActualHeight, framework.IsEnabled));
            }
            catch (InvalidOperationException) { }
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) RecordElement(VisualTreeHelper.GetChild(element, i), lines);
    }

    private static void CaptureVisibleWindows(string prefix)
    {
        foreach (Window window in _app.Windows)
            if (window.IsVisible) CaptureWindow(window, prefix + (window == _window ? ".png" : "-overlay.png"));
    }

    internal static void CaptureWindow(Window window, string name)
    {
        if (window.ActualWidth < 1 || window.ActualHeight < 1) return;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(_output, name))) encoder.Save(stream);
    }
}
