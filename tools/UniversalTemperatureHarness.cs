// Independent Task5 tooling, never linked into the product. Real release windows and
// embedded backend; no installer, default user sensor cache, or foreign process actions.
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using WinMemoryCleaner;
using Forms = System.Windows.Forms;

public sealed class VerificationApp : WinMemoryCleaner.App
{
    // Do not call base: production migration, single-instance, startup task and updater
    // must not run. The real MainViewModel/monitoring workers remain production code.
    protected override void OnStartup(StartupEventArgs e) { }
}

public static class UniversalTemperatureHarness
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PayloadPrefix = "WinMemoryCleaner.Sensors.Payload.";
    private static string _assemblyPath, _xamlPath, _scratch, _output, _mode, _cache, _registryKey;
    private static Assembly _production;
    private static VerificationApp _app;
    private static MainWindow _window;
    private static MonitoringViewModel _model;
    private static Forms.NotifyIcon _icon;
    private static DriverCapture _driver;
    private static object _payload;
    private static int _warmup, _seconds, _trial, _ownerNumber;
    private static int _exitCode = 3;
    private static bool _redirected, _finished;
    private static readonly Stopwatch EntryClock = Stopwatch.StartNew();
    private static readonly object OwnersGate = new object(), EvidenceGate = new object();
    private static readonly List<Owner> Owners = new List<Owner>();
    private static readonly List<Child> Children = new List<Child>();
    private static readonly List<Dictionary<string, object>> Checks = new List<Dictionary<string, object>>();
    private static readonly HashSet<string> Providers = new HashSet<string>(StringComparer.Ordinal);
    private static readonly List<DispatchRecord> Dispatches = new List<DispatchRecord>();
    private static StreamWriter _events, _snapshots, _dispatchCsv, _csv;
    private static TextWriterTraceListener _binding;
    private static System.Threading.Timer _dispatchTimer, _sampleTimer;
    private static string _phase = "startup";
    private static int _dispatchPending, _sampling, _maxOwned;
    private static Stopwatch _demandClock;
    private static double _lastSampleTime, _lastMainCpu, _measureStart = -1, _measureMainCpu, _measureBackendCpu;
    private static long _baselineMainPrivate, _baselineBackendPrivate, _endMainPrivate, _endBackendPrivate;
    private static readonly List<double> MainCpu = new List<double>(), BackendCpu = new List<double>(), CombinedCpu = new List<double>();
    private static readonly List<long> MainPrivate = new List<long>(), BackendPrivate = new List<long>(), CombinedPrivate = new List<long>();
    private static Process _self;
    private static Child _measurementChild;
    private static int _measurementOwnedMinimum = Int32.MaxValue;
    private static double _lastBackendTotal;
    private static double _firstRunningSeconds = -1;
    private static string _initialSystemSettings;
    private static Dictionary<string, object> _summary;

    private sealed class DriverCapture
    {
        internal readonly object GenuineState;
        internal readonly bool BeforeOverride;
        internal DriverCapture(object state, bool before) { GenuineState = state; BeforeOverride = before; }
    }
    private sealed class Owner
    {
        internal int Number;
        internal object Controller, Sampler;
        internal IResourceMonitor Monitor;
    }
    private sealed class Child
    {
        internal Owner Owner;
        internal Process Process;
        internal int Pid;
        internal long CreationTicks;
        internal string Path;
        internal object Job;
        internal double LastCpu;
        internal bool FinalCpuCollected;
    }
    private sealed class BackendFrame
    {
        internal int Pid, Refresh;
        internal string Status, Driver, Diagnostic;
        internal long Sequence = -1, ReceivedTicks;
        internal DateTime SampleUtc;
        internal MetricValue Cpu, Gpu;
        internal bool CpuEnabled, Abandoned;
        internal object Session, Job, Lease;
        internal Process Process;
    }
    private sealed class DispatchRecord
    {
        internal string Phase;
        internal double Elapsed, Latency;
        internal bool Measured;
    }

    [DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "contracts") return RunContracts(args);
        if (args.Length < 5 || (args[0] != "functional" && args[0] != "performance"))
        { Console.Error.WriteLine("contracts assembly scratch output | functional assembly app-xaml scratch output | performance assembly app-xaml scratch output warmup seconds trial"); return 2; }
        _mode = args[0]; _assemblyPath = Path.GetFullPath(args[1]); _xamlPath = Path.GetFullPath(args[2]);
        _scratch = Path.GetFullPath(args[3]); _output = Path.GetFullPath(args[4]);
        if (_mode == "performance")
        {
            if (args.Length != 8) return 2;
            _warmup = Int32.Parse(args[5], CultureInfo.InvariantCulture); _seconds = Int32.Parse(args[6], CultureInfo.InvariantCulture); _trial = Int32.Parse(args[7], CultureInfo.InvariantCulture);
            if (_warmup != 30 || (_seconds != 60 && _seconds != 600)) return 2;
        }
        try
        {
            RequireScratch(_output); Directory.CreateDirectory(_output);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            _production = Assembly.LoadFrom(_assemblyPath);
            return RunCore();
        }
        catch (Exception error)
        {
            if (Directory.Exists(_output)) File.WriteAllText(Path.Combine(_output, "fatal-error.txt"), error.ToString());
            Console.Error.WriteLine(error); return 3;
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs e)
    {
        string name = new AssemblyName(e.Name).Name;
        if (name == "WinMemoryCleaner.Community") return Assembly.LoadFrom(_assemblyPath);
        string candidate = Path.Combine(Path.GetDirectoryName(_assemblyPath), name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    // These two real boundary assertions were first executed RED (two expected
    // assertion failures, no extraction/registry override) before their implementation.
    private static int RunContracts(string[] args)
    {
        string assemblyPath = Path.GetFullPath(args[1]), scratch = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        _scratch = scratch; Directory.CreateDirectory(output); Assembly assembly = Assembly.LoadFrom(assemblyPath);
        var checks = new List<Dictionary<string, object>>();
        RunAssertion(checks, "isolated-cache-never-uses-user-default", delegate
        {
            string root = PayloadRoot(scratch, output);
            string prefix = scratch.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Assertion failed: payload root must be within session scratch before any preparation; actual=" + root);
            string userDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"WinMemoryCleaner.Community\Sensors");
            if (String.Equals(root, userDefault, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Assertion failed: user sensor cache is forbidden.");
        });
        RunAssertion(checks, "genuine-driver-is-captured-before-HKLM-override", delegate
        {
            DriverCapture capture = CaptureGenuineDriver(assembly);
            if (capture.GenuineState == null || !capture.BeforeOverride) throw new InvalidOperationException("Assertion failed: SensorDriverTrust.Inspect must return its real production state before process-local registry override.");
            if (capture.GenuineState.GetType().GetProperty("ServicePresent", Instance) == null || capture.GenuineState.GetType().GetProperty("CpuAllowed", Instance) == null)
                throw new InvalidOperationException("Assertion failed: captured object must be the actual production SensorDriverState.");
        });
        RunAssertion(checks, "deep-evidence-cache-fits-net40-and-releases-real-lease", delegate
        {
            string root = PayloadRoot(scratch, output); RequireScratch(root);
            byte[] manifest;
            using (Stream input = assembly.GetManifestResourceStream(PayloadPrefix + "manifest.json"))
            using (var bytes = new MemoryStream())
            {
                if (input == null) throw new InvalidOperationException("Actual embedded manifest is missing.");
                input.CopyTo(bytes); manifest = bytes.ToArray();
            }
            var parsed = (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(manifest));
            foreach (IDictionary<string, object> file in (IList)parsed["files"])
            {
                string path = Path.Combine(Path.Combine(root, new string('a', 64)), (string)file["path"]);
                if (path.Length >= 260 || Path.GetDirectoryName(path).Length >= 248)
                    throw new InvalidOperationException("Assertion failed: actual payload exceeds .NET 4 path budget; length=" + path.Length + "; path=" + path);
            }
            if (Directory.Exists(root)) throw new InvalidOperationException("Refusing an existing contract payload root.");
            IDisposable lease = null;
            try
            {
                Type type = assembly.GetType("WinMemoryCleaner.SensorPayload", true);
                Func<string, Stream> resources = delegate(string name) { return assembly.GetManifestResourceStream(name); };
                object payload = type.GetConstructor(Instance, null, new[] { typeof(string), typeof(byte[]), typeof(Func<string, Stream>) }, null).Invoke(new object[] { root, manifest, resources });
                lease = (IDisposable)Invoke(payload, "Prepare");
                string executable = (string)Get(lease, "Executable");
                lease.Dispose(); lease = null;
                using (var writable = new FileStream(executable, FileMode.Open, FileAccess.Write, FileShare.None)) { }
                RequireScratch(root); Directory.Delete(root, true);
                if (Directory.Exists(root)) throw new InvalidOperationException("Real prepared payload was not removed after lease release.");
            }
            catch (Exception error) { throw new InvalidOperationException("Actual embedded Prepare/release/removal failed; no backend or installer was started.", error); }
            finally { if (lease != null) lease.Dispose(); }
        });
        RunAssertion(checks, "performance-completion-waits-for-duration-and-samples", delegate
        {
            // Replay the actual failed retention boundary: 600.486s, only 595 samples.
            _measureStart = 30.2529223; _seconds = 600;
            try
            {
                for (int count = 0; count < 595; count++) MainCpu.Add(0);
                if (CanFinishPerformance(630.7391164)) throw new InvalidOperationException("Assertion failed: timer stopped with only 595 of the required 600 samples.");
                for (int count = 595; count < 600; count++) MainCpu.Add(0);
                if (CanFinishPerformance(629.2529223)) throw new InvalidOperationException("Assertion failed: sample count cannot shorten the required measurement duration.");
                if (!CanFinishPerformance(635.7391164)) throw new InvalidOperationException("Assertion failed: completed duration and 600 samples must finish the bounded run.");
            }
            finally { MainCpu.Clear(); _measureStart = -1; _seconds = 0; }
        });
        File.WriteAllText(Path.Combine(output, "contracts.json"), Json(checks));
        int failures = 0;
        foreach (var check in checks) { Console.WriteLine(check["name"] + ": " + check["result"] + " " + check["evidence"]); if ((string)check["result"] == "failed") failures++; }
        return failures == 0 ? 0 : 1;
    }
    private static void RunAssertion(List<Dictionary<string, object>> checks, string name, Action assertion)
    {
        var row = new Dictionary<string, object>(); row["name"] = name;
        try { assertion(); row["result"] = "passed"; row["evidence"] = "Real boundary contract satisfied."; }
        catch (InvalidOperationException error) { row["result"] = "failed"; row["evidence"] = error.Message; }
        checks.Add(row);
    }
    private static string PayloadRoot(string scratch, string output)
    {
        string boundary = Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string evidence = Path.GetFullPath(output);
        if (!evidence.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing evidence outside session scratch.");
        string identity;
        using (SHA256 hash = SHA256.Create()) identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(evidence.ToLowerInvariant()))).Replace("-", String.Empty).Substring(0, 32);
        // Keep .NET 4 paths short even when evidence uses deeply nested trial folders.
        string root = Path.GetFullPath(Path.Combine(scratch, "uvc-" + identity));
        if (!root.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing a sensor cache outside session scratch.");
        return root;
    }
    private static DriverCapture CaptureGenuineDriver(Assembly assembly)
    {
        if (_redirected) throw new InvalidOperationException("Real driver inspection after HKLM redirection is forbidden.");
        Type trust = assembly.GetType("WinMemoryCleaner.Sensors.SensorDriverTrust", true);
        MethodInfo inspect = trust.GetMethod("Inspect", Static);
        if (inspect == null) throw new InvalidOperationException("Production SensorDriverTrust.Inspect is missing.");
        return new DriverCapture(inspect.Invoke(null, null), true);
    }
    private static void RequireScratch(string path)
    {
        string prefix = _scratch.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Writable path is outside session scratch: " + path);
        for (string part = Path.GetFullPath(path); !String.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            if ((Directory.Exists(part) || File.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Writable path contains a reparse point: " + part);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunCore()
    {
        _self = Process.GetCurrentProcess(); _cache = PayloadRoot(_scratch, _output); RequireScratch(_cache);
        _events = Writer("events.jsonl"); _snapshots = Writer("snapshots.jsonl"); _dispatchCsv = Writer("dispatcher.csv");
        _dispatchCsv.WriteLine("elapsedSeconds,latencyMs,phase,measurement");
        _initialSystemSettings = ReadSettingsFingerprint();
        _driver = CaptureGenuineDriver(_production); // MUST precede RegOverridePredefKey.
        Save("genuine-driver.json", DriverEvidence());
        PreparePayloadObject();
        Check("production-version-and-real-payload", _production.GetName().Version.Equals(new Version(3, 0, 8, 4)), _production.FullName);
        Check("release-test-flag-is-off", !(bool)Get(typeof(WinMemoryCleaner.App), "IsTestBuild"), "No product compile-time test isolation was enabled.");
        Event("entry-before-isolation", ProcessCost());
        _registryKey = @"Software\WinMemoryCleaner.UniversalVerification\" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(_output, "isolated-registry-path.txt"), _registryKey);
        try
        {
            using (RegistryKey machine = Registry.CurrentUser.CreateSubKey(_registryKey))
            {
                foreach (string path in new[] { @"SOFTWARE\Microsoft\.NETFramework", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontLink", @"SOFTWARE\Microsoft\NET Framework Setup\NDP", @"SOFTWARE\Microsoft\Cryptography\Defaults", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib" }) CloneRegistry(Registry.LocalMachine, machine, path);
                object handleObject = typeof(RegistryKey).GetProperty("Handle").GetValue(machine, null);
                SafeHandle safe = handleObject as SafeHandle; IntPtr handle = safe == null ? (IntPtr)handleObject : safe.DangerousGetHandle();
                int code = RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)), handle);
                if (code != 0) throw new InvalidOperationException("Process-local HKLM isolation failed: " + code);
                _redirected = true;
                try
                {
                    SeedSettings(machine);
                    DependencyInjection.Container.Register<IComputerService, ComputerService>();
                    DependencyInjection.Container.Register<IHotkeyService, HotkeyService>();
                    DependencyInjection.Container.Register<INotificationService, NotificationService>();
                    _icon = new Forms.NotifyIcon(); DependencyInjection.Container.Register(_icon);
                    typeof(WinMemoryCleaner.App).GetProperty("Path").SetValue(null, _assemblyPath, null);
                    typeof(WinMemoryCleaner.App).GetProperty("Version").SetValue(null, _production.GetName().Version, null);
                    _app = new VerificationApp(); _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    _binding = new TextWriterTraceListener(Path.Combine(_output, "binding.log"));
                    PresentationTraceSources.DataBindingSource.Listeners.Add(_binding); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                    LoadApplicationResources();
                    Localizer.Language = new Language(CultureInfo.GetCultureInfo("zh-Hans")); ThemeManager.Theme = Enums.Theme.Dark;
                    NewWindow(); _window.Show();
                    Event("real-window-shown", ProcessCost());
                    _dispatchTimer = new System.Threading.Timer(ProbeDispatcher, null, 100, 100);
                    if (_mode == "performance") StartPerformance();
                    else
                    {
                        var worker = new Thread(Functional) { IsBackground = true, Name = "Task5 bounded functional verification" };
                        worker.Start();
                    }
                    _app.Run();
                }
                finally
                {
                    if (_sampleTimer != null) _sampleTimer.Dispose(); if (_dispatchTimer != null) _dispatchTimer.Dispose();
                    CleanupOwned();
                    if (_window != null) { try { _window.Close(); _window.Dispose(); } catch (InvalidOperationException) { } }
                    if (_app != null) { var locator = _app.Resources["ViewModelLocator"] as IDisposable; if (locator != null) locator.Dispose(); }
                    if (_icon != null) _icon.Dispose();
                    if (_binding != null) { _binding.Flush(); _binding.Close(); }
                    RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)), IntPtr.Zero); _redirected = false;
                }
            }
        }
        finally
        {
            if (_redirected) { RegOverridePredefKey(new IntPtr(unchecked((int)0x80000002)), IntPtr.Zero); _redirected = false; }
            bool removed = false;
            if (!String.IsNullOrEmpty(_registryKey))
            {
                Registry.CurrentUser.DeleteSubKeyTree(_registryKey, false);
                using (RegistryKey remaining = Registry.CurrentUser.OpenSubKey(_registryKey)) removed = remaining == null;
            }
            Check("disposable-registry-removed", removed, _registryKey);
            Check("true-HKLM-settings-unchanged", _initialSystemSettings == ReadSettingsFingerprint(), "Read-only fingerprints of both registry views before/after process-local isolation.");
            Save("checks.json", Checks);
            Save("cleanup.json", CleanupEvidence());
            if (_summary != null) { _summary["checks"] = CheckCounts(); _summary["cleanup"] = CleanupEvidence(); Save("summary.json", _summary); }
            if (_csv != null) _csv.Dispose(); if (_events != null) _events.Dispose(); if (_snapshots != null) _snapshots.Dispose(); if (_dispatchCsv != null) _dispatchCsv.Dispose();
            foreach (Child child in Children) child.Process.Dispose(); _self.Dispose();
        }
        return _finished ? (FailedChecks() == 0 ? _exitCode : 1) : 3;
    }

    private static void PreparePayloadObject()
    {
        byte[] manifest;
        using (Stream input = _production.GetManifestResourceStream(PayloadPrefix + "manifest.json"))
        using (var output = new MemoryStream())
        {
            if (input == null) throw new InvalidDataException("Real embedded manifest is missing.");
            byte[] buffer = new byte[4096]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { if (output.Length + count > 65536) throw new InvalidDataException("Manifest exceeded 64 KiB."); output.Write(buffer, 0, count); }
            manifest = output.ToArray();
        }
        var parsed = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(manifest)) as IDictionary<string, object>;
        IList files = (IList)parsed["files"];
        int embedded = 0; foreach (string resource in _production.GetManifestResourceNames()) if (resource.StartsWith(PayloadPrefix, StringComparison.Ordinal)) embedded++;
        Check("real-embedded-24-files-25-resources", files.Count == 24 && embedded == 25, "files=" + files.Count + "; embedded resources=" + embedded);
        File.WriteAllBytes(Path.Combine(_output, "embedded-manifest.json"), manifest);
        Type type = _production.GetType("WinMemoryCleaner.SensorPayload", true);
        Func<string, Stream> resources = delegate(string name) { return _production.GetManifestResourceStream(name); };
        _payload = type.GetConstructor(Instance, null, new[] { typeof(string), typeof(byte[]), typeof(Func<string, Stream>) }, null).Invoke(new object[] { _cache, manifest, resources });
        Check("isolated-production-payload-root", (string)Field(_payload, "_root") == _cache, _cache);
    }
    private static Delegate Lambda(Type delegateType, Expression body) { return Expression.Lambda(delegateType, body).Compile(); }
    private static IResourceMonitor CreateMonitor()
    {
        Type controllerType = _production.GetType("WinMemoryCleaner.SensorBackendController", true), jobType = _production.GetType("WinMemoryCleaner.SensorJob", true);
        ConstructorInfo constructor = controllerType.GetConstructors(Instance)[0];
        foreach (ConstructorInfo candidate in controllerType.GetConstructors(Instance)) if (candidate.GetParameters().Length == 4) constructor = candidate;
        ParameterInfo[] parameters = constructor.GetParameters();
        var prepare = Lambda(parameters[0].ParameterType, Expression.Call(Expression.Constant(_payload), _payload.GetType().GetMethod("Prepare", Instance)));
        var jobs = Lambda(parameters[1].ParameterType, Expression.New(jobType.GetConstructor(Instance, null, Type.EmptyTypes, null)));
        var inspect = Lambda(parameters[3].ParameterType, Expression.Constant(_driver.GenuineState, _driver.GenuineState.GetType()));
        object controller = constructor.Invoke(new object[] { prepare, jobs, new Func<long>(Stopwatch.GetTimestamp), inspect });
        Type samplerType = _production.GetType("WinMemoryCleaner.TemperatureSampler", true), backendType = _production.GetType("WinMemoryCleaner.ISensorBackend", true);
        object sampler = samplerType.GetConstructor(Instance, null, new[] { backendType }, null).Invoke(new[] { controller });
        IResourceMonitor monitor = (IResourceMonitor)typeof(ResourceMonitor).GetConstructor(Instance, null, new[] { samplerType }, null).Invoke(new[] { sampler });
        var owner = new Owner { Number = Interlocked.Increment(ref _ownerNumber), Controller = controller, Sampler = sampler, Monitor = monitor };
        lock (OwnersGate) Owners.Add(owner);
        Check("controller-" + owner.Number + "-keeps-genuine-driver-delegate", Object.ReferenceEquals(((Delegate)Field(controller, "_inspect")).DynamicInvoke(), _driver.GenuineState) && _driver.BeforeOverride, Json(DriverEvidence()));
        Event("real-monitor-created", new Dictionary<string, object> { { "owner", owner.Number }, { "payloadRoot", _cache }, { "defaultFallbacks", "actual ASUS/NVAPI and actual Libre/Open WMI; no substitutes" } });
        return monitor;
    }
    private static void NewWindow()
    {
        Func<IResourceMonitor> factory = CreateMonitor;
        _window = (MainWindow)typeof(MainWindow).GetConstructor(Instance, null, new[] { typeof(Func<IResourceMonitor>) }, null).Invoke(new object[] { factory });
        _window.ShowActivated = false; _window.ShowInTaskbar = false; _app.MainWindow = _window; _model = _window.Monitoring;
        // Keep the verification shell off the user's desktop while exercising real visibility and state changes.
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = SystemParameters.VirtualScreenLeft - _window.Width - 64; _window.Top = SystemParameters.VirtualScreenTop;
        _window.StateChanged += delegate { Event("owned-window-state-changed", new Dictionary<string, object> { { "state", _window.WindowState.ToString() }, { "visible", _window.IsVisible }, { "compact", Settings.CompactMode }, { "page", Field(_window, "_page") }, { "mainConsumer", Field(_model, "_mainVisible") }, { "phase", _phase } }); };
        _window.IsVisibleChanged += delegate { Event("owned-window-visibility-changed", new Dictionary<string, object> { { "state", _window.WindowState.ToString() }, { "visible", _window.IsVisible }, { "compact", Settings.CompactMode }, { "page", Field(_window, "_page") }, { "mainConsumer", Field(_model, "_mainVisible") }, { "phase", _phase } }); };
    }
    private static void SeedSettings(RegistryKey root)
    {
        using (RegistryKey settings = root.CreateSubKey(Constants.App.Registry.Key.Settings))
        {
            settings.SetValue("Language", "zh-Hans"); settings.SetValue("FontSize", 14);
            foreach (string name in new[] { "CreateStartMenuShortcut", "RunOnStartup", "AutoUpdate", "AutoOptimizationInterval", "AutoOptimizationMemoryUsage", "UseHotkey", "CompactMode", "StartMinimized", "CloseAfterOptimization", "ShowVirtualMemory", "AlwaysOnTop" }) settings.SetValue(name, 0);
            settings.SetValue("CloseToTheNotificationArea", 1); settings.SetValue("TrayIconShowMemoryUsage", 1);
            // Literal independent fixture: real overlay; both temperatures plus CPU/GPU/memory.
            settings.SetValue("MonitoringOptions", "{\"RefreshSeconds\":2,\"OverlayEnabled\":false,\"ClickThrough\":true,\"Layout\":0,\"Screen\":\"\",\"Corner\":3,\"X\":16,\"Y\":16,\"Items\":31}");
        }
    }
    private static void LoadApplicationResources()
    {
        XDocument document = XDocument.Load(_xamlPath); XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement resources = document.Root.Element(presentation + "Application.Resources").Element(presentation + "ResourceDictionary");
        resources.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:WinMemoryCleaner"); resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"); resources.SetAttributeValue(XNamespace.Xmlns + "System", "clr-namespace:System;assembly=mscorlib");
        var context = new ParserContext { BaseUri = new Uri("pack://application:,,,/WinMemoryCleaner.Community;component/app.xaml", UriKind.Absolute) };
        _app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString().Replace("clr-namespace:WinMemoryCleaner", "clr-namespace:WinMemoryCleaner;assembly=WinMemoryCleaner.Community"), context);
    }
    private static void CloneRegistry(RegistryKey sourceRoot, RegistryKey targetRoot, string path)
    {
        using (RegistryKey source = sourceRoot.OpenSubKey(path))
        {
            if (source == null) return;
            using (RegistryKey target = targetRoot.CreateSubKey(path)) foreach (string name in source.GetValueNames()) target.SetValue(name, source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), source.GetValueKind(name));
            foreach (string child in source.GetSubKeyNames()) CloneRegistry(sourceRoot, targetRoot, path + "\\" + child);
        }
    }
    private static string ReadSettingsFingerprint()
    {
        var rows = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)) ReadRegistryValues(root, @"SOFTWARE\WinMemoryCleaner.Community", view.ToString(), rows);
        rows.Sort(StringComparer.Ordinal); return Json(rows);
    }
    private static void ReadRegistryValues(RegistryKey root, string path, string prefix, List<string> rows)
    {
        using (RegistryKey key = root.OpenSubKey(path))
        {
            if (key == null) { rows.Add(prefix + "|" + path + "|absent"); return; }
            foreach (string name in key.GetValueNames()) rows.Add(prefix + "|" + path + "|" + name + "|" + key.GetValueKind(name) + "|" + Json(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
            foreach (string child in key.GetSubKeyNames()) ReadRegistryValues(root, path + "\\" + child, prefix, rows);
        }
    }

    private static object Get(object value, string property) { Type type = value as Type; return (type ?? value.GetType()).GetProperty(property, type == null ? Instance : Static).GetValue(type == null ? value : null, null); }
    private static object Field(object value, string name) { return value.GetType().GetField(name, Instance).GetValue(value); }
    private static object Invoke(object value, string method, params object[] args) { return value.GetType().GetMethod(method, Instance).Invoke(value, args); }
    private static Owner CurrentOwner() { lock (OwnersGate) return Owners.Count == 0 ? null : Owners[Owners.Count - 1]; }
    private static Owner[] OwnerArray() { lock (OwnersGate) return Owners.ToArray(); }
    private static BackendFrame ReadBackend(Owner owner)
    {
        if (owner == null) return new BackendFrame { Status = "Disabled", Driver = "NotChecked" };
        object controller = owner.Controller;
        lock (Field(controller, "_gate"))
        {
            var frame = new BackendFrame { Pid = (int)Get(controller, "ProcessId"), Status = (string)Get(controller, "BackendStatus"), Driver = (string)Get(controller, "DriverStatus"), Diagnostic = (string)Get(controller, "Diagnostic"), Cpu = (MetricValue)Field(controller, "_cpu"), Gpu = (MetricValue)Field(controller, "_gpu"), Session = Field(controller, "_session") };
            if (frame.Session != null)
            {
                object freshness = Field(frame.Session, "Freshness");
                frame.Sequence = (long)Field(freshness, "_sequence"); frame.ReceivedTicks = (long)Field(freshness, "_received"); frame.SampleUtc = (DateTime)Field(freshness, "_sample");
                frame.Refresh = (int)Field(frame.Session, "Refresh"); frame.CpuEnabled = (bool)Field(frame.Session, "CpuEnabled"); frame.Abandoned = (bool)Field(frame.Session, "Abandoned");
                frame.Job = Field(frame.Session, "Job"); frame.Lease = Field(frame.Session, "Lease"); frame.Process = (Process)Field(frame.Session, "Process");
            }
            return frame;
        }
    }
    private static Child ObserveChild(Owner owner)
    {
        if (owner == null) return null;
        lock (Field(owner.Controller, "_gate"))
        {
            BackendFrame frame = ReadBackend(owner);
            if (frame.Pid == 0 || frame.Process == null || frame.Job == null || frame.Lease == null || frame.Abandoned || frame.Status != "Running") return null;
            if (frame.Process.HasExited) return null;
            if (frame.Process.Id != frame.Pid || frame.Pid == 23072 || frame.Pid == 14816 || frame.Pid == _self.Id) throw new InvalidOperationException("Unsafe controller process identity.");
            long creation = frame.Process.StartTime.ToUniversalTime().Ticks;
            lock (OwnersGate)
            {
                foreach (Child existing in Children) if (existing.Pid == frame.Pid && existing.CreationTicks == creation) return existing;
                string expected = (string)Get(frame.Lease, "Executable"); RequireScratch(expected);
                Process process = Process.GetProcessById(frame.Pid);
                try
                {
                    string actual = process.MainModule.FileName;
                    if (process.StartTime.ToUniversalTime().Ticks != creation || !String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || !actual.StartsWith(_cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !InOwnedJob(process, frame.Job)) throw new InvalidOperationException("Backend PID/creation/path/Job ownership did not match.");
                    var child = new Child { Owner = owner, Process = process, Pid = frame.Pid, CreationTicks = creation, Path = actual, Job = frame.Job };
                    Children.Add(child); process = null;
                    Event("owned-child-verified", ChildEvidence(child)); return child;
                }
                finally { if (process != null) process.Dispose(); }
            }
        }
    }
    private static bool InOwnedJob(Process process, object job)
    {
        SafeHandle handle = Field(job, "_handle") as SafeHandle;
        if (handle == null || handle.IsClosed || handle.IsInvalid) return false;
        bool member; return IsProcessInJob(process.Handle, handle.DangerousGetHandle(), out member) && member;
    }
    private static bool Alive(Child child)
    {
        try { return !child.Process.HasExited; } catch (InvalidOperationException) { return false; }
    }
    private static void GuardChild(Child child)
    {
        if (child == null || child.Pid == 23072 || child.Pid == 14816 || child.Pid == _self.Id) throw new InvalidOperationException("Protected or missing fault target.");
        bool known; lock (OwnersGate) known = Children.Contains(child) && Owners.Contains(child.Owner);
        BackendFrame frame = ReadBackend(child.Owner);
        child.Process.Refresh();
        if (!known || !Alive(child) || frame.Pid != child.Pid || frame.Process == null || frame.Process.Id != child.Pid || child.Process.StartTime.ToUniversalTime().Ticks != child.CreationTicks || !String.Equals(child.Process.MainModule.FileName, child.Path, StringComparison.OrdinalIgnoreCase) || !child.Path.StartsWith(_cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Object.ReferenceEquals(frame.Job, child.Job) || !InOwnedJob(child.Process, child.Job)) throw new InvalidOperationException("Refusing fault: controller ownership/PID/creation/scratch path/Job changed.");
    }
    private static int OwnedCount()
    {
        foreach (Owner owner in OwnerArray()) ObserveChild(owner);
        int count = 0; lock (OwnersGate) foreach (Child child in Children) if (Alive(child)) count++;
        _maxOwned = Math.Max(_maxOwned, count); return count;
    }
    private static Dictionary<string, object> ChildEvidence(Child child)
    { return new Dictionary<string, object> { { "pid", child.Pid }, { "creationUtcTicks", child.CreationTicks }, { "executable", child.Path }, { "controllerOwner", child.Owner.Number }, { "jobContained", true }, { "alive", Alive(child) } }; }

    private static void Ui(Action action)
    {
        Exception error = null; using (var done = new ManualResetEvent(false))
        {
            _app.Dispatcher.BeginInvoke(new Action(delegate { try { action(); } catch (Exception failure) { error = failure; } finally { done.Set(); } }), DispatcherPriority.Normal);
            if (!done.WaitOne(8000)) throw new TimeoutException("Owned window dispatcher did not respond in 8 seconds.");
        }
        if (error != null) throw new InvalidOperationException("Owned window action failed.", error);
    }
    private static void ProbeDispatcher(object state)
    {
        if (_finished || Interlocked.CompareExchange(ref _dispatchPending, 1, 0) != 0) return;
        long queued = Stopwatch.GetTimestamp(); string phase = _phase; double elapsed = EntryClock.Elapsed.TotalSeconds;
        bool measured = _mode == "performance" && _measureStart >= 0 && _demandClock != null && _demandClock.Elapsed.TotalSeconds > _measureStart;
        try
        {
            _app.Dispatcher.BeginInvoke(new Action(delegate
            {
                double latency = (Stopwatch.GetTimestamp() - queued) * 1000.0 / Stopwatch.Frequency;
                lock (EvidenceGate)
                {
                    Dispatches.Add(new DispatchRecord { Phase = phase, Elapsed = elapsed, Latency = latency, Measured = measured });
                    _dispatchCsv.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2},{3}", elapsed, latency, Csv(phase), measured ? 1 : 0));
                }
                Interlocked.Exchange(ref _dispatchPending, 0);
            }), DispatcherPriority.Input);
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _dispatchPending, 0); }
    }
    private static bool WaitFor(Func<bool> predicate, double seconds)
    {
        Stopwatch clock = Stopwatch.StartNew(); double nextSnapshot = 0;
        while (clock.Elapsed.TotalSeconds <= seconds)
        {
            OwnedCount(); if (predicate()) return true;
            if (clock.Elapsed.TotalSeconds >= nextSnapshot) { Ui(WriteSnapshot); nextSnapshot += 1; }
            Thread.Sleep(40);
        }
        return predicate();
    }
    private static bool WaitRunning(string name)
    {
        bool ready = WaitFor(delegate { return ReadBackend(CurrentOwner()).Status == "Running" && ObserveChild(CurrentOwner()) != null; }, 15);
        Check(name, ready, Json(BackendEvidence(ReadBackend(CurrentOwner())))); return ready;
    }
    private static void ObserveFor(double seconds)
    {
        Stopwatch clock = Stopwatch.StartNew(); double next = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            OwnedCount(); if (clock.Elapsed.TotalSeconds >= next) { Ui(WriteSnapshot); next += 1; } Thread.Sleep(40);
        }
        Ui(WriteSnapshot);
    }
    private static void ConfigureOverlay()
    {
        _model.ShowCpu = true; _model.ShowGpu = true; _model.ShowMemory = true; _model.ShowCpuTemperature = true; _model.ShowGpuTemperature = true;
        _model.RefreshSeconds = 2; _model.OverlayLayout = 0; _model.ClickThrough = true; _model.OverlayEnabled = true;
    }

    private static void Functional()
    {
        try
        {
            _phase = "initial-production-window";
            if (WaitRunning("real-production-backend-started"))
            {
                BackendFrame initial = ReadBackend(CurrentOwner());
                Check("parent-keeps-genuine-driver-status", initial.Driver == (string)Get(_driver.GenuineState, "Status"), Json(BackendEvidence(initial)));
                Check("unverified-shared-driver-is-not-not-installed", !(bool)Get(_driver.GenuineState, "ServicePresent") || initial.Driver != "NotInstalled", Json(DriverEvidence()));
                Check("untrusted-driver-does-not-enable-universal-CPU", (bool)Get(_driver.GenuineState, "CpuAllowed") || (!initial.CpuEnabled && (initial.Cpu == null || initial.Cpu.Status != MetricStatus.Available)), Json(BackendEvidence(initial)));
                Check("child-genuine-shared-driver-detail", !(bool)Get(_driver.GenuineState, "ServicePresent") || (initial.Cpu != null && initial.Cpu.Detail.IndexOf("driver=NotInstalled", StringComparison.Ordinal) < 0 && initial.Cpu.Detail.IndexOf("driver=", StringComparison.Ordinal) >= 0), Json(Metric(initial.Cpu)));
                CheckOrUnavailable("physical-universal-CPU", initial.CpuEnabled && Valid(initial.Cpu), "No installation authorized. Universal CPU cannot be credited from ASUS/WMI/NVAPI fallback. " + Json(Metric(initial.Cpu)));
                CheckOrUnavailable("physical-universal-GPU", Valid(initial.Gpu) && initial.Gpu.Detail.StartsWith("LibreHardwareMonitor:", StringComparison.Ordinal), Json(Metric(initial.Gpu)));
            }
            foreach (int refresh in new[] { 1, 2, 5 }) VerifyRefresh(refresh);
            Ui(delegate { _window.SelectPage(0); CaptureWindow(_window, "overview-zh-dark.png"); ConfigureOverlay(); _window.SelectPage(2); });
            ObserveFor(2);
            Ui(delegate { VerifySettings("zh-Hans", Enums.Theme.Dark, "settings-zh-dark.png"); CancelInstaller("zh"); VerifySettings("en", Enums.Theme.Light, "settings-en-light.png"); CancelInstaller("en"); Localizer.Language = new Language(CultureInfo.GetCultureInfo("zh-Hans")); ThemeManager.Theme = Enums.Theme.Dark; _window.Hide(); });
            _phase = "hidden-shell-horizontal-overlay";
            BackendFrame beforeHide = ReadBackend(CurrentOwner()); Child hiddenChild = ObserveChild(CurrentOwner()); int counter = _model.TemperatureSampleCount;
            ObserveFor(7);
            BackendFrame afterHide = ReadBackend(CurrentOwner());
            Check("hidden-shell-demanded-overlay-keeps-updating", hiddenChild != null && afterHide.Pid == hiddenChild.Pid && afterHide.Sequence > beforeHide.Sequence && _model.TemperatureSampleCount > counter, Json(BackendEvidence(afterHide)));
            Ui(delegate { CaptureOverlay("overlay-horizontal.png", 0); _model.OverlayLayout = 1; }); ObserveFor(1);
            Ui(delegate { CaptureOverlay("overlay-vertical.png", 1); _model.OverlayLayout = 0; });
            VerifyStop("temperature-flags-disabled", delegate { _model.ShowCpuTemperature = false; _model.ShowGpuTemperature = false; });
            Ui(delegate { _model.ShowCpuTemperature = true; _model.ShowGpuTemperature = true; }); WaitRunning("resume-after-temperature-disable");
            VerifyStop("hidden-shell-no-demand", delegate { _model.OverlayEnabled = false; });
            counter = _model.TemperatureSampleCount; ObserveFor(3);
            Check("no-demand-does-not-start-or-sample", OwnedCount() == 0 && _model.TemperatureSampleCount == counter, "temperature counter before=" + counter + "; after=" + _model.TemperatureSampleCount);
            Ui(delegate { _window.WindowState = WindowState.Normal; _window.Show(); _window.SelectPage(0); }); WaitRunning("resume-visible-window");
            VerifyStop("settings-page-pauses-without-overlay", delegate { _window.SelectPage(2); });
            Ui(delegate { _window.SelectPage(0); }); WaitRunning("resume-after-settings-pause");
            VerifyStop("minimized-shell-pauses-without-overlay", delegate { _window.WindowState = WindowState.Minimized; });
            Ui(delegate { _window.WindowState = WindowState.Normal; _window.SelectPage(0); }); WaitRunning("resume-after-minimized-pause");
            VerifyFaultSuspend(); VerifyFaultExit();
            VerifyClose("initial-host-window-exit");
            for (int repeat = 1; repeat <= 2; repeat++)
            {
                _phase = "repeated-start-" + repeat;
                Ui(delegate { NewWindow(); _window.Show(); _window.SelectPage(0); });
                WaitRunning("repeated-start-" + repeat + "-running"); ObserveFor(3); VerifyClose("repeated-start-" + repeat + "-exit");
            }
            Check("never-more-than-one-owned-backend", _maxOwned <= 1, "maximum observed owned live children=" + _maxOwned);
            _phase = "functional-end"; Ui(WriteSnapshot);
            _summary = BaseSummary(); _summary["functionalChecks"] = CheckCounts(); _summary["providers"] = ProviderArray(); _summary["maximumOwnedBackends"] = _maxOwned;
            lock (EvidenceGate) _summary["dispatcherP95Ms"] = Percentile(DispatchLatencies(null, false), .95);
            _exitCode = FailedChecks() == 0 ? 0 : 1; _finished = true;
            Ui(delegate { _dispatchTimer.Change(Timeout.Infinite, Timeout.Infinite); _app.Shutdown(_exitCode); });
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_output, "functional-error.txt"), error.ToString());
            Check("functional-run-completed", false, error.ToString());
            _exitCode = 3; _finished = false;
            try { Ui(delegate { _app.Shutdown(3); }); } catch (Exception) { }
        }
    }
    private static void VerifyRefresh(int seconds)
    {
        _phase = "refresh-" + seconds + "s"; Ui(delegate { _model.RefreshSeconds = seconds; });
        Owner owner = CurrentOwner(); Child child = ObserveChild(owner); BackendFrame start = ReadBackend(owner);
        long last = start.Sequence; int advances = 0; bool finite = true; bool identity = child != null;
        Stopwatch clock = Stopwatch.StartNew(); double next = 0;
        while (clock.Elapsed.TotalSeconds < seconds * 3 + 2)
        {
            int live = OwnedCount(); BackendFrame frame = ReadBackend(owner);
            if (frame.Sequence > last) { advances++; last = frame.Sequence; }
            if (frame.Gpu != null && frame.Gpu.Status == MetricStatus.Available && !Valid(frame.Gpu)) finite = false;
            if (frame.Cpu != null && frame.Cpu.Status == MetricStatus.Available && !Valid(frame.Cpu)) finite = false;
            if (child == null || frame.Pid != child.Pid || !Alive(child) || live > 1) identity = false;
            if (clock.Elapsed.TotalSeconds >= next) { Ui(WriteSnapshot); next += 1; } Thread.Sleep(40);
        }
        BackendFrame end = ReadBackend(owner);
        Check("refresh-" + seconds + "s-continuous-real-frames", advances >= 2 && end.Refresh == seconds, "sequence advances=" + advances + "; duration=" + clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "; " + Json(BackendEvidence(end)));
        Check("refresh-" + seconds + "s-does-not-restart-or-overlap", identity, child == null ? "No owned child available." : Json(ChildEvidence(child)));
        Check("refresh-" + seconds + "s-all-available-temperatures-finite-1-to-150", finite, Json(BackendEvidence(end)));
    }
    private static void VerifyStop(string name, Action action)
    {
        _phase = name; Child child = ObserveChild(CurrentOwner()); Stopwatch clock = Stopwatch.StartNew();
        bool uiInvalid = false; bool cacheInvalid = false; double actionMs = 0;
        Ui(delegate
        {
            Stopwatch ui = Stopwatch.StartNew(); action(); actionMs = ui.Elapsed.TotalMilliseconds;
            ResourceSnapshot last = Field(_model, "_last") as ResourceSnapshot;
            uiInvalid = last != null && Disabled(last.CpuTemperature) && Disabled(last.GpuTemperature);
            BackendFrame frame = ReadBackend(CurrentOwner()); cacheInvalid = frame.Status == "Disabled" && Disabled(frame.Cpu) && Disabled(frame.Gpu);
            WriteSnapshot();
        });
        Check(name + "-UI-cache-invalidated-synchronously", uiInvalid && cacheInvalid, "actionMs=" + actionMs.ToString("F6", CultureInfo.InvariantCulture) + "; backend=" + Json(BackendEvidence(ReadBackend(CurrentOwner()))));
        bool gone = child == null || child.Process.WaitForExit(5000); double exitMs = clock.Elapsed.TotalMilliseconds;
        Check(name + "-owned-child-exits-within-2s", child != null && gone && exitMs <= 2000, "measured milliseconds from demand change dispatch=" + exitMs.ToString("F6", CultureInfo.InvariantCulture) + "; child=" + (child == null ? "absent" : Json(ChildEvidence(child))));
        bool cleared = WaitFor(delegate { return ReadBackend(CurrentOwner()).Pid == 0 && OwnedCount() == 0; }, 5);
        Check(name + "-ownership-cleared", cleared, Json(BackendEvidence(ReadBackend(CurrentOwner()))));
    }
    private static void VerifyClose(string name)
    {
        _phase = name; Child child = ObserveChild(CurrentOwner()); Stopwatch clock = Stopwatch.StartNew();
        Ui(delegate { _window.Close(); });
        bool gone = child == null || child.Process.WaitForExit(5000); double exitMs = clock.Elapsed.TotalMilliseconds;
        bool clear = WaitFor(delegate { return OwnedCount() == 0; }, 5);
        Check(name + "-no-owned-orphan", gone && clear && exitMs <= 2000, "actual child exit wait milliseconds=" + exitMs.ToString("F6", CultureInfo.InvariantCulture));
    }
    private static void VerifyFaultSuspend()
    {
        _phase = "fault-suspend"; Ui(delegate { _model.RefreshSeconds = 2; _window.SelectPage(0); });
        if (!WaitRunning("fault-suspend-real-ready")) { Check("fault-suspend-exercised", false, "No real owned backend ready."); return; }
        Owner owner = CurrentOwner(); Child child = ObserveChild(owner); BackendFrame before = ReadBackend(owner);
        lock (Field(owner.Controller, "_gate")) { GuardChild(child); int status = NtSuspendProcess(child.Process.Handle); if (status != 0) throw new InvalidOperationException("NtSuspendProcess failed: " + status); }
        Event("guarded-owned-child-suspended", ChildEvidence(child)); Stopwatch faultClock = Stopwatch.StartNew();
        ObserveFor(3); BackendFrame early = ReadBackend(owner);
        Check("suspended-backend-stops-new-frames", early.Sequence == before.Sequence, Json(BackendEvidence(early)));
        bool invalid = WaitFor(delegate { return ReadBackend(owner).Status == "Error"; }, 14); BackendFrame failed = ReadBackend(owner);
        double invalidMs = faultClock.Elapsed.TotalMilliseconds;
        Check("suspended-backend-expires-universal-cache", invalid && !Valid(failed.Cpu) && !Valid(failed.Gpu), "faultElapsedMs=" + invalidMs.ToString("F3", CultureInfo.InvariantCulture) + "; " + Json(BackendEvidence(failed)));
        bool gone = child.Process.WaitForExit(5000); double goneMs = faultClock.Elapsed.TotalMilliseconds;
        Check("suspended-backend-is-job-cleaned", gone, "total fault-to-exit milliseconds=" + goneMs.ToString("F3", CultureInfo.InvariantCulture) + "; after observed invalidation=" + (goneMs - invalidMs).ToString("F3", CultureInfo.InvariantCulture));
        ObserveFor(3); Ui(delegate { VerifyFallback("suspended-backend-fresh-fallback-or-explicit-error"); CaptureWindow(_window, "fault-suspend-overview.png"); });
        Check("suspended-backend-UI-dispatch-p95-at-most-50ms", Percentile(DispatchLatencies("fault-suspend", false), .95) <= 50, "p95ms=" + Percentile(DispatchLatencies("fault-suspend", false), .95).ToString("F6", CultureInfo.InvariantCulture));
        _phase = "fault-backoff"; ObserveFor(10);
        Check("real-fault-backoff-no-immediate-restart", OwnedCount() == 0 && ReadBackend(owner).Status == "Error", "faultElapsedSeconds=" + faultClock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture));
        bool restarted = WaitFor(delegate { return ReadBackend(owner).Status == "Running" && ObserveChild(owner) != null; }, 25);
        Check("real-30-second-backoff-restarts-once", restarted && OwnedCount() == 1 && ObserveChild(owner) != child && faultClock.Elapsed.TotalSeconds >= 29, "faultElapsedSeconds=" + faultClock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "; " + Json(BackendEvidence(ReadBackend(owner))));
    }
    private static void VerifyFaultExit()
    {
        _phase = "fault-exit";
        if (!WaitRunning("fault-exit-real-ready")) { Check("fault-exit-exercised", false, "No real owned backend ready."); return; }
        Owner owner = CurrentOwner(); Child child = ObserveChild(owner); Stopwatch clock = Stopwatch.StartNew();
        lock (Field(owner.Controller, "_gate")) { GuardChild(child); Event("guarded-owned-child-kill", ChildEvidence(child)); child.Process.Kill(); }
        bool exited = child.Process.WaitForExit(5000); bool invalid = WaitFor(delegate { return ReadBackend(owner).Status == "Error"; }, 5);
        BackendFrame failed = ReadBackend(owner);
        Check("forced-owned-exit-invalidates-cache", exited && invalid && !Valid(failed.Cpu) && !Valid(failed.Gpu), "faultElapsedMs=" + clock.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "; " + Json(BackendEvidence(failed)));
        ObserveFor(5); Ui(delegate { VerifyFallback("forced-owned-exit-fresh-fallback-or-explicit-error"); CaptureWindow(_window, "fault-exit-overview.png"); });
        Check("forced-owned-exit-UI-dispatch-p95-at-most-50ms", Percentile(DispatchLatencies("fault-exit", false), .95) <= 50, "p95ms=" + Percentile(DispatchLatencies("fault-exit", false), .95).ToString("F6", CultureInfo.InvariantCulture));
        Check("forced-owned-exit-no-unmanaged-child", OwnedCount() == 0, Json(BackendEvidence(ReadBackend(owner))));
    }
    private static void VerifyFallback(string name)
    {
        ResourceSnapshot snapshot = Field(_model, "_last") as ResourceSnapshot;
        bool correct = snapshot != null && CorrectFallback(snapshot.CpuTemperature) && CorrectFallback(snapshot.GpuTemperature);
        Check(name, correct, snapshot == null ? "No UI snapshot." : Json(new Dictionary<string, object> { { "cpu", Metric(snapshot.CpuTemperature) }, { "gpu", Metric(snapshot.GpuTemperature) }, { "backend", snapshot.SensorBackendStatus } })); WriteSnapshot();
    }
    private static bool CorrectFallback(MetricValue metric)
    {
        if (metric == null) return false;
        if (metric.Status == MetricStatus.Available) return Valid(metric) && !metric.Detail.StartsWith("LibreHardwareMonitor:", StringComparison.Ordinal) && metric.Detail.IndexOf("fallback:", StringComparison.Ordinal) >= 0;
        return !metric.Value.HasValue && (metric.Status == MetricStatus.Error || metric.Status == MetricStatus.AccessDenied || metric.Status == MetricStatus.Unsupported);
    }

    private static void VerifySettings(string culture, Enums.Theme theme, string screenshot)
    {
        Localizer.Language = new Language(CultureInfo.GetCultureInfo(culture)); ThemeManager.Theme = theme; _window.SelectPage(2); _window.UpdateLayout();
        TextBlock status = _window.FindName("TemperatureStatus") as TextBlock; Button install = _window.FindName("InstallTemperatureDriver") as Button;
        bool active = status != null && install != null && status.GetBindingExpression(TextBlock.TextProperty).Status == BindingStatus.Active && status.GetBindingExpression(FrameworkElement.ToolTipProperty).Status == BindingStatus.Active && install.GetBindingExpression(UIElement.IsEnabledProperty).Status == BindingStatus.Active && status.Text == _model.TemperatureStatusText && Convert.ToString(install.Content, CultureInfo.InvariantCulture) == _model.Labels["InstallSensorDriver"];
        Check("status-settings-" + culture + "-" + theme, active, status == null ? "Missing status TextBlock." : status.Text + " | " + install.Content);
        var layout = _window.FindName("OverlayLayoutChoice") as ComboBox;
        var corner = _window.FindName("OverlayCornerChoice") as ComboBox;
        var bindings = new DispatcherFrame(); _app.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { bindings.Continue = false; })); Dispatcher.PushFrame(bindings); _window.UpdateLayout();
        Check("layout-choice-language-" + culture, layout != null && layout.SelectedIndex == _model.OverlayLayout && layout.Text == _model.Labels[_model.OverlayLayout == 1 ? "Vertical" : "Horizontal"], layout == null ? "Missing layout choice." : layout.Text);
        string[] cornerKeys = { "TopLeft", "TopRight", "BottomLeft", "BottomRight", "Custom" };
        Check("corner-choice-language-" + culture, corner != null && corner.SelectedIndex == _model.OverlayCorner && corner.Text == _model.Labels[cornerKeys[_model.OverlayCorner]], corner == null ? "Missing corner choice." : corner.Text);
        CaptureWindow(_window, screenshot);
    }
    private static void CancelInstaller(string suffix)
    {
        Button install = _window.FindName("InstallTemperatureDriver") as Button;
        if (install == null) { Check("install-confirmation-cancel-" + suffix, false, "Missing real button."); return; }
        bool cancelled = false; string prompt = null; Stopwatch timeout = Stopwatch.StartNew();
        var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        cancel.Tick += delegate
        {
            foreach (Window dialog in _app.Windows)
            {
                if (!(dialog is MessageDialog) || dialog.Owner != _window || !dialog.IsVisible) continue;
                GuardWindow(dialog); prompt = Convert.ToString(Get(dialog.DataContext, "Message"), CultureInfo.InvariantCulture);
                dialog.UpdateLayout();
                Check("install-confirmation-readable-width-" + suffix, dialog.ActualWidth <= Math.Min(600, SystemParameters.WorkArea.Width - 32), "width=" + dialog.ActualWidth + "; height=" + dialog.ActualHeight + "; workArea=" + SystemParameters.WorkArea);
                CaptureWindow(dialog, "install-confirmation-cancel-" + suffix + ".png");
                cancelled = true; cancel.Stop(); dialog.DialogResult = false; break;
            }
            if (timeout.Elapsed.TotalSeconds > 5) { cancel.Stop(); throw new TimeoutException("No owned cancellable confirmation dialog appeared."); }
        };
        cancel.Start(); try { install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } finally { cancel.Stop(); }
        Check("install-confirmation-cancel-" + suffix + "-no-installer", cancelled && Field(_model, "_installer") == null && !_model.IsInstallingDriver, prompt ?? "No owned dialog was cancelled.");
    }
    private static void GuardWindow(Window window)
    {
        bool known = window == _window || (window is MessageDialog && window.Owner == _window) || (window is MonitoringOverlay && Field(_window, "_overlay") == window);
        uint pid; IntPtr handle = new WindowInteropHelper(window).Handle;
        if (!known || handle == IntPtr.Zero || GetWindowThreadProcessId(handle, out pid) == 0 || pid != (uint)_self.Id) throw new InvalidOperationException("Refusing capture/action on a foreign window.");
    }
    private static void CaptureWindow(Window window, string name)
    {
        GuardWindow(window); window.UpdateLayout();
        if (!window.IsVisible || window.ActualWidth < 1 || window.ActualHeight < 1) throw new InvalidOperationException("Owned screenshot target has no visible rendered area.");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (Stream output = File.Create(Path.Combine(_output, name))) png.Save(output);
        Event("owned-window-render-capture", new Dictionary<string, object> { { "path", Path.Combine(_output, name) }, { "width", window.ActualWidth }, { "height", window.ActualHeight }, { "hostPid", _self.Id }, { "type", window.GetType().FullName }, { "desktopCapture", false } });
    }
    private static void CaptureOverlay(string name, int layout)
    {
        Window overlay = Field(_window, "_overlay") as Window; if (overlay == null) { Check(name + "-rendered", false, "No owned overlay."); return; }
        CaptureWindow(overlay, name); int style = GetWindowLong(new WindowInteropHelper(overlay).Handle, -20);
        Check(name + "-compact-layout-and-noactivate-through", _model.OverlayLayout == layout && overlay.ActualWidth > 0 && overlay.ActualHeight > 0 && (style & 0x08000000) != 0 && (style & 0x20) != 0, "layout=" + layout + "; width=" + overlay.ActualWidth + "; height=" + overlay.ActualHeight + "; extendedStyle=" + style);
    }

    private static void StartPerformance()
    {
        ConfigureOverlay(); _window.Hide(); _phase = "performance-warmup"; _demandClock = Stopwatch.StartNew();
        _self.Refresh(); _lastMainCpu = _self.TotalProcessorTime.TotalSeconds;
        _csv = Writer("samples.csv");
        _csv.WriteLine("elapsedDemandSeconds,elapsedEntrySeconds,mainHostCpuMachinePercent,backendCpuMachinePercent,combinedCpuMachinePercent,mainPrivateBytes,backendPrivateBytes,combinedPrivateBytes,mainWorkingSetBytes,backendWorkingSetBytes,ownedCount,backendPid,backendCreationUtcTicks,backendExecutable,measurement,backendSequence,backendStatus,driverStatus,backendCpuTotalSeconds,mainCpuTotalSeconds");
        _sampleTimer = new System.Threading.Timer(PerformanceSample, null, 1000, 1000);
        Event("performance-demand-configured", new Dictionary<string, object> { { "hiddenShell", !_window.IsVisible }, { "overlay", _model.OverlayEnabled }, { "cpuTemperatureFlag", _model.ShowCpuTemperature }, { "gpuTemperatureFlag", _model.ShowGpuTemperature }, { "refreshSeconds", _model.RefreshSeconds }, { "warmup", _warmup }, { "measurement", _seconds }, { "trial", _trial }, { "hostOverheadIncluded", true } });
    }
    private static void PerformanceSample(object state)
    {
        if (Interlocked.Exchange(ref _sampling, 1) != 0) return;
        try
        {
            double elapsed = _demandClock.Elapsed.TotalSeconds, interval = Math.Max(.001, elapsed - _lastSampleTime);
            _self.Refresh(); double totalMainCpu = _self.TotalProcessorTime.TotalSeconds; long mainPrivate = _self.PrivateMemorySize64;
            int live = OwnedCount(); Child current = ObserveChild(CurrentOwner());
            double deltaBackend = 0, backendTotal = 0; long backendPrivate = 0, backendWorking = 0;
            lock (OwnersGate)
            {
                foreach (Child child in Children)
                {
                    bool alive = Alive(child);
                    if (!alive && child.FinalCpuCollected) { backendTotal += child.LastCpu; continue; }
                    child.Process.Refresh(); double total = child.Process.TotalProcessorTime.TotalSeconds;
                    deltaBackend += Math.Max(0, total - child.LastCpu); child.LastCpu = total; backendTotal += total;
                    if (alive) { backendPrivate += child.Process.PrivateMemorySize64; backendWorking += child.Process.WorkingSet64; } else child.FinalCpuCollected = true;
                }
            }
            double mainCpu = Math.Max(0, totalMainCpu - _lastMainCpu) / interval / Environment.ProcessorCount * 100;
            double backendCpu = deltaBackend / interval / Environment.ProcessorCount * 100;
            BackendFrame frame = ReadBackend(CurrentOwner());
            if (_firstRunningSeconds < 0 && frame.Status == "Running") _firstRunningSeconds = elapsed;
            bool boundary = _measureStart < 0 && elapsed >= _warmup;
            if (boundary)
            {
                _measureStart = elapsed; _measureMainCpu = totalMainCpu; _measureBackendCpu = backendTotal; _measurementChild = current;
                _baselineMainPrivate = mainPrivate; _baselineBackendPrivate = backendPrivate; _phase = "performance-measurement";
                Event("warmup-boundary", new Dictionary<string, object> { { "actualWarmupSeconds", elapsed }, { "mainPrivateBytes", mainPrivate }, { "backendPrivateBytes", backendPrivate }, { "backend", BackendEvidence(frame) }, { "child", current == null ? null : ChildEvidence(current) } });
            }
            bool measured = _measureStart >= 0 && !boundary;
            _csv.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F9},{3:F9},{4:F9},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18:F6},{19:F6}", elapsed, EntryClock.Elapsed.TotalSeconds, mainCpu, backendCpu, mainCpu + backendCpu, mainPrivate, backendPrivate, mainPrivate + backendPrivate, _self.WorkingSet64, backendWorking, live, current == null ? 0 : current.Pid, current == null ? 0 : current.CreationTicks, Csv(current == null ? "" : current.Path), measured ? 1 : 0, frame.Sequence, Csv(frame.Status), Csv(frame.Driver), backendTotal, totalMainCpu)); _csv.Flush();
            if (measured)
            {
                MainCpu.Add(mainCpu); BackendCpu.Add(backendCpu); CombinedCpu.Add(mainCpu + backendCpu); MainPrivate.Add(mainPrivate); BackendPrivate.Add(backendPrivate); CombinedPrivate.Add(mainPrivate + backendPrivate);
                _endMainPrivate = mainPrivate; _endBackendPrivate = backendPrivate; _measurementOwnedMinimum = Math.Min(_measurementOwnedMinimum, live);
                if (current != _measurementChild || live != 1) Check("performance-owned-identity-at-" + elapsed.ToString("F3", CultureInfo.InvariantCulture), false, "Backend changed/missing during measurement.");
            }
            _lastSampleTime = elapsed; _lastMainCpu = totalMainCpu; _lastBackendTotal = backendTotal;
            _app.Dispatcher.BeginInvoke(new Action(WriteSnapshot), DispatcherPriority.Background);
            if (CanFinishPerformance(elapsed))
            {
                _sampleTimer.Change(Timeout.Infinite, Timeout.Infinite); _dispatchTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _app.Dispatcher.BeginInvoke(new Action(FinishPerformance), DispatcherPriority.ContextIdle);
            }
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(_output, "sampling-errors.log"), error + Environment.NewLine);
            Check("performance-sampling-error", false, error.ToString());
        }
        finally { Interlocked.Exchange(ref _sampling, 0); }
    }
    private static bool CanFinishPerformance(double elapsed)
    { return _measureStart >= 0 && elapsed - _measureStart >= _seconds && MainCpu.Count >= _seconds; }
    private static void FinishPerformance()
    {
        double[] latencies = DispatchLatencies(null, true); double p95 = Percentile(latencies, .95), median = Percentile(CombinedCpu.ToArray(), .5);
        Check("performance-actual-measured-duration", _lastSampleTime - _measureStart >= _seconds && MainCpu.Count >= _seconds, "measuredSeconds=" + (_lastSampleTime - _measureStart).ToString("F6", CultureInfo.InvariantCulture) + "; samples=" + MainCpu.Count);
        Check("performance-one-actual-backend-throughout", _measurementChild != null && _measurementOwnedMinimum == 1 && _maxOwned == 1, _measurementChild == null ? "No owned backend at warmup boundary." : Json(ChildEvidence(_measurementChild)));
        Check("performance-combined-CPU-median-strictly-below-1-percent", median >= 0 && median < 1, "medianMachinePercent=" + median.ToString("F9", CultureInfo.InvariantCulture));
        Check("performance-UI-dispatch-p95-at-most-50ms", latencies.Length >= 200 && p95 >= 0 && p95 <= 50, "p95ms=" + p95.ToString("F9", CultureInfo.InvariantCulture) + "; observations=" + latencies.Length);
        long growth = _endMainPrivate + _endBackendPrivate - _baselineMainPrivate - _baselineBackendPrivate;
        if (_seconds == 600) Check("retention-combined-private-growth-at-most-10MiB", growth <= 10L * 1024 * 1024, "growthBytes=" + growth + "; baseline is end of actual 30s warmup; no forced GC or heap manipulation.");
        _summary = BaseSummary();
        _summary["trial"] = _trial; _summary["warmupSeconds"] = _warmup; _summary["requestedMeasurementSeconds"] = _seconds;
        _summary["actualWarmupSeconds"] = _measureStart; _summary["actualMeasurementSeconds"] = _lastSampleTime - _measureStart; _summary["samples"] = MainCpu.Count;
        _summary["mainHost"] = Metrics(MainCpu, MainPrivate, _baselineMainPrivate, _endMainPrivate, (_lastMainCpu - _measureMainCpu) / (_lastSampleTime - _measureStart) / Environment.ProcessorCount * 100);
        _summary["backend"] = Metrics(BackendCpu, BackendPrivate, _baselineBackendPrivate, _endBackendPrivate, (_lastBackendTotal - _measureBackendCpu) / (_lastSampleTime - _measureStart) / Environment.ProcessorCount * 100);
        _summary["combined"] = Metrics(CombinedCpu, CombinedPrivate, _baselineMainPrivate + _baselineBackendPrivate, _endMainPrivate + _endBackendPrivate, ((_lastMainCpu - _measureMainCpu) + (_lastBackendTotal - _measureBackendCpu)) / (_lastSampleTime - _measureStart) / Environment.ProcessorCount * 100);
        _summary["dispatcherP95Ms"] = p95; _summary["dispatcherObservations"] = latencies.Length; _summary["providers"] = ProviderArray(); _summary["maximumOwnedBackends"] = _maxOwned;
        _summary["measurementOwnedIdentity"] = _measurementChild == null ? null : ChildEvidence(_measurementChild); _summary["firstObservedBackendRunningAfterDemandSeconds"] = _firstRunningSeconds;
        _summary["startupHostTotalCpuSecondsAtEnd"] = _lastMainCpu; _summary["backendTotalCpuSecondsAtEnd"] = _lastBackendTotal;
        WriteSnapshot(); Save("summary-before-cleanup.json", _summary);
        _exitCode = FailedChecks() == 0 ? 0 : 1; _finished = true; _window.Close(); _app.Shutdown(_exitCode);
    }
    private static Dictionary<string, object> Metrics(List<double> cpu, List<long> memory, long baseline, long end, double mean)
    {
        double[] bytes = new double[memory.Count]; for (int i = 0; i < memory.Count; i++) bytes[i] = memory[i];
        return new Dictionary<string, object> { { "medianCpuMachinePercent", Percentile(cpu.ToArray(), .5) }, { "meanCpuMachinePercent", mean }, { "cpuP95MachinePercent", Percentile(cpu.ToArray(), .95) }, { "medianPrivateBytes", (long)Percentile(bytes, .5) }, { "warmupEndPrivateBytes", baseline }, { "endPrivateBytes", end }, { "privateGrowthBytes", end - baseline } };
    }
    private static double[] DispatchLatencies(string phase, bool measured)
    {
        var result = new List<double>(); lock (EvidenceGate) foreach (DispatchRecord record in Dispatches) if ((phase == null || record.Phase == phase) && (!measured || record.Measured)) result.Add(record.Latency); return result.ToArray();
    }
    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return -1; Array.Sort(values);
        if (percentile == .5) return values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2];
        return values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1)];
    }

    private static bool Valid(MetricValue metric) { return metric != null && metric.Status == MetricStatus.Available && metric.Value.HasValue && !Double.IsNaN(metric.Value.Value) && !Double.IsInfinity(metric.Value.Value) && metric.Value.Value >= 1 && metric.Value.Value <= 150; }
    private static bool Disabled(MetricValue metric) { return metric != null && metric.Status == MetricStatus.Disabled && !metric.Value.HasValue; }
    private static object Metric(MetricValue value) { return value == null ? null : new Dictionary<string, object> { { "status", value.Status.ToString() }, { "celsius", value.Value }, { "detail", value.Detail } }; }
    private static Dictionary<string, object> BackendEvidence(BackendFrame frame)
    { return new Dictionary<string, object> { { "pid", frame.Pid }, { "status", frame.Status }, { "driver", frame.Driver }, { "sequence", frame.Sequence }, { "receivedMonotonicTicks", frame.ReceivedTicks }, { "sampleUtc", frame.SampleUtc == DateTime.MinValue ? null : frame.SampleUtc.ToString("o", CultureInfo.InvariantCulture) }, { "refreshSeconds", frame.Refresh }, { "parentCpuEnabled", frame.CpuEnabled }, { "abandoned", frame.Abandoned }, { "cpu", Metric(frame.Cpu) }, { "gpu", Metric(frame.Gpu) }, { "diagnostic", frame.Diagnostic } }; }
    private static Dictionary<string, object> DriverEvidence()
    { return new Dictionary<string, object> { { "capturedBeforeHKLMOverride", _driver.BeforeOverride }, { "status", Get(_driver.GenuineState, "Status") }, { "servicePresent", Get(_driver.GenuineState, "ServicePresent") }, { "cpuAllowed", Get(_driver.GenuineState, "CpuAllowed") }, { "version", Convert.ToString(Get(_driver.GenuineState, "Version"), CultureInfo.InvariantCulture) }, { "inspection", "Actual production SensorDriverTrust.Inspect; captured object passed unchanged to actual controller. Child inspects real system independently." }, { "installationAuthorized", false }, { "installerExecuted", false } }; }
    private static Dictionary<string, object> BaseSummary()
    {
        return new Dictionary<string, object> { { "mode", _mode }, { "assembly", _assemblyPath }, { "assemblyVersion", _production.GetName().Version.ToString() }, { "assemblySha256", FileHash(_assemblyPath) }, { "applicationXaml", _xamlPath }, { "applicationXamlSha256", FileHash(_xamlPath) }, { "canonicalReleaseEvidence", false }, { "host", "Derived VerificationApp hosting real production MainWindow; exact App resources; actual isolated TemperatureSampler+ResourceMonitor+SensorBackendController" }, { "externalHarnessOverheadCountsInMainHost", true }, { "directProductionAppStartupMeasured", false }, { "logicalProcessors", Environment.ProcessorCount }, { "cpuNormalization", "Process.TotalProcessorTime delta / actual Stopwatch interval / all logical processors * 100" }, { "driver", DriverEvidence() }, { "payloadRoot", _cache }, { "unverifiedHardware", new[] { "Universal CPU on trusted/authorized PawnIO hardware", "AMD CPU physical hardware", "AMD GPU physical hardware", "Intel GPU physical hardware" } } };
    }
    private static void WriteSnapshot()
    {
        ResourceSnapshot selected = Field(_model, "_last") as ResourceSnapshot; BackendFrame backend = ReadBackend(CurrentOwner());
        var row = new Dictionary<string, object> { { "elapsedEntrySeconds", EntryClock.Elapsed.TotalSeconds }, { "utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "phase", _phase }, { "backend", BackendEvidence(backend) }, { "sampleCount", _model.SampleCount }, { "temperatureSampleCount", _model.TemperatureSampleCount }, { "processSampleCount", _model.ProcessSampleCount }, { "mainVisible", _window.IsVisible }, { "overlayEnabled", _model.OverlayEnabled }, { "refreshSeconds", _model.RefreshSeconds }, { "cpuTemperatureFlag", _model.ShowCpuTemperature }, { "gpuTemperatureFlag", _model.ShowGpuTemperature }, { "UIStatus", _model.TemperatureStatusText }, { "UIcpuTemperature", selected == null ? null : Metric(selected.CpuTemperature) }, { "UIgpuTemperature", selected == null ? null : Metric(selected.GpuTemperature) }, { "UIgpuDevice", selected == null ? null : selected.GpuName } };
        row["mainWindowState"] = _window.WindowState.ToString(); row["compactMode"] = Settings.CompactMode; row["mainConsumer"] = Field(_model, "_mainVisible"); row["selectedPage"] = Field(_window, "_page");
        lock (EvidenceGate)
        {
            if (backend.Cpu != null) Providers.Add("backend CPU: " + backend.Cpu.Status + " | " + backend.Cpu.Detail);
            if (backend.Gpu != null) Providers.Add("backend GPU: " + backend.Gpu.Status + " | " + backend.Gpu.Detail);
            if (selected != null) { Providers.Add("selected CPU: " + selected.CpuTemperature.Status + " | " + selected.CpuTemperature.Detail); Providers.Add("selected GPU: " + selected.GpuTemperature.Status + " | " + selected.GpuTemperature.Detail); }
            _snapshots.WriteLine(Json(row)); _snapshots.Flush(); _dispatchCsv.Flush();
        }
    }
    private static string[] ProviderArray() { lock (EvidenceGate) { string[] values = new string[Providers.Count]; Providers.CopyTo(values); Array.Sort(values, StringComparer.Ordinal); return values; } }
    private static void Check(string name, bool passed, string evidence)
    {
        lock (EvidenceGate) Checks.Add(new Dictionary<string, object> { { "name", name }, { "result", passed ? "passed" : "failed" }, { "elapsedEntrySeconds", EntryClock.Elapsed.TotalSeconds }, { "evidence", evidence } });
    }
    private static void CheckOrUnavailable(string name, bool available, string evidence)
    {
        lock (EvidenceGate) Checks.Add(new Dictionary<string, object> { { "name", name }, { "result", available ? "passed" : "unavailable" }, { "elapsedEntrySeconds", EntryClock.Elapsed.TotalSeconds }, { "evidence", evidence } });
    }
    private static int FailedChecks() { int result = 0; lock (EvidenceGate) foreach (var check in Checks) if ((string)check["result"] == "failed") result++; return result; }
    private static Dictionary<string, object> CheckCounts()
    {
        int passed = 0, failed = 0, unavailable = 0; lock (EvidenceGate) foreach (var check in Checks) { string result = (string)check["result"]; if (result == "passed") passed++; else if (result == "failed") failed++; else unavailable++; }
        return new Dictionary<string, object> { { "total", passed + failed + unavailable }, { "passed", passed }, { "failed", failed }, { "unavailable", unavailable } };
    }
    private static void Event(string name, object evidence)
    { lock (EvidenceGate) if (_events != null) { _events.WriteLine(Json(new Dictionary<string, object> { { "event", name }, { "elapsedEntrySeconds", EntryClock.Elapsed.TotalSeconds }, { "utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "evidence", evidence } })); _events.Flush(); } }
    private static Dictionary<string, object> ProcessCost()
    { _self.Refresh(); return new Dictionary<string, object> { { "hostPid", _self.Id }, { "hostCreationUtcTicks", _self.StartTime.ToUniversalTime().Ticks }, { "hostExecutable", _self.MainModule.FileName }, { "hostTotalCpuSeconds", _self.TotalProcessorTime.TotalSeconds }, { "hostPrivateBytes", _self.PrivateMemorySize64 }, { "hostWorkingSetBytes", _self.WorkingSet64 } }; }
    private static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = 16777216 }.Serialize(value); }
    private static StreamWriter Writer(string name) { return new StreamWriter(Path.Combine(_output, name), false, new UTF8Encoding(false)); }
    private static void Save(string name, object value) { File.WriteAllText(Path.Combine(_output, name), Json(value), new UTF8Encoding(false)); }
    private static string Csv(string text) { return "\"" + (text ?? String.Empty).Replace("\"", "\"\"") + "\""; }
    private static string FileHash(string path) { using (Stream input = File.OpenRead(path)) using (SHA256 hash = SHA256.Create()) { var text = new StringBuilder(); foreach (byte value in hash.ComputeHash(input)) text.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); } }

    private static void CleanupOwned()
    {
        if (_app != null && _window != null) { try { _window.Monitoring.Dispose(); } catch (Exception) { } }
        foreach (Owner owner in OwnerArray()) { try { ((IDisposable)owner.Controller).Dispose(); } catch (Exception error) { Event("controller-dispose-error", error.ToString()); } }
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 5 && OwnedCount() != 0) Thread.Sleep(10);
        foreach (Child child in Children)
        {
            if (!Alive(child)) continue;
            // Best-effort final Job closure only for a recorded controller-owned identity.
            // No PID/name-wide kill; if ownership no longer matches, refuse the action.
            lock (Field(child.Owner.Controller, "_gate"))
            {
                GuardChild(child); ((IDisposable)child.Job).Dispose(); Event("guarded-final-owned-Job-closure", ChildEvidence(child));
            }
            child.Process.WaitForExit(5000);
        }
        Check("final-owned-process-cleanup", OwnedCount() == 0, "cleanupElapsedMilliseconds=" + clock.Elapsed.TotalMilliseconds.ToString("F6", CultureInfo.InvariantCulture));
        if (Directory.Exists(_cache))
        {
            bool idle = WaitUntilLeasesReleased(5);
            if (idle)
            {
                try { RequireScratch(_cache); Directory.Delete(_cache, true); }
                catch (IOException error) { Event("scratch-payload-remove-error", error.ToString()); idle = false; }
                catch (UnauthorizedAccessException error) { Event("scratch-payload-remove-error", error.ToString()); idle = false; }
            }
            Check("scratch-payload-cache-removed", idle && !Directory.Exists(_cache), _cache);
        }
    }
    private static bool WaitUntilLeasesReleased(double seconds)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            bool released = true; foreach (Owner owner in OwnerArray()) if (ReadBackend(owner).Session != null) released = false;
            if (released) return true; Thread.Sleep(10);
        }
        return false;
    }
    private static Dictionary<string, object> CleanupEvidence()
    {
        var identities = new List<object>(); int alive = 0;
        lock (OwnersGate) foreach (Child child in Children) { identities.Add(ChildEvidence(child)); if (Alive(child)) alive++; }
        return new Dictionary<string, object> { { "ownedChildren", identities }, { "remainingOwnedChildren", alive }, { "registryPath", _registryKey }, { "registryOverrideRemoved", !_redirected }, { "scratchPayloadRemoved", !Directory.Exists(_cache) }, { "protectedPidsNeverActionTargets", new[] { 23072, 14816 } }, { "installerExecuted", false }, { "userCacheUsed", false } };
    }
}
