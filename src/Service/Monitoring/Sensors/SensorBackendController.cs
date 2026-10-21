using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Owns at most one isolated read-only temperature backend per cleaner process. Payload preparation,
    /// process creation, pipe reading and cleanup all run on background threads, so configuration and
    /// snapshot reads never block the caller and never touch hardware directly.
    /// </summary>
    internal sealed class SensorBackendController : ISensorBackend
    {
        private const int StartBudgetSeconds = 10;
        private const int RetrySeconds = 30;
        private const int PollMilliseconds = 40;
        private const int StderrCapacity = 16384;
        private const int StopWaitMilliseconds = 2000;
        private const int ExitWaitMilliseconds = 1000;
        private const int JoinMilliseconds = 500;
        private const string Source = "LibreHardwareMonitor";
        private const string DisabledReason = Source + ": universal temperature backend is not requested";
        private const string WaitingReason = Source + ": waiting for the universal temperature backend";
        private const string StartBudgetReason = Source + ": no status frame within 10 seconds";
        private const string StaleReason = Source + ": status frames stopped arriving";
        private const string PipeReason = Source + ": the backend closed its status pipe";
        private static readonly long StartBudgetTicks = StartBudgetSeconds * Stopwatch.Frequency;
        private static readonly long RetryTicks = RetrySeconds * Stopwatch.Frequency;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private readonly object _gate = new object();
        private readonly Func<SensorPayloadLease> _prepare;
        private readonly Func<ISensorJob> _jobs;
        private readonly Func<long> _clock;
        private readonly Func<SensorDriverState> _inspect;
        private readonly byte[] _stderr = new byte[StderrCapacity];

        private Session _session;
        private Thread _worker;
        private bool _disposed;
        private bool _demand;
        private int _refresh = 2;
        private long _retryAt;
        private int _stderrCount;
        private int _stderrWrite;
        private int _processId;
        private string _backend = "Disabled";
        private string _driver = "NotChecked";
        private MetricValue _cpu = MetricValue.Missing(MetricStatus.Disabled, DisabledReason);
        private MetricValue _gpu = MetricValue.Missing(MetricStatus.Disabled, DisabledReason);

        internal SensorBackendController()
            : this(PrepareDefault, CreateJob, Stopwatch.GetTimestamp, SensorDriverTrust.Inspect) { }

        internal SensorBackendController(Func<SensorPayloadLease> prepare, Func<ISensorJob> jobs, Func<long> clock, Func<SensorDriverState> inspect)
        {
            if (prepare == null) throw new ArgumentNullException("prepare");
            if (jobs == null) throw new ArgumentNullException("jobs");
            if (clock == null) throw new ArgumentNullException("clock");
            if (inspect == null) throw new ArgumentNullException("inspect");
            _prepare = prepare;
            _jobs = jobs;
            _clock = clock;
            _inspect = inspect;
        }

        int ISensorBackend.ProcessId { get { return ProcessId; } }
        string ISensorBackend.BackendStatus { get { return BackendStatus; } }
        string ISensorBackend.DriverStatus { get { return DriverStatus; } }
        string ISensorBackend.Diagnostic { get { return Diagnostic; } }
        void ISensorBackend.Configure(int seconds) { Configure(seconds); }
        void ISensorBackend.GetSnapshot(out MetricValue cpu,out MetricValue gpu) { GetSnapshot(out cpu,out gpu); }
        void ISensorBackend.Suspend() { Suspend(); }
        void ISensorBackend.ResetDriverState() { ResetDriverState(); }

        /// <summary>PID of the backend this controller owns, or 0 while nothing is running.</summary>
        internal int ProcessId { get { lock (_gate) { return _processId; } } }

        internal string BackendStatus { get { lock (_gate) { return _backend; } } }

        internal string DriverStatus { get { lock (_gate) { return _driver; } } }

        /// <summary>Latest 16 KiB of raw backend stderr, which is drained continuously.</summary>
        internal string Diagnostic { get { lock (_gate) { return StderrTextLocked(); } } }

        /// <summary>Stores the requested refresh; never starts device work on its own.</summary>
        internal void Configure(int seconds)
        {
            seconds = SensorProtocol.NormalizeRefresh(seconds);
            lock (_gate)
            {
                if (_disposed) return;
                _refresh = seconds;
                if (_session != null) { _session.Refresh = seconds; _session.Revision++; }
            }
            NotifyWorker();
        }

        /// <summary>Reads cached state and records demand; the actual backend starts asynchronously.</summary>
        internal void GetSnapshot(out MetricValue cpu, out MetricValue gpu)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _demand = true;
                    if (_session == null && String.Equals(_backend, "Disabled", StringComparison.Ordinal)) SetWaitingLocked();
                    EnsureWorkerLocked();
                    EvaluateLocked();
                }
                cpu = _cpu;
                gpu = _gpu;
            }
        }

        /// <summary>Drops demand, revokes the cache immediately and stops the backend asynchronously.</summary>
        internal void Suspend()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _demand = false;
                if (_session != null) _session.Abandoned = true;
                SetDisabledLocked();
            }
            NotifyWorker();
        }

        /// <summary>Clears driver backoff and replaces the current backend after the old one exited.</summary>
        internal void ResetDriverState()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _retryAt = 0;
                _driver = "NotChecked";
                if (_session != null) _session.Abandoned = true;
                if (_demand)
                {
                    SetWaitingLocked();
                    EnsureWorkerLocked();
                }
                else SetDisabledLocked();
            }
            NotifyWorker();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _demand = false;
                if (_session != null) _session.Abandoned = true;
                SetDisabledLocked();
                Monitor.PulseAll(_gate);
            }
            GC.SuppressFinalize(this);
        }

        private static SensorPayloadLease PrepareDefault() { return SensorPayload.CreateDefault().Prepare(); }

        private static ISensorJob CreateJob() { return new SensorJob(); }

        private void NotifyWorker()
        {
            lock (_gate) { Monitor.PulseAll(_gate); }
        }

        private void SetDisabledLocked()
        {
            _cpu = MetricValue.Missing(MetricStatus.Disabled, DisabledReason);
            _gpu = MetricValue.Missing(MetricStatus.Disabled, DisabledReason);
            _backend = "Disabled";
        }

        private void SetWaitingLocked()
        {
            if (_disposed || !_demand) { SetDisabledLocked(); return; }
            _cpu = MetricValue.Missing(MetricStatus.WarmingUp, WaitingReason);
            _gpu = MetricValue.Missing(MetricStatus.WarmingUp, WaitingReason);
            _backend = "Preparing";
        }

        private void EnsureWorkerLocked()
        {
            if (_worker != null && _worker.IsAlive) return;
            long now = _clock();
            if (_retryAt != 0 && now < _retryAt) return;
            var worker = new Thread(Run) { IsBackground = true, Name = "Universal temperature backend" };
            _worker = worker;
            try { worker.Start(); }
            catch (Exception error)
            {
                _worker = null;
                _retryAt = now + RetryTicks;
                _cpu = MetricValue.Missing(MetricStatus.Error, Describe(error));
                _gpu = _cpu;
                _backend = "Error";
            }
        }

        private bool IsCurrent(Session session) { lock (_gate) { return IsCurrentLocked(session); } }

        private bool IsCurrentLocked(Session session)
        { return !_disposed && session != null && !session.Abandoned && ReferenceEquals(_session, session); }

        private void Run()
        {
            while (true)
            {
                Session session = null;
                lock (_gate)
                {
                    if (_disposed || !_demand) { _worker = null; Monitor.PulseAll(_gate); return; }
                    long now = _clock();
                    if (_session == null && (_retryAt == 0 || now >= _retryAt))
                    {
                        session = new Session(Guid.NewGuid().ToString("N"), _refresh);
                        _session = session;
                        _retryAt = 0;
                        ClearStderrLocked();
                        SetWaitingLocked();
                    }
                    if (session == null) Monitor.Wait(_gate, PollMilliseconds);
                }
                if (session != null) RunSession(session);
            }
        }

        private void RunSession(Session session)
        {
            string reason = null;
            try
            {
                SensorDriverState driver = InspectDriver();
                session.CpuEnabled = driver.CpuAllowed;
                lock (_gate) { if (!_disposed) _driver = driver.Status; }
                if (!IsCurrent(session)) { session.Abandoned = true; return; }
                session.Lease = _prepare();
                if (!IsCurrent(session)) return;
                StartProcess(session);
                if (!IsCurrent(session)) return;
                SendConfigure(session);
                Supervise(session);
            }
            catch (Exception error) { if (!session.Abandoned) reason = Describe(error); }
            finally { Cleanup(session, reason); }
        }

        private SensorDriverState InspectDriver()
        {
            try
            {
                SensorDriverState state = _inspect();
                if (state != null) return state;
            }
            catch (Exception) { }
            return new SensorDriverState(false, true, null, "InspectionFailed");
        }

        private void StartProcess(Session session)
        {
            int pid;
            long ticks;
            using (Process current = Process.GetCurrentProcess())
            {
                pid = current.Id;
                ticks = current.StartTime.ToUniversalTime().Ticks;
            }
            var start = new ProcessStartInfo
            {
                FileName = session.Lease.Executable,
                WorkingDirectory = session.Lease.Directory,
                Arguments = "--parent-pid " + pid.ToString(CultureInfo.InvariantCulture) + " --parent-start-ticks " + ticks.ToString(CultureInfo.InvariantCulture),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            Process process = Process.Start(start);
            if (process == null) throw new Win32Exception("The universal temperature backend did not start.");
            lock (_gate) { session.Process = process; session.StartedTicks = _clock(); }
            session.ProcessId = process.Id;
            session.Input = process.StandardInput.BaseStream;
            session.Output = process.StandardOutput.BaseStream;
            session.Error = process.StandardError.BaseStream;
            ISensorJob job = _jobs();
            if (job == null || !job.Assign(process))
            {
                if (job != null) job.Dispose();
                throw new InvalidOperationException("The backend could not be contained in its own job object.");
            }
            session.Job = job;
            lock (_gate) { if (IsCurrentLocked(session)) _processId = process.Id; }
            var output = new Thread(ReadFrames) { IsBackground = true, Name = "Sensor backend status" };
            session.OutputThread = output;
            output.Start(session);
            var error = new Thread(ReadDiagnostics) { IsBackground = true, Name = "Sensor backend diagnostics" };
            session.ErrorThread = error;
            error.Start(session);
        }

        private void ReadFrames(object state)
        {
            var session = (Session)state;
            try
            {
                var reader = new BoundedLineReader(session.Output);
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    SensorMessage message;
                    try { message = SensorProtocol.Decode(line); }
                    catch (InvalidDataException error) { Fault(session, Describe(error)); return; }
                    lock (_gate)
                    {
                        if (!IsCurrentLocked(session)) return;
                        if (!HandleLocked(session, message)) return;
                    }
                }
                Fault(session, PipeReason);
            }
            catch (Exception error) { Fault(session, Describe(error)); }
        }

        private void ReadDiagnostics(object state)
        {
            var session = (Session)state;
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    int count = session.Error.Read(buffer, 0, buffer.Length);
                    if (count <= 0) return;
                    lock (_gate) { if (ReferenceEquals(_session, session)) AppendStderrLocked(buffer, count); }
                }
            }
            catch (Exception) { }
        }

        private bool HandleLocked(Session session, SensorMessage message)
        {
            if (!String.Equals(message.Session, session.Id, StringComparison.Ordinal))
            { FaultLocked(session, Source + ": the backend reported a different session"); return false; }
            if (String.Equals(message.Kind, "hello", StringComparison.Ordinal))
            {
                if (session.Handshake) { FaultLocked(session, Source + ": the backend repeated its handshake"); return false; }
                session.Handshake = true;
                return true;
            }
            if (!String.Equals(message.Kind, "snapshot", StringComparison.Ordinal))
            { FaultLocked(session, Source + ": the backend sent an unexpected frame"); return false; }
            if (!session.Handshake)
            { FaultLocked(session, Source + ": the backend sent a status frame before its handshake"); return false; }
            // Rejected frames are duplicates, out-of-order or implausible timestamps; they must not refresh the reading.
            if (!session.Freshness.Accept(message, DateTime.UtcNow, _clock(), session.Refresh)) return true;
            session.Started = true;
            _cpu = Reading(message, true);
            _gpu = Reading(message, false);
            _backend = "Running";
            return true;
        }

        private static MetricValue Reading(SensorMessage message, bool cpu)
        {
            SensorReading reading = TemperatureSelection.Highest(message.Readings, cpu);
            if (reading != null && TemperatureSelection.ValidValue(reading.Celsius))
                return MetricValue.Available(reading.Celsius.Value, Source + ": " + reading.DeviceName + " / " + reading.SensorName);
            string status = cpu ? message.CpuStatus : message.GpuStatus;
            string reason = String.IsNullOrEmpty(message.Detail) ? (cpu ? "CPU" : "GPU") + " temperature is unavailable" : message.Detail;
            if (String.Equals(status, "Unsupported", StringComparison.Ordinal)) return MetricValue.Missing(MetricStatus.Unsupported, reason);
            if (String.Equals(status, "AccessDenied", StringComparison.Ordinal)) return MetricValue.Missing(MetricStatus.AccessDenied, reason);
            if (String.Equals(status, "Disabled", StringComparison.Ordinal)) return MetricValue.Missing(MetricStatus.Disabled, reason);
            if (String.Equals(status, "WarmingUp", StringComparison.Ordinal)) return MetricValue.Missing(MetricStatus.WarmingUp, reason);
            return MetricValue.Missing(MetricStatus.Error, reason);
        }

        private void Supervise(Session session)
        {
            while (true)
            {
                int revision;
                lock (_gate)
                {
                    if (!IsCurrentLocked(session)) { session.Abandoned = true; return; }
                    if (session.Fault != null) return;
                    long now = _clock();
                    if (!session.Started)
                    {
                        if (now - session.StartedTicks >= StartBudgetTicks) { FaultLocked(session, StartBudgetReason); return; }
                    }
                    else if (!session.Freshness.IsFresh(DateTime.UtcNow, now, session.Refresh)) { FaultLocked(session, StaleReason); return; }
                    revision = session.Revision;
                    if (revision == session.ObservedRevision) Monitor.Wait(_gate, PollMilliseconds);
                }
                if (revision != session.ObservedRevision)
                {
                    try { SendConfigure(session); }
                    catch (Exception error) { Fault(session, Describe(error)); return; }
                }
            }
        }

        private void SendConfigure(Session session)
        {
            int revision;
            int refresh;
            string id;
            bool cpu;
            lock (_gate)
            {
                revision = session.Revision;
                refresh = session.Refresh;
                id = session.Id;
                cpu = session.CpuEnabled;
            }
            var message = new SensorMessage { Kind = "configure", Session = id, RefreshSeconds = refresh, CpuEnabled = cpu };
            byte[] bytes = Utf8.GetBytes(SensorProtocol.Encode(message) + "\n");
            session.Input.Write(bytes, 0, bytes.Length);
            session.Input.Flush();
            lock (_gate) { session.ObservedRevision = revision; }
        }

        private static void StopQuietly(Session session)
        {
            try
            {
                if (session.Input == null || session.Process.HasExited) return;
                byte[] bytes = Utf8.GetBytes(SensorProtocol.Encode(new SensorMessage { Kind = "stop", Session = session.Id }) + "\n");
                session.Input.Write(bytes, 0, bytes.Length);
                session.Input.Flush();
                session.Process.WaitForExit(StopWaitMilliseconds);
            }
            catch (Exception) { }
        }

        private void Fault(Session session, string reason)
        {
            lock (_gate) { FaultLocked(session, reason); }
            NotifyWorker();
        }

        private void FaultLocked(Session session, string reason)
        {
            if (IsCurrentLocked(session) && session.Fault == null)
            {
                session.Fault = reason;
                _cpu = MetricValue.Missing(MetricStatus.Error, reason);
                _gpu = MetricValue.Missing(MetricStatus.Error, reason);
                _backend = "Error";
                _retryAt = _clock() + RetryTicks;
            }
            session.Abandoned = true;
            Monitor.PulseAll(_gate);
        }

        private void EvaluateLocked()
        {
            Session session = _session;
            if (session == null || session.Abandoned || session.Process == null) return;
            long now = _clock();
            if (!session.Started)
            {
                if (now - session.StartedTicks >= StartBudgetTicks) FaultLocked(session, StartBudgetReason);
                return;
            }
            if (!session.Freshness.IsFresh(DateTime.UtcNow, now, session.Refresh)) FaultLocked(session, StaleReason);
        }

        private void Cleanup(Session session, string reason)
        {
            bool faulted;
            lock (_gate)
            {
                if (reason != null && session.Fault == null && !session.Abandoned) FaultLocked(session, reason);
                faulted = session.Fault != null;
                session.Abandoned = true;
                Monitor.PulseAll(_gate);
            }
            try
            {
                Process process = session.Process;
                if (process != null)
                {
                    if (!faulted) StopQuietly(session);
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }
                CloseQuietly(session.Input);
                CloseQuietly(session.Output);
                CloseQuietly(session.Error);
                JoinQuietly(session.OutputThread);
                JoinQuietly(session.ErrorThread);
                if (process != null)
                {
                    try { process.WaitForExit(ExitWaitMilliseconds); }
                    catch (Exception) { }
                    try { process.Dispose(); }
                    catch (Exception) { }
                }
                if (session.Job != null) { try { session.Job.Dispose(); } catch (Exception) { } }
                if (session.Lease != null) { try { session.Lease.Dispose(); } catch (Exception) { } }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session)) _session = null;
                    _processId = 0;
                    if (!faulted) SetWaitingLocked();
                    Monitor.PulseAll(_gate);
                }
            }
        }

        private void ClearStderrLocked() { _stderrCount = 0; _stderrWrite = 0; }

        private void AppendStderrLocked(byte[] buffer, int count)
        {
            for (int i = 0; i < count && i < buffer.Length; i++)
            {
                _stderr[_stderrWrite] = buffer[i];
                _stderrWrite = (_stderrWrite + 1) % StderrCapacity;
                if (_stderrCount < StderrCapacity) _stderrCount++;
            }
        }

        private string StderrTextLocked()
        {
            if (_stderrCount == 0) return String.Empty;
            var bytes = new byte[_stderrCount];
            int start = _stderrCount == StderrCapacity ? _stderrWrite : 0;
            for (int i = 0; i < _stderrCount; i++) bytes[i] = _stderr[(start + i) % StderrCapacity];
            return new UTF8Encoding(false, false).GetString(bytes);
        }

        private static string Describe(Exception error)
        {
            if (error == null) return Source + ": unknown backend error";
            string message = Source + ": " + error.GetType().Name + ": " + error.Message;
            return message.Length > 512 ? message.Substring(0, 512) : message;
        }

        private static void CloseQuietly(Stream stream)
        {
            if (stream == null) return;
            try { stream.Close(); }
            catch (Exception) { }
        }

        private static void JoinQuietly(Thread thread)
        {
            if (thread == null) return;
            try { thread.Join(JoinMilliseconds); }
            catch (Exception) { }
        }

        private sealed class Session
        {
            internal readonly string Id;
            internal long StartedTicks;
            internal readonly SensorFreshness Freshness = new SensorFreshness();
            internal int Refresh;
            internal int Revision;
            internal int ObservedRevision;
            internal int ProcessId;
            internal bool CpuEnabled;
            internal bool Handshake;
            internal bool Started;
            internal bool Abandoned;
            internal string Fault;
            internal SensorPayloadLease Lease;
            internal ISensorJob Job;
            internal Process Process;
            internal Stream Input;
            internal Stream Output;
            internal Stream Error;
            internal Thread OutputThread;
            internal Thread ErrorThread;
            internal Session(string id, int refresh) { Id = id; Refresh = refresh; }
        }
    }
}
