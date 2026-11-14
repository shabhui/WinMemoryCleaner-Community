using System;
using System.Runtime.InteropServices;

namespace WinMemoryCleaner
{
    // WinSDK shared/evntprov.h: EVENT_FILTER_TYPE_PID is a ULONG PID array;
    // EVENT_FILTER_EVENT_ID is BOOLEAN FilterIn, UCHAR Reserved, USHORT Count,
    // then USHORT Events[]. ENABLE_TRACE_PARAMETERS v2 accepts both descriptors.
    internal sealed class FramePresentFilter : IDisposable
    {
        private IntPtr _pid;
        private IntPtr _ids;
        private IntPtr _descriptors;
        private IntPtr _parameterBuffer;
        private FramePresentNativeMethods.EnableTraceParameters _parameters;
        private bool _disposed;

        internal FramePresentFilter(int processId, ushort[] eventIds)
        {
            if (processId <= 0) throw new ArgumentOutOfRangeException("processId");
            if (eventIds == null) throw new ArgumentNullException("eventIds");
            if (eventIds.Length == 0 || eventIds.Length > 4) throw new ArgumentOutOfRangeException("eventIds");
            try
            {
                _pid = Marshal.AllocHGlobal(4);
                Marshal.WriteInt32(_pid, processId);
                int idBytes = 4 + eventIds.Length * 2;
                _ids = Marshal.AllocHGlobal(idBytes);
                Marshal.WriteByte(_ids, 0, 1); // include, never exclude
                Marshal.WriteByte(_ids, 1, 0);
                Marshal.WriteInt16(_ids, 2, (short)eventIds.Length);
                for (int i = 0; i < eventIds.Length; i++) Marshal.WriteInt16(_ids, 4 + 2 * i, unchecked((short)eventIds[i]));
                _descriptors = Marshal.AllocHGlobal(32);
                FramePresentNativeMethods.EventFilterDescriptor pid = new FramePresentNativeMethods.EventFilterDescriptor
                { Ptr = Address(_pid), Size = 4, Type = 0x80000004 };
                FramePresentNativeMethods.EventFilterDescriptor ids = new FramePresentNativeMethods.EventFilterDescriptor
                { Ptr = Address(_ids), Size = (uint)idBytes, Type = 0x80000200 };
                Marshal.StructureToPtr(pid, _descriptors, false);
                Marshal.StructureToPtr(ids, IntPtr.Add(_descriptors, 16), false);
                _parameters = new FramePresentNativeMethods.EnableTraceParameters
                { Version = 2, EnableFilterDesc = _descriptors, FilterDescCount = 2 };
                _parameterBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FramePresentNativeMethods.EnableTraceParameters)));
                Marshal.StructureToPtr(_parameters, _parameterBuffer, false);
            }
            catch (Exception) { Dispose(); throw; }
        }

        internal FramePresentNativeMethods.EnableTraceParameters Parameters { get { return _parameters; } }
        internal IntPtr ParameterPointer { get { return _parameterBuffer; } }

        private static ulong Address(IntPtr pointer)
        {
            // EVENT_FILTER_DESCRIPTOR.Ptr is always ULONG64, even under WOW64.
            return IntPtr.Size == 8 ? unchecked((ulong)pointer.ToInt64()) : unchecked((uint)pointer.ToInt32());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_parameterBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(_parameterBuffer); _parameterBuffer = IntPtr.Zero; }
            if (_descriptors != IntPtr.Zero) { Marshal.FreeHGlobal(_descriptors); _descriptors = IntPtr.Zero; }
            if (_ids != IntPtr.Zero) { Marshal.FreeHGlobal(_ids); _ids = IntPtr.Zero; }
            if (_pid != IntPtr.Zero) { Marshal.FreeHGlobal(_pid); _pid = IntPtr.Zero; }
            _parameters = new FramePresentNativeMethods.EnableTraceParameters();
            GC.SuppressFinalize(this);
        }
    }
}
