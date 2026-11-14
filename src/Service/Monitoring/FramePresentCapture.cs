using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinMemoryCleaner
{
    // One uniquely owned user-provider real-time session, never a shared/kernel
    // session. The monitor invokes Create/Dispose only on its serial control worker.
    // Factual manifest/ABI sources are listed in FramePresentDecoder/NativeMethods.
    internal sealed class FramePresentCapture : IDisposable
    {
        private readonly int _processId;
        private readonly Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> _sink;
        private readonly Action<string, int> _fault;
        private readonly Func<ulong, uint> _process;
        private readonly Func<ulong, uint> _close;
        private readonly Func<ulong, IntPtr, uint, uint> _control;
        private readonly Func<IntPtr, byte[]> _metadata;
        private readonly FramePresentSchema[] _schemas = new FramePresentSchema[12];
        private FramePresentNativeMethods.EventRecordCallbackDelegate _eventCallback;
        private FramePresentNativeMethods.BufferCallbackDelegate _bufferCallback;
        private IntPtr _properties;
        private IntPtr _queryProperties;
        private IntPtr _logfile;
        private ulong _session;
        private ulong _consumer = ulong.MaxValue;
        private Thread _thread;
        private bool _started;
        private int _stopping;
        private int _failed;
        private int _cleanupError;

        internal static IDisposable Create(ProcessSnapshot target,
            Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> sink, Action<string, int> fault)
        {
            return new FramePresentCapture(target, sink, fault, SupportsFilters, StartNative, EnableNative,
                OpenNative, ProcessNative, FramePresentNativeMethods.CloseTrace, ControlNative, MetadataNative);
        }

        // Fakes replace only native calls. Session ownership, filtering, callback
        // marshaling, schema checking, loss handling and lifecycle remain real code.
        internal FramePresentCapture(ProcessSnapshot target,
            Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> sink, Action<string, int> fault,
            Func<bool> supported, Func<string, IntPtr, Tuple<uint, ulong>> start,
            Func<ulong, Guid, IntPtr, uint> enable, Func<IntPtr, Tuple<uint, ulong>> open,
            Func<ulong, uint> process, Func<ulong, uint> close, Func<ulong, IntPtr, uint, uint> control,
            Func<IntPtr, byte[]> metadata)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (target.Id <= 0 || target.CreationTime <= 0) throw new ArgumentOutOfRangeException("target");
            if (sink == null) throw new ArgumentNullException("sink");
            if (fault == null) throw new ArgumentNullException("fault");
            if (supported == null) throw new ArgumentNullException("supported");
            if (start == null) throw new ArgumentNullException("start");
            if (enable == null) throw new ArgumentNullException("enable");
            if (open == null) throw new ArgumentNullException("open");
            if (process == null) throw new ArgumentNullException("process");
            if (close == null) throw new ArgumentNullException("close");
            if (control == null) throw new ArgumentNullException("control");
            if (metadata == null) throw new ArgumentNullException("metadata");
            _processId = target.Id; _sink = sink; _fault = fault; _process = process;
            _close = close; _control = control; _metadata = metadata;
            try
            {
                if (!supported()) { SignalFault("FpsUnsupported", 50); return; }
                string name = "WinMemoryCleaner.FramePresent." + Guid.NewGuid().ToString("N");
                int nameOffset;
                _properties = Properties(name, out nameOffset);
                _queryProperties = Properties(name, out nameOffset);
                Tuple<uint, ulong> session = start(name, _properties);
                Check(session.Item1);
                if (session.Item2 == 0) throw new Win32Exception(6);
                _session = session.Item2; // Ownership begins only after StartTrace succeeds.
                using (FramePresentFilter dxgi = new FramePresentFilter(_processId, new ushort[] { 42, 43, 55, 56 }))
                    Check(enable(_session, FramePresentDecoder.Dxgi, dxgi.ParameterPointer));
                using (FramePresentFilter d3d9 = new FramePresentFilter(_processId, new ushort[] { 1, 2 }))
                    Check(enable(_session, FramePresentDecoder.D3d9, d3d9.ParameterPointer));
                _eventCallback = OnRecord;
                _bufferCallback = OnBuffer;
                FramePresentNativeMethods.EventTraceLogfile logfile = new FramePresentNativeMethods.EventTraceLogfile
                {
                    LoggerName = IntPtr.Add(_properties, nameOffset),
                    ProcessTraceMode = 0x10001100, // EVENT_RECORD | RAW_TIMESTAMP | REAL_TIME
                    EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_eventCallback),
                    BufferCallback = Marshal.GetFunctionPointerForDelegate(_bufferCallback)
                };
                _logfile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FramePresentNativeMethods.EventTraceLogfile)));
                Marshal.StructureToPtr(logfile, _logfile, false);
                Tuple<uint, ulong> consumer = open(_logfile);
                Check(consumer.Item1);
                if (InvalidConsumer(consumer.Item2)) throw new Win32Exception(6);
                _consumer = consumer.Item2;
                _thread = new Thread(Consume) { IsBackground = true, Name = "DirectX Present ETW" };
                _thread.Start();
                _started = true;
            }
            catch (Win32Exception error)
            {
                SignalFault(FramePresentNativeMethods.ErrorState(error.NativeErrorCode), error.NativeErrorCode);
                CleanupStartup();
            }
            catch (DllNotFoundException) { SignalFault("FpsUnsupported", 126); CleanupStartup(); }
            catch (EntryPointNotFoundException) { SignalFault("FpsUnsupported", 127); CleanupStartup(); }
            catch (Exception) { SignalFault("FpsError", 0); CleanupStartup(); }
        }

        private void CleanupStartup()
        {
            // Return the object even if native cleanup fails. The monitor must
            // retain that owner and refuse another capture, not lose a live session.
            try { Dispose(); }
            catch (Exception) { if (_cleanupError == 0) _cleanupError = 6; }
        }

        private static void Check(uint code) { if (code != 0) throw new Win32Exception(unchecked((int)code)); }
        private static bool InvalidConsumer(ulong handle)
        {
            return handle == ulong.MaxValue || (IntPtr.Size == 4 && handle == uint.MaxValue);
        }
        private bool Running { get { return Thread.VolatileRead(ref _stopping) == 0 && Thread.VolatileRead(ref _failed) == 0; } }

        private static IntPtr Properties(string name, out int nameOffset)
        {
            nameOffset = Marshal.SizeOf(typeof(FramePresentNativeMethods.EventTraceProperties));
            byte[] text = Encoding.Unicode.GetBytes(name + "\0");
            int bytes = nameOffset + text.Length;
            FramePresentNativeMethods.EventTraceProperties properties = new FramePresentNativeMethods.EventTraceProperties
            {
                Wnode = new FramePresentNativeMethods.WnodeHeader
                { BufferSize = (uint)bytes, Guid = Guid.NewGuid(), ClientContext = 1, Flags = 0x20000 },
                BufferSize = 64, MinimumBuffers = 2, MaximumBuffers = 8,
                // NO_PER_PROCESSOR_BUFFERING avoids ETW's per-CPU minimum buffer
                // multiplication. These are requested native limits, not an RSS claim.
                LogFileMode = 0x10000100, FlushTimer = 1, LoggerNameOffset = (uint)nameOffset
            };
            IntPtr buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.Copy(new byte[bytes], 0, buffer, bytes);
                Marshal.StructureToPtr(properties, buffer, false);
                Marshal.Copy(text, 0, IntPtr.Add(buffer, nameOffset), text.Length);
                return buffer;
            }
            catch (Exception) { Marshal.FreeHGlobal(buffer); throw; }
        }

        private void Consume()
        {
            try
            {
                uint code = _process(_consumer);
                // A real-time consumer ending without owned Stop is not evidence
                // of a healthy zero-FPS target. Invalidate instead of keeping history.
                if (Running) SignalFault(code == 0 ? "FpsEventLoss" : FramePresentNativeMethods.ErrorState(unchecked((int)code)), unchecked((int)code));
            }
            catch (Exception) { if (Running) SignalFault("FpsError", 0); }
            finally
            {
                GC.KeepAlive(_eventCallback);
                GC.KeepAlive(_bufferCallback);
            }
        }

        private void SignalFault(string state, int code)
        {
            if (Interlocked.Exchange(ref _failed, 1) != 0) return;
            try { _fault(FrameRateReading.FailureKey(state), code); }
            catch (Exception) { /* No managed exception may unwind into ETW. */ }
        }

        private static int SchemaSlot(Guid provider, int id, int pointerSize)
        {
            int slot = provider == FramePresentDecoder.Dxgi ? (id == 42 ? 0 : id == 43 ? 1 : id == 55 ? 2 : 3) : (id == 1 ? 4 : 5);
            return slot * 2 + (pointerSize == 8 ? 1 : 0);
        }

        private void OnRecord(IntPtr pointer)
        {
            if (!Running) return;
            try
            {
                if (pointer == IntPtr.Zero) { SignalFault("FpsError", 0); return; }
                FramePresentNativeMethods.EventRecord record = (FramePresentNativeMethods.EventRecord)Marshal.PtrToStructure(pointer, typeof(FramePresentNativeMethods.EventRecord));
                FramePresentNativeMethods.EventHeader header = record.Header;
                Guid provider = header.ProviderId;
                int id = header.Descriptor.Id;
                if (!FramePresentDecoder.Relevant(provider, id)) return;
                if (header.ProcessId != _processId)
                {
                    // A relevant event from another PID would violate the required
                    // filter contract. Do not silently keep a broad-capture fallback.
                    SignalFault("FpsUnsupported", 0); return;
                }
                int bits = header.Flags & 0x60;
                int pointerSize = bits == 0x20 ? 4 : bits == 0x40 ? 8 : 0;
                if (header.Size < 80 || pointerSize == 0 || header.Descriptor.Version != 0 || (header.Flags & 0x18c) != 0)
                { SignalFault("FpsUnsupported", 0); return; }
                int slot = SchemaSlot(provider, id, pointerSize);
                FramePresentSchema schema = _schemas[slot];
                if (schema == null)
                {
                    schema = FramePresentSchema.Read(_metadata(pointer), provider, id, header.Descriptor.Version, pointerSize);
                    if (schema == null) { SignalFault("FpsUnsupported", 0); return; }
                    _schemas[slot] = schema;
                }
                // Verified scalar layouts use at most 16 bytes. Never allocate an
                // attacker-selected payload size or retain a native event pointer.
                if (record.UserDataLength > 16 || (record.UserDataLength != 0 && record.UserData == IntPtr.Zero))
                { SignalFault("FpsUnsupported", 0); return; }
                byte[] payload = new byte[record.UserDataLength];
                if (payload.Length != 0) Marshal.Copy(record.UserData, payload, 0, payload.Length);
                _sink(provider, id, header.Descriptor.Version, (int)header.ProcessId, unchecked((int)header.ThreadId),
                    header.Timestamp, pointerSize, payload, schema.Names, schema.Types);
            }
            catch (Win32Exception error) { SignalFault("FpsUnsupported", error.NativeErrorCode); }
            catch (Exception) { SignalFault("FpsError", 0); }
        }

        private uint OnBuffer(IntPtr pointer)
        {
            if (!Running) return 0;
            try
            {
                if (pointer == IntPtr.Zero) { SignalFault("FpsError", 0); return 0; }
                FramePresentNativeMethods.EventTraceLogfile logfile = (FramePresentNativeMethods.EventTraceLogfile)Marshal.PtrToStructure(pointer, typeof(FramePresentNativeMethods.EventTraceLogfile));
                Check(_control(_session, _queryProperties, 0)); // QUERY only our successful StartTrace handle
                FramePresentNativeMethods.EventTraceProperties properties = (FramePresentNativeMethods.EventTraceProperties)Marshal.PtrToStructure(_queryProperties, typeof(FramePresentNativeMethods.EventTraceProperties));
                if (logfile.EventsLost != 0 || logfile.LogfileHeader.BuffersLost != 0 || properties.EventsLost != 0 ||
                    properties.LogBuffersLost != 0 || properties.RealTimeBuffersLost != 0)
                { SignalFault("FpsEventLoss", 0); return 0; }
                return Running ? 1u : 0u;
            }
            catch (Win32Exception error) { SignalFault(FramePresentNativeMethods.ErrorState(error.NativeErrorCode), error.NativeErrorCode); return 0; }
            catch (Exception) { SignalFault("FpsError", 0); return 0; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0)
            {
                if (_cleanupError != 0) throw new Win32Exception(_cleanupError);
                return;
            }
            int errorCode = 0;
            if (!InvalidConsumer(_consumer))
            {
                try
                {
                    uint code = _close(_consumer);
                    if (code != 0 && code != 7007) errorCode = unchecked((int)code); // ERROR_CTX_CLOSE_PENDING is normal
                }
                catch (Exception) { errorCode = 6; }
            }
            if (_session != 0)
            {
                try
                {
                    uint code = _control(_session, _properties, 1); // STOP only the handle we created; never stop by name
                    if (code != 0 && code != 4201) errorCode = unchecked((int)code); // already-gone owned session
                }
                catch (Exception) { errorCode = 6; }
            }
            if (_started)
            {
                if (Thread.CurrentThread == _thread)
                {
                    _cleanupError = 6;
                    throw new InvalidOperationException("DirectX Present cleanup must run off its consumer thread.");
                }
                // This join is deliberately off the UI thread. No timeout can prove
                // callbacks ended: retain resources/ownership if the native stop stalls.
                _thread.Join();
            }
            ReleaseBuffers();
            _consumer = ulong.MaxValue;
            if (errorCode == 0) _session = 0;
            _cleanupError = errorCode;
            GC.SuppressFinalize(this);
            if (errorCode != 0) throw new Win32Exception(errorCode);
        }

        private void ReleaseBuffers()
        {
            // Called only before consumer start, or after its actual completion.
            if (_logfile != IntPtr.Zero) { Marshal.FreeHGlobal(_logfile); _logfile = IntPtr.Zero; }
            if (_queryProperties != IntPtr.Zero) { Marshal.FreeHGlobal(_queryProperties); _queryProperties = IntPtr.Zero; }
            if (_properties != IntPtr.Zero) { Marshal.FreeHGlobal(_properties); _properties = IntPtr.Zero; }
            _eventCallback = null; _bufferCallback = null;
            Array.Clear(_schemas, 0, _schemas.Length);
        }

        private static bool SupportsFilters()
        {
            FramePresentNativeMethods.OsVersionInfo version = new FramePresentNativeMethods.OsVersionInfo
            { Size = (uint)Marshal.SizeOf(typeof(FramePresentNativeMethods.OsVersionInfo)), ServicePack = string.Empty };
            // RtlGetVersion avoids manifest-dependent OS version virtualization.
            return FramePresentNativeMethods.RtlGetVersion(ref version) == 0 &&
                (version.Major > 6 || (version.Major == 6 && version.Minor >= 3));
        }
        private static Tuple<uint, ulong> StartNative(string name, IntPtr properties)
        {
            ulong session;
            uint code = FramePresentNativeMethods.StartTraceW(out session, name, properties);
            return Tuple.Create(code, session);
        }
        private static uint EnableNative(ulong session, Guid provider, IntPtr pointer)
        {
            FramePresentNativeMethods.EnableTraceParameters parameters = (FramePresentNativeMethods.EnableTraceParameters)Marshal.PtrToStructure(pointer, typeof(FramePresentNativeMethods.EnableTraceParameters));
            return FramePresentNativeMethods.EnableTraceEx2(session, ref provider, 1, 5, 0x8000000000000002UL, 0, 1000, ref parameters);
        }
        private static Tuple<uint, ulong> OpenNative(IntPtr logfile)
        {
            ulong consumer = FramePresentNativeMethods.OpenTraceW(logfile);
            return Tuple.Create(InvalidConsumer(consumer) ? unchecked((uint)Marshal.GetLastWin32Error()) : 0u, consumer);
        }
        private static uint ProcessNative(ulong consumer) { return FramePresentNativeMethods.ProcessTrace(new[] { consumer }, 1, IntPtr.Zero, IntPtr.Zero); }
        private static uint ControlNative(ulong session, IntPtr properties, uint control) { return FramePresentNativeMethods.ControlTraceW(session, null, properties, control); }
        private static byte[] MetadataNative(IntPtr record)
        {
            uint bytes = 0;
            uint code = FramePresentNativeMethods.TdhGetEventInformation(record, 0, IntPtr.Zero, IntPtr.Zero, ref bytes);
            if (code != 122 || bytes < 112 || bytes > FramePresentSchema.MaximumMetadataBytes) return null;
            uint capacity = bytes;
            IntPtr buffer = Marshal.AllocHGlobal((int)capacity);
            try
            {
                code = FramePresentNativeMethods.TdhGetEventInformation(record, 0, IntPtr.Zero, buffer, ref bytes);
                if (code != 0 || bytes > capacity || bytes < 112) return null;
                byte[] metadata = new byte[bytes];
                Marshal.Copy(buffer, metadata, 0, metadata.Length);
                return metadata;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
