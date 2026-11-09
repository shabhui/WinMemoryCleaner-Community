// Verification-only accounting. A nested job contains this harness and only its
// descendants. It has no CPU/memory limits and never opens a termination handle.
// The outer bounded runner owns emergency cleanup; this class records normal exit.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public sealed class MonitoringProcessAccounting : IDisposable
{
    public sealed class Snapshot
    {
        public int Sample, ActiveProcesses, AssignedProcesses;
        public double CpuSeconds, HostCpuSeconds, SystemCpuMachinePercent;
        public long PrivateBytes, WorkingSetBytes, HostPrivateBytes, HostWorkingSetBytes;
    }

    private sealed class Entry
    {
        internal IntPtr Handle;
        internal int Id;
        internal long Created;
        internal string Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        internal long User, Kernel, PeriodUser, PeriodKernel;
        internal uint Faults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        internal uint Size, PageFaultCount;
        internal UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool;
        internal UIntPtr PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint Low, High;
        internal long Value { get { return ((long)High << 32) | Low; } }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int kind, IntPtr data, uint bytes, out uint returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    private static extern bool QueryAccounting(IntPtr job, int kind, out Accounting data, uint bytes, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool member);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out FileTime created, out FileTime exited, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint bytes);

    private readonly string _output;
    private readonly int _selfId;
    private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
    private readonly List<Dictionary<string, object>> _identities = new List<Dictionary<string, object>>();
    private IntPtr _job;
    private StreamWriter _csv;
    private int _sample;
    private long _systemIdle, _systemTotal;
    private bool _systemReady;

    public MonitoringProcessAccounting(string output)
    {
        _output = Path.GetFullPath(output);
        Directory.CreateDirectory(_output);
        using (var self = Process.GetCurrentProcess()) _selfId = self.Id;
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Create measurement job");
        if (!AssignProcessToJobObject(_job, GetCurrentProcess()))
        {
            int error = Marshal.GetLastWin32Error();
            CloseHandle(_job); _job = IntPtr.Zero;
            throw new Win32Exception(error, "Assign harness to its nested measurement job");
        }
        _csv = new StreamWriter(Path.Combine(_output, "process-samples.csv"), false);
        _csv.WriteLine("sample,elapsedSeconds,pid,creationFileTime,role,cpuSeconds,privateBytes,workingSetBytes");
    }

    private int[] ProcessIds()
    {
        // Bounded to 128 descendants: normal WMC monitoring starts at most one backend.
        const int capacity = 128;
        int bytes = 8 + capacity * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            uint returned;
            if (!QueryInformationJobObject(_job, 3, buffer, (uint)bytes, out returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Read owned-job process list");
            int assigned = Marshal.ReadInt32(buffer, 0), count = Marshal.ReadInt32(buffer, 4);
            if (count < 1 || count > capacity || assigned > count)
                throw new InvalidOperationException("Incomplete owned-job process list; refusing a partial total.");
            var ids = new int[count];
            for (int index = 0; index < count; index++)
                ids[index] = checked((int)Marshal.ReadIntPtr(buffer, 8 + index * IntPtr.Size).ToInt64());
            return ids;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private Entry OpenEntry(int id)
    {
        Entry entry;
        if (_entries.TryGetValue(id, out entry))
        {
            if (WaitForSingleObject(entry.Handle, 0) == 258) return entry;
            CloseHandle(entry.Handle); _entries.Remove(id);
        }
        // QUERY_INFORMATION | VM_READ | SYNCHRONIZE; no terminate or set-quota right.
        IntPtr handle = OpenProcess(0x00100410, false, id);
        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 87) return null; // Process exited between job enumeration and open.
            throw new Win32Exception(error, "Open owned process for read-only accounting");
        }
        try
        {
            bool member;
            if (!IsProcessInJob(handle, _job, out member))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Confirm owned-job membership");
            if (!member) return null; // A recycled PID is not part of our job.
            FileTime created, exited, kernel, user;
            if (!GetProcessTimes(handle, out created, out exited, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Read owned-process identity");
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length))
            {
                int error = Marshal.GetLastWin32Error();
                // Windows can discard the image path before the exit handle becomes
                // signalled. Wait only on this retained, job-verified process handle.
                if (WaitForSingleObject(handle, 100) == 0)
                {
                    File.AppendAllText(Path.Combine(_output, "accounting-exits-before-image.jsonl"),
                        new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                            { "pid", id }, { "creationFileTime", created.Value }, { "nativeError", error },
                            { "ownedJobMembershipVerified", true }, { "exitConfirmedOnSameHandle", true },
                            { "cpuRetainedByJobAccounting", true }
                        }) + Environment.NewLine);
                    return null;
                }
                throw new Win32Exception(error, "Read live owned-process image path");
            }
            entry = new Entry { Handle = handle, Id = id, Created = created.Value, Path = path.ToString() };
            _entries.Add(id, entry); handle = IntPtr.Zero;
            _identities.Add(new Dictionary<string, object> {
                { "pid", id }, { "creationFileTime", created.Value }, { "imagePath", entry.Path },
                { "role", id == _selfId ? "host" : "descendant" }, { "ownedJobMembershipVerified", true }
            });
            File.WriteAllText(Path.Combine(_output, "process-identities.json"), new JavaScriptSerializer().Serialize(_identities));
            return entry;
        }
        finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
    }

    public Snapshot TakeSample(double elapsedSeconds)
    {
        var result = new Snapshot { Sample = _sample++, SystemCpuMachinePercent = -1 };
        bool foundSelf = false;
        foreach (int id in ProcessIds())
        {
            var entry = OpenEntry(id);
            if (entry == null || WaitForSingleObject(entry.Handle, 0) == 0) continue;
            MemoryCounters memory;
            if (!GetProcessMemoryInfo(entry.Handle, out memory, (uint)Marshal.SizeOf(typeof(MemoryCounters))))
            {
                if (WaitForSingleObject(entry.Handle, 0) == 0) continue;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Read owned-process private memory");
            }
            FileTime created, exited, kernel, user;
            if (!GetProcessTimes(entry.Handle, out created, out exited, out kernel, out user) || created.Value != entry.Created)
                throw new InvalidOperationException("Accounting handle identity could not be verified.");
            double cpu = (kernel.Value + user.Value) / 10000000.0;
            long privateBytes = checked((long)memory.PrivateUsage.ToUInt64());
            long workingSet = checked((long)memory.WorkingSet.ToUInt64());
            result.PrivateBytes += privateBytes; result.WorkingSetBytes += workingSet;
            result.ActiveProcesses++;
            if (id == _selfId)
            {
                foundSelf = true; result.HostCpuSeconds = cpu;
                result.HostPrivateBytes = privateBytes; result.HostWorkingSetBytes = workingSet;
            }
            _csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2},{3},{4},{5:F7},{6},{7}",
                result.Sample, elapsedSeconds, id, entry.Created, id == _selfId ? "host" : "descendant", cpu, privateBytes, workingSet));
        }
        _csv.Flush();
        if (!foundSelf) throw new InvalidOperationException("Host is missing from its own measurement job.");
        Accounting accounting; uint returned;
        if (!QueryAccounting(_job, 1, out accounting, (uint)Marshal.SizeOf(typeof(Accounting)), out returned))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read job CPU accounting");
        // Job totals retain CPU time even if a short-lived descendant exits between samples.
        result.CpuSeconds = (accounting.User + accounting.Kernel) / 10000000.0;
        result.AssignedProcesses = checked((int)accounting.TotalProcesses);
        FileTime idle, systemKernel, systemUser;
        if (!GetSystemTimes(out idle, out systemKernel, out systemUser))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read total system CPU load");
        long total = systemKernel.Value + systemUser.Value;
        if (_systemReady && total > _systemTotal)
            result.SystemCpuMachinePercent = (1.0 - (idle.Value - _systemIdle) / (double)(total - _systemTotal)) * 100;
        _systemIdle = idle.Value; _systemTotal = total; _systemReady = true;
        return result;
    }

    public bool VerifyDescendantsExited(int timeoutMilliseconds)
    {
        var clock = Stopwatch.StartNew();
        int[] ids;
        while (true)
        {
            ids = ProcessIds();
            if ((ids.Length == 1 && ids[0] == _selfId) || clock.ElapsedMilliseconds >= timeoutMilliseconds) break;
            Thread.Sleep(50);
        }
        bool clean = ids.Length == 1 && ids[0] == _selfId;
        File.WriteAllText(Path.Combine(_output, "process-cleanup.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
            { "hostPid", _selfId }, { "activeProcessIdsAfterApplicationDispose", ids },
            { "allDescendantsExitedNormally", clean }, { "waitMilliseconds", clock.ElapsedMilliseconds },
            { "forcedTerminationByMeasurementTool", false },
            { "observedIdentities", _identities.Count }, { "accounting", "Nested Windows job; no limits or breakaway flags" }
        }));
        return clean;
    }

    public void Dispose()
    {
        if (_csv != null) { _csv.Dispose(); _csv = null; }
        foreach (var entry in _entries.Values) CloseHandle(entry.Handle);
        _entries.Clear();
        // No kill-on-close flag: never terminate the host while writing its result.
        if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; }
    }
}
