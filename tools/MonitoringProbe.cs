// Read-only hardware evidence plus actions restricted to two children created here.
// No WMC startup, registry redirection, user settings or global memory cleanup.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

public static class MonitoringProbe
{
    private static Assembly _assembly;
    private static string _output;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
    private static object Get(object value, string property) { return value.GetType().GetProperty(property).GetValue(value, null); }
    private static void Save(string name, object value) { File.WriteAllText(Path.Combine(_output, name), Json.Serialize(value)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    // PIDs listed in WMC_PROTECTED_PIDS (comma-separated) are never eligible for any action,
    // even if a logic error would otherwise select them. Verification runs set this to the
    // user's existing application PIDs; owned children are guarded structurally as well.
    private static bool ProtectedPid(int id)
    {
        var configured = Environment.GetEnvironmentVariable("WMC_PROTECTED_PIDS");
        if (string.IsNullOrEmpty(configured)) return false;
        foreach (var part in configured.Split(','))
        {
            int value;
            if (int.TryParse(part.Trim(), out value) && value == id) return true;
        }
        return false;
    }

    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--owned-child")
        {
            var pages = new byte[64 * 1024 * 1024];
            for (int i = 0; i < pages.Length; i += 4096) pages[i] = 42;
            File.WriteAllText(args[1], Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(90000); GC.KeepAlive(pages); return 0;
        }
        if (args.Length != 2) { Console.Error.WriteLine("assembly output-directory"); return 2; }
        _output = Path.GetFullPath(args[1]); Directory.CreateDirectory(_output);
        try
        {
            _assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var monitor = Activator.CreateInstance(_assembly.GetType("WinMemoryCleaner.ResourceMonitor", true));
            try
            {
                var sample = monitor.GetType().GetMethod("Sample");
                var first = sample.Invoke(monitor, new object[] { true, true });
                Thread.Sleep(1200);
                var before = ProcessIds();
                var second = sample.Invoke(monitor, new object[] { true, true });
                var after = ProcessIds();
                var rows = ((IEnumerable)Get(second, "Processes")).Cast<object>().ToList();
                var sampledIds = new HashSet<int>(rows.Select(row => (int)Get(row, "Id")));
                var missing = before.Intersect(after).Where(id => !sampledIds.Contains(id)).ToArray();
                var summary = new Dictionary<string, object>();
                summary["timestampUtc"] = DateTime.UtcNow.ToString("o");
                summary["assembly"] = Path.GetFullPath(args[0]);
                summary["logicalProcessors"] = Environment.ProcessorCount;
                summary["registryRedirected"] = false;
                summary["enumeratedRows"] = rows.Count;
                summary["stablePidsMissing"] = missing;
                summary["duplicateNameGroups"] = rows.GroupBy(row => (string)Get(row, "Name")).Count(group => group.Count() > 1);
                summary["firstCpu"] = Get(first, "CpuUsage");
                foreach (string name in new[] { "CpuUsage", "GpuUsage", "CpuTemperature", "GpuTemperature", "GpuName" }) summary[name] = Get(second, name);
                Save("native-snapshot.json", second);
                summary["dxgiAdapters"] = ReadAdapters();
                Save("native-summary.json", summary);
                Require(missing.Length == 0, "A stable enumerable PID is missing from the native snapshot.");
                Require(sampledIds.Count == rows.Count, "Duplicate PID in native snapshot.");
                Require(Get(Get(first, "CpuUsage"), "Value") == null, "First CPU sample must not claim a measured zero.");
                summary["ownedProcessActions"] = CheckOwnedChildren(monitor);
                Save("native-summary.json", summary);
                Console.WriteLine("Native probe passed: " + rows.Count + " process rows; owned-child actions only.");
            }
            finally { ((IDisposable)monitor).Dispose(); }
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(_output, "native-error.txt"), error.ToString());
            Console.Error.WriteLine(error); return 1;
        }
    }
    private static HashSet<int> ProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var process in Process.GetProcesses()) { using (process) result.Add(process.Id); }
        return result;
    }
    private sealed class OwnedChild : IDisposable
    {
        internal readonly Process Process;
        internal readonly long Created;
        internal OwnedChild(string marker)
        {
            string executable;
            using (var self = Process.GetCurrentProcess()) executable = self.MainModule.FileName;
            Process = Process.Start(new ProcessStartInfo(executable, "--owned-child \"" + marker + "\"") { UseShellExecute = false, CreateNoWindow = true });
            // Retain the native process handle for exit checks and emergency cleanup.
            IntPtr retained = Process.Handle;
            Created = Process.StartTime.ToUniversalTime().ToFileTimeUtc();
            Require(!ProtectedPid(Process.Id) && retained != IntPtr.Zero, "Only a new verification child is eligible.");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(marker) && !Process.HasExited && wait.Elapsed.TotalSeconds < 15) Thread.Sleep(50);
            Require(File.Exists(marker) && !Process.HasExited, "Owned child did not become ready.");
        }
        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Require(!ProtectedPid(Process.Id) && Process.StartTime.ToUniversalTime().ToFileTimeUtc() == Created, "Refuse cleanup after an identity change.");
                Process.Kill(); Process.WaitForExit(5000);
            }
            Process.Dispose();
        }
    }
    private static object ChildSnapshot(object monitor, OwnedChild child)
    {
        var snapshot = monitor.GetType().GetMethod("Sample").Invoke(monitor, new object[] { true, false });
        var row = ((IEnumerable)Get(snapshot, "Processes")).Cast<object>().Single(item => (int)Get(item, "Id") == child.Process.Id);
        Require((long)Get(row, "CreationTime") == child.Created && !child.Process.HasExited, "Child identity must match before any action.");
        return row;
    }
    private static object CheckOwnedChildren(object monitor)
    {
        var events = new List<Dictionary<string, object>>();
        using (var first = new OwnedChild(Path.Combine(_output, "owned-child-a-" + Guid.NewGuid().ToString("N") + ".ready")))
        using (var other = new OwnedChild(Path.Combine(_output, "owned-child-b-" + Guid.NewGuid().ToString("N") + ".ready")))
        {
            var target = ChildSnapshot(monitor, first);
            var otherTarget = ChildSnapshot(monitor, other);
            Require((string)Get(target, "Name") == (string)Get(otherTarget, "Name"), "Children must demonstrate separate same-name identities.");
            var service = Activator.CreateInstance(_assembly.GetType("WinMemoryCleaner.ProcessActionService", true));
            var execute = service.GetType().GetMethod("Execute");
            first.Process.Refresh(); long before = first.Process.WorkingSet64;
            var cancelled = execute.Invoke(service, new object[] { target, true, false });
            Require(Get(cancelled, "Outcome").ToString() == "NotConfirmed" && !first.Process.HasExited && !other.Process.HasExited, "Cancellation must leave both children alive.");
            events.Add(new Dictionary<string, object> { { "case", "cancel" }, { "pid", first.Process.Id }, { "result", cancelled }, { "bothAlive", true } });
            var trimmed = execute.Invoke(service, new object[] { target, false, false });
            Thread.Sleep(150); first.Process.Refresh(); long after = first.Process.WorkingSet64;
            Require(Get(trimmed, "Outcome").ToString() == "Trimmed" && !first.Process.HasExited && !other.Process.HasExited, "Selected working-set release must not terminate either child.");
            Require(after < before, "Owned child's committed pages must demonstrate actual working-set release.");
            events.Add(new Dictionary<string, object> { { "case", "trim" }, { "pid", first.Process.Id }, { "result", trimmed }, { "beforeWorkingSet", before }, { "afterWorkingSet", after }, { "otherAlive", true } });
            var ended = execute.Invoke(service, new object[] { target, true, true });
            Require(Get(ended, "Outcome").ToString() == "Terminated" && first.Process.WaitForExit(5000) && !other.Process.HasExited, "Confirmed action must terminate exactly the selected child.");
            events.Add(new Dictionary<string, object> { { "case", "terminate-selected" }, { "pid", first.Process.Id }, { "result", ended }, { "otherPid", other.Process.Id }, { "otherAlive", true } });
            var finishOther = execute.Invoke(service, new object[] { otherTarget, true, true });
            Require(Get(finishOther, "Outcome").ToString() == "Terminated" && other.Process.WaitForExit(5000), "Second owned child must terminate only when selected separately.");
            events.Add(new Dictionary<string, object> { { "case", "terminate-other-separately" }, { "pid", other.Process.Id }, { "result", finishOther } });
        }
        return events;
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Name;
        internal uint Vendor, Device, SubSystem, Revision;
        internal UIntPtr VideoMemory, SystemMemory, SharedMemory;
        internal uint LuidLow; internal int LuidHigh; internal uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { internal uint Low; internal int High; internal uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterQuery { internal uint Handle, Type; internal IntPtr Data; internal uint Bytes; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref AdapterQuery query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint adapter);
    private static Delegate Method(IntPtr instance, int index, Type type)
    {
        return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size), type);
    }
    private static object ReadAdapters()
    {
        var result = new List<Dictionary<string, object>>(); IntPtr factory;
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out factory) != 0) return result;
        try
        {
            var enumerate = (EnumAdapter)Method(factory, 12, typeof(EnumAdapter));
            for (uint i = 0; i < 32; i++)
            {
                IntPtr adapter; if (enumerate(factory, i, out adapter) != 0) break;
                try
                {
                    AdapterDescription description;
                    if (((GetDescription)Method(adapter, 10, typeof(GetDescription)))(adapter, out description) != 0) continue;
                    var row = new Dictionary<string, object> { { "name", description.Name }, { "vendor", description.Vendor.ToString("X4") }, { "device", description.Device.ToString("X4") }, { "dxgiFlags", description.Flags }, { "luid", description.LuidHigh.ToString("X8") + ":" + description.LuidLow.ToString("X8") }, { "dedicatedBytes", description.VideoMemory.ToUInt64() } };
                    var open = new OpenAdapter { Low = description.LuidLow, High = description.LuidHigh };
                    try
                    {
                        int status = D3DKMTOpenAdapterFromLuid(ref open); row["openAdapterStatus"] = status;
                        if (status == 0)
                        {
                            var data = Marshal.AllocHGlobal(4);
                            try
                            {
                                Marshal.WriteInt32(data, 0);
                                var query = new AdapterQuery { Handle = open.Handle, Type = 15, Data = data, Bytes = 4 };
                                status = D3DKMTQueryAdapterInfo(ref query); row["adapterTypeStatus"] = status;
                                if (status == 0) row["adapterTypeBits"] = unchecked((uint)Marshal.ReadInt32(data));
                            }
                            finally { Marshal.FreeHGlobal(data); D3DKMTCloseAdapter(ref open.Handle); }
                        }
                    }
                    catch (EntryPointNotFoundException) { row["adapterTypeStatus"] = "unsupported"; }
                    result.Add(row);
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
}
