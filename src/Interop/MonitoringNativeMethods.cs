using System;
using System.Runtime.InteropServices;
namespace WinMemoryCleaner
{
    internal static class MonitoringNativeMethods
    {
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        [DllImport("ntdll.dll")]internal static extern int NtQuerySystemInformation(int info,IntPtr buffer,int length,out int needed);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern IntPtr OpenProcess(uint rights,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetProcessTimes(IntPtr handle,out long create,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]internal static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool IsProcessCritical(IntPtr handle,out bool critical);
        [DllImport("ntdll.dll",EntryPoint="NtQueryInformationProcess")]internal static extern int ProcessProtection(IntPtr handle,int info,out byte protection,int size,out int needed);
        [DllImport("ntdll.dll",EntryPoint="NtQueryInformationProcess")]internal static extern int ProcessCritical(IntPtr handle,int info,out uint critical,int size,out int needed);
        [DllImport("psapi.dll",SetLastError=true)]internal static extern bool EmptyWorkingSet(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateProcess(IntPtr handle,uint exitCode);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)]internal static extern IntPtr GetProcAddress(IntPtr module,[MarshalAs(UnmanagedType.LPStr)]string name);
        [DllImport("kernel32.dll")]internal static extern bool FreeLibrary(IntPtr module);
        [StructLayout(LayoutKind.Sequential)]internal struct UnicodeString { internal ushort Length,MaximumLength;internal IntPtr Buffer; }
        // Modern SYSTEM_PROCESS_INFORMATION prefix: pointer-sized fields preserve x86/x64 offsets.
        [StructLayout(LayoutKind.Sequential)]internal struct ProcessInfo
        {
            internal uint NextEntryOffset,NumberOfThreads;
            internal long WorkingSetPrivateSize;
            internal uint HardFaultCount,NumberOfThreadsHighWatermark;
            internal ulong CycleTime;
            internal long CreateTime,UserTime,KernelTime;
            internal UnicodeString ImageName;
            internal int BasePriority;
            internal IntPtr UniqueProcessId,InheritedFromUniqueProcessId;
            internal uint HandleCount,SessionId;
            internal UIntPtr UniqueProcessKey,PeakVirtualSize,VirtualSize;
            internal uint PageFaultCount;
            internal UIntPtr PeakWorkingSetSize,WorkingSetSize,QuotaPeakPagedPoolUsage,QuotaPagedPoolUsage,QuotaPeakNonPagedPoolUsage,QuotaNonPagedPoolUsage,PagefileUsage,PeakPagefileUsage,PrivatePageCount;
        }
    }
}
