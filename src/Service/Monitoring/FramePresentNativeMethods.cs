using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMemoryCleaner
{
    // ABI declarations, not a copied collector. Microsoft win32metadata WinSDK:
    // shared/evntrace.h EVENT_TRACE_PROPERTIES, EVENT_TRACE_LOGFILEW,
    // TRACE_LOGFILE_HEADER, ENABLE_TRACE_PARAMETERS; shared/evntprov.h
    // EVENT_DESCRIPTOR/FILTER_DESCRIPTOR; um/evntcons.h EVENT_HEADER/RECORD.
    // Default Windows packing is 8; pointer fields use the consumer's pointer size.
    internal static class FramePresentNativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct EventDescriptor
        {
            internal ushort Id;
            internal byte Version;
            internal byte Channel;
            internal byte Level;
            internal byte Opcode;
            internal ushort Task;
            internal ulong Keyword;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventHeader
        {
            internal ushort Size;
            internal ushort HeaderType;
            internal ushort Flags;
            internal ushort EventProperty;
            internal uint ThreadId;
            internal uint ProcessId;
            internal long Timestamp;
            internal Guid ProviderId;
            internal EventDescriptor Descriptor;
            internal ulong ProcessorTime;
            internal Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventRecord
        {
            internal EventHeader Header;
            internal uint BufferContext;
            internal ushort ExtendedDataCount;
            internal ushort UserDataLength;
            internal IntPtr ExtendedData;
            internal IntPtr UserData;
            internal IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventTraceHeader
        {
            internal ushort Size;
            internal ushort FieldTypeFlags;
            internal uint Version;
            internal uint ThreadId;
            internal uint ProcessId;
            internal long Timestamp;
            internal Guid Guid;
            internal ulong ProcessorTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventTrace
        {
            internal EventTraceHeader Header;
            internal uint InstanceId;
            internal uint ParentInstanceId;
            internal Guid ParentGuid;
            internal IntPtr MofData;
            internal uint MofLength;
            internal uint ClientContext;
        }

        // TIME_ZONE_INFORMATION is 172 bytes, 4-byte aligned. It is only reserved
        // storage inside TRACE_LOGFILE_HEADER; time-zone conversion is never used.
        [StructLayout(LayoutKind.Sequential, Size = 172)]
        internal struct TimeZoneInformation { internal int Bias; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TraceLogfileHeader
        {
            internal uint BufferSize;
            internal uint Version;
            internal uint ProviderVersion;
            internal uint NumberOfProcessors;
            internal long EndTime;
            internal uint TimerResolution;
            internal uint MaximumFileSize;
            internal uint LogFileMode;
            internal uint BuffersWritten;
            internal uint StartBuffers;
            internal uint PointerSize;
            internal uint EventsLost;
            internal uint CpuSpeedInMHz;
            internal IntPtr LoggerName;
            internal IntPtr LogFileName;
            internal TimeZoneInformation TimeZone;
            internal long BootTime;
            internal long PerfFreq;
            internal long StartTime;
            internal uint ReservedFlags;
            internal uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventTraceLogfile
        {
            internal IntPtr LogFileName;
            internal IntPtr LoggerName;
            internal long CurrentTime;
            internal uint BuffersRead;
            internal uint ProcessTraceMode;
            internal EventTrace CurrentEvent;
            internal TraceLogfileHeader LogfileHeader;
            internal IntPtr BufferCallback;
            internal uint BufferSize;
            internal uint Filled;
            internal uint EventsLost;
            internal IntPtr EventRecordCallback;
            internal uint IsKernelTrace;
            internal IntPtr Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WnodeHeader
        {
            internal uint BufferSize;
            internal uint ProviderId;
            internal ulong HistoricalContext;
            internal long Timestamp;
            internal Guid Guid;
            internal uint ClientContext;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventTraceProperties
        {
            internal WnodeHeader Wnode;
            internal uint BufferSize;
            internal uint MinimumBuffers;
            internal uint MaximumBuffers;
            internal uint MaximumFileSize;
            internal uint LogFileMode;
            internal uint FlushTimer;
            internal uint EnableFlags;
            internal int AgeLimit;
            internal uint NumberOfBuffers;
            internal uint FreeBuffers;
            internal uint EventsLost;
            internal uint BuffersWritten;
            internal uint LogBuffersLost;
            internal uint RealTimeBuffersLost;
            internal IntPtr LoggerThreadId;
            internal uint LogFileNameOffset;
            internal uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EnableTraceParameters
        {
            internal uint Version;
            internal uint EnableProperty;
            internal uint ControlFlags;
            internal Guid SourceId;
            internal IntPtr EnableFilterDesc;
            internal uint FilterDescCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EventFilterDescriptor
        {
            internal ulong Ptr;
            internal uint Size;
            internal uint Type;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct OsVersionInfo
        {
            internal uint Size;
            internal uint Major;
            internal uint Minor;
            internal uint Build;
            internal uint Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            internal string ServicePack;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void EventRecordCallbackDelegate(IntPtr record);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate uint BufferCallbackDelegate(IntPtr logfile);

        internal static string ErrorState(int code)
        {
            switch (code)
            {
                case 5: return "FpsAccessDenied";
                case 50: case 87: case 120: case 127: case 1150: case 1168: case 4200:
                    return "FpsUnsupported";
                default: return "FpsError";
            }
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder image, ref uint length);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int RtlGetVersion(ref OsVersionInfo version);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint StartTraceW(out ulong session, string name, IntPtr properties);
        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern uint EnableTraceEx2(ulong session, ref Guid provider, uint control, byte level,
            ulong anyKeyword, ulong allKeyword, uint timeout, ref EnableTraceParameters parameters);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern uint ProcessTrace([In] ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll", ExactSpelling = true)]
        internal static extern uint CloseTrace(ulong consumer);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint ControlTraceW(ulong session, string name, IntPtr properties, uint control);
        [DllImport("tdh.dll", ExactSpelling = true)]
        internal static extern uint TdhGetEventInformation(IntPtr record, uint contextCount, IntPtr context,
            IntPtr information, ref uint size);
    }
}
