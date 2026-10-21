using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMemoryCleaner
{
    internal interface ISensorJob : IDisposable { bool Assign(Process process); }

    /// <summary>
    /// Own anonymous kill-on-close job object. Closing the handle terminates only the process this
    /// instance was explicitly given; no process is ever selected by name or by PID.
    /// </summary>
    internal sealed class SensorJob : ISensorJob
    {
        private const int ExtendedLimitInformation = 9;
        private const uint LimitKillOnJobClose = 0x2000U;
        private readonly object _gate = new object();
        private SafeJobHandle _handle;
        private bool _assigned;
        private bool _disposed;

        internal SensorJob()
        {
            SafeJobHandle handle = null;
            try
            {
                handle = CreateJobObject(IntPtr.Zero, null);
                if (handle == null || handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create a sensor job object.");
                ApplyKillOnClose(handle);
                _handle = handle;
                handle = null; // Ownership is now held by this job.
            }
            finally { if (handle != null) handle.Dispose(); }
        }

        /// <summary>Assigns exactly the given process handle; a closed or reused handle cannot be substituted.</summary>
        public bool Assign(Process process)
        {
            if (process == null) throw new ArgumentNullException("process");
            lock (_gate)
            {
                if (_disposed || _assigned || _handle == null || _handle.IsInvalid || _handle.IsClosed) return false;
                if (!AssignProcessToJobObject(_handle, process.Handle)) return false;
                _assigned = true;
                return true;
            }
        }

        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }

        private void Dispose(bool disposing)
        {
            if (!disposing) return;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_handle != null) { _handle.Dispose(); _handle = null; }
            }
        }

        private static void ApplyKillOnClose(SafeJobHandle handle)
        {
            var information = new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = LimitKillOnJobClose;
            int size = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                if (!SetInformationJobObject(handle, ExtendedLimitInformation, buffer, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot configure the sensor job object.");
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal SafeJobHandle() : base(true) { }
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            internal long PerProcessUserTimeLimit;
            internal long PerJobUserTimeLimit;
            internal uint LimitFlags;
            internal UIntPtr MinimumWorkingSetSize;
            internal UIntPtr MaximumWorkingSetSize;
            internal uint ActiveProcessLimit;
            internal UIntPtr Affinity;
            internal uint PriorityClass;
            internal uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            internal ulong ReadOperationCount;
            internal ulong WriteOperationCount;
            internal ulong OtherOperationCount;
            internal ulong ReadTransferCount;
            internal ulong WriteTransferCount;
            internal ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            internal JobObjectBasicLimitInformation BasicLimitInformation;
            internal IoCounters IoInfo;
            internal UIntPtr ProcessMemoryLimit;
            internal UIntPtr JobMemoryLimit;
            internal UIntPtr PeakProcessMemoryUsed;
            internal UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeJobHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeJobHandle job, int informationClass, IntPtr information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
