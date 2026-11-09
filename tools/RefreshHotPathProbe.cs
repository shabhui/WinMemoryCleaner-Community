// Deterministic allocation/draw probe. Compile with the same Framework/C#4 toolchain
// against a CommunityTestBuild Release assembly; never run production startup.
// Test builds use a separate settings identity. No real shell icon, hardware sampling,
// global memory cleaning, forced GC or working-set trimming is performed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using WinMemoryCleaner;
using Forms = System.Windows.Forms;

public static class RefreshHotPathProbe
{
    private static string _assemblyPath;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("assembly output-json"); return 2; }
        _assemblyPath = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Assembly.LoadFrom(_assemblyPath);
            if (File.Exists(args[1])) throw new InvalidOperationException("Choose a fresh output file.");
            Run(Path.GetFullPath(args[1]));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name == "WinMemoryCleaner.Community") return Assembly.LoadFrom(_assemblyPath);
        string path = Path.Combine(Path.GetDirectoryName(_assemblyPath), name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string output)
    {
        var testBuild = typeof(App).GetProperty("IsTestBuild", BindingFlags.Static | BindingFlags.NonPublic);
        if (testBuild == null || !(bool)testBuild.GetValue(null, null))
            throw new InvalidOperationException("Only isolated CommunityTestBuild assemblies are allowed.");
        AppDomain.MonitoringIsEnabled = true;
        Settings.Reset(true);
        Settings.UseHotkey = false;
        Settings.TrayIconShowMemoryUsage = true;
        Settings.ShowVirtualMemory = true;
        Localizer.Language = new Language(CultureInfo.GetCultureInfo("en"));
        var results = new Dictionary<string, object>();
        results["assembly"] = _assemblyPath;
        results["runtime"] = Environment.Version.ToString();
        results["pointerBytes"] = IntPtr.Size;
        results["testSettingsOnly"] = true;
        results["allocationCounter"] = "AppDomain.MonitoringTotalAllocatedMemorySize";
        results["forcedGc"] = false;
        using (var notifyIcon = new Forms.NotifyIcon())
        using (var notifications = new NotificationService(null))
        using (var hotkey = new HotkeyService())
        using (var cleaner = new MainViewModel(new ComputerService(), hotkey, notifications))
        using (var monitor = new MonitoringViewModel(cleaner, Dispatcher.CurrentDispatcher))
        {
            // Bypass Initialize solely to keep the probe's NotifyIcon invisible.
            typeof(NotificationService).GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(notifications, notifyIcon);
            var memory = new Memory();
            memory.Physical.Used.Percentage = 50;
            memory.Virtual.Used.Percentage = 40;
            for (int i = 0; i < 20; i++) notifications.Update(memory);
            int replacements = 0;
            var tray = Measure(3000, delegate(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var previous = notifyIcon.Icon;
                    notifications.Update(memory);
                    if (!ReferenceEquals(previous, notifyIcon.Icon)) replacements++;
                }
            });
            tray["iconReplacements"] = replacements;
            tray["visibleShellIcon"] = notifyIcon.Visible;
            results["unchangedTray"] = tray;

            replacements = 0;
            var tooltip = Measure(3000, delegate(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    memory.Virtual.Used.Percentage = 40 + i % 2;
                    var previous = notifyIcon.Icon;
                    notifications.Update(memory);
                    if (!ReferenceEquals(previous, notifyIcon.Icon)) replacements++;
                }
            });
            tooltip["iconReplacements"] = replacements;
            results["tooltipOnly"] = tooltip;

            // Publish fixed data without starting the sampling pump. Both old and new
            // implementations expose this language-refresh path and use it for bindings.
            typeof(MonitoringViewModel).GetField("_last", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(monitor,
                new ResourceSnapshot { CpuUsage = MetricValue.Available(12.5, "Fixture CPU") });
            typeof(MonitoringViewModel).GetMethod("OnLanguageChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(monitor,
                new object[] { null, new System.ComponentModel.PropertyChangedEventArgs(string.Empty) });
            int characters = 0;
            for (int i = 0; i < 100; i++) characters += monitor.CpuText.Length;
            var getters = Measure(100000, delegate(int count)
            {
                for (int i = 0; i < count; i++) characters += monitor.CpuText.Length;
            });
            getters["charactersReadIncludingWarmup"] = characters;
            getters["sampleCount"] = monitor.SampleCount;
            results["cpuTextGetter"] = getters;
        }
        Settings.Reset(true);
        File.WriteAllText(output, new JavaScriptSerializer().Serialize(results));
        Console.WriteLine(new JavaScriptSerializer().Serialize(results));
    }

    private static Dictionary<string, object> Measure(int iterations, Action<int> action)
    {
        var watch = new Stopwatch();
        long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        watch.Start();
        action(iterations);
        watch.Stop();
        long bytes = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - allocated;
        return new Dictionary<string, object>
        {
            { "iterations", iterations }, { "elapsedMs", watch.Elapsed.TotalMilliseconds },
            { "allocatedBytes", bytes }, { "gen0Collections", GC.CollectionCount(0) - gen0 },
            { "gen1Collections", GC.CollectionCount(1) - gen1 }, { "gen2Collections", GC.CollectionCount(2) - gen2 }
        };
    }
}
