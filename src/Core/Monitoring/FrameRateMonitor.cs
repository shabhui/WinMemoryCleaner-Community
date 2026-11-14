using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace WinMemoryCleaner
{
    /// <summary>Demand-driven, read-only successful application DirectX Present monitoring.</summary>
    public sealed class FrameRateMonitor : IFrameRateMonitor
    {
        private readonly object _gate = new object();
        private readonly Func<long> _clock;
        private readonly long _frequency;
        private readonly Func<ProcessSnapshot> _foreground;
        private readonly Func<ProcessSnapshot, string> _verify;
        private readonly Func<ProcessSnapshot, Action<Guid, int, int, int, int, long, int, byte[], string[], int[]>, Action<string, int>, IDisposable> _factory;
        private readonly int _selfId;
        private bool _active;
        private bool _disposed;
        private bool _followForeground;
        private bool _workerScheduled;
        private bool _cleanupFailed;
        private long _generation;
        private long _captureGeneration;
        private ProcessSnapshot _target;
        private FramePresentAccumulator _accumulator;
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The single serial control worker disposes capture off the UI caller and waits for native callback completion before another start.")]
        private IDisposable _capture;
        private string _failure;
        private string _failureSource;
        private int _failureCode;

        /// <summary>Creates an inactive monitor. No process handle, thread, enumeration or ETW session is opened.</summary>
        public FrameRateMonitor() : this(new FramePresentProcess()) { }

        private FrameRateMonitor(FramePresentProcess process)
            : this(Stopwatch.GetTimestamp, Stopwatch.Frequency, process.ResolveForeground, process.Verify,
                FramePresentCapture.Create, process.SelfId) { }

        internal FrameRateMonitor(Func<long> clock, long frequency, Func<ProcessSnapshot> foreground,
            Func<ProcessSnapshot, string> verify,
            Func<ProcessSnapshot, Action<Guid, int, int, int, int, long, int, byte[], string[], int[]>, Action<string, int>, IDisposable> factory,
            int selfId)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            if (foreground == null) throw new ArgumentNullException("foreground");
            if (verify == null) throw new ArgumentNullException("verify");
            if (factory == null) throw new ArgumentNullException("factory");
            if (frequency <= 0 || frequency > long.MaxValue / 4) throw new ArgumentOutOfRangeException("frequency");
            if (selfId <= 0) throw new ArgumentOutOfRangeException("selfId");
            _clock = clock; _frequency = frequency; _foreground = foreground;
            _verify = verify; _factory = factory; _selfId = selfId;
        }

        /// <summary>Invalidates old readings immediately; native start/stop is serialized off the caller.</summary>
        /// <param name="active">True only while FPS is enabled and its presentation is visible.</param>
        public void SetActive(bool active)
        {
            lock (_gate)
            {
                if (_disposed || _active == active) return;
                _active = active;
                if (_followForeground) _target = null;
                ResetLocked();
                ScheduleLocked();
            }
        }

        /// <summary>Copies a transient manual identity, or defers foreground discovery to Sample.</summary>
        /// <param name="followForeground">True to follow the current foreground process, excluding this tool.</param>
        /// <param name="target">A manual process snapshot, or null for no selection.</param>
        public void SetTarget(bool followForeground, ProcessSnapshot target)
        {
            ProcessSnapshot copy = followForeground ? null : Copy(target);
            lock (_gate)
            {
                if (_disposed) return;
                _followForeground = followForeground;
                _target = copy;
                ResetLocked();
                ScheduleLocked();
            }
        }

        private ProcessSnapshot Copy(ProcessSnapshot target)
        {
            if (target == null || target.Id <= 0 || target.Id == _selfId) return null;
            return new ProcessSnapshot { Id = target.Id, CreationTime = target.CreationTime, Name = FrameRateReading.DisplayName(target.Name) };
        }

        private static bool SameIdentity(ProcessSnapshot left, ProcessSnapshot right)
        {
            return left == null ? right == null : right != null && left.Id == right.Id && left.CreationTime == right.CreationTime;
        }

        private void ResetLocked()
        {
            _generation++;
            _accumulator = null;
            _failure = null; _failureSource = null; _failureCode = 0;
        }

        private bool DemandLocked(long generation)
        {
            return !_disposed && _active && _target != null && _failure == null && !_cleanupFailed && generation == _generation;
        }

        private FrameRateReading StateLocked()
        {
            string name = _target == null ? string.Empty : _target.Name;
            if (_disposed || !_active) return FrameRateReading.Missing("FpsDisabled", name, "DirectX Present", 0);
            if (_cleanupFailed) return FrameRateReading.Missing("FpsError", name, "DirectX Present cleanup", 0);
            if (_failure != null) return FrameRateReading.Missing(_failure, name, _failureSource, _failureCode);
            return FrameRateReading.Missing(_target == null ? "FpsChooseTarget" : "FpsWarmingUp", name, "DirectX Present", 0);
        }

        private void FailLocked(long generation, string state, string source, int code)
        {
            if (_disposed || !_active || generation != _generation || _failure != null) return;
            _failure = FrameRateReading.FailureKey(state);
            _failureSource = source; _failureCode = code;
            if (_accumulator != null) _accumulator.Fail(_failure);
            ScheduleLocked();
        }

        /// <summary>Resolves/verifies identity on the sampling worker and returns no stale or invented FPS.</summary>
        /// <returns>A rate for the current identity, or an explicit unavailable state with a null value.</returns>
        public FrameRateReading Sample()
        {
            long generation;
            bool follow;
            ProcessSnapshot target;
            lock (_gate)
            {
                if (_disposed || !_active) return StateLocked();
                generation = _generation; follow = _followForeground; target = Copy(_target);
            }
            if (follow)
            {
                ProcessSnapshot foreground;
                try { foreground = Copy(_foreground()); }
                catch (Win32Exception error)
                {
                    lock (_gate) { FailLocked(generation, FramePresentNativeMethods.ErrorState(error.NativeErrorCode), "Foreground process", error.NativeErrorCode); return StateLocked(); }
                }
                catch (Exception)
                {
                    lock (_gate) { FailLocked(generation, "FpsError", "Foreground process", 0); return StateLocked(); }
                }
                lock (_gate)
                {
                    if (_disposed || !_active || !_followForeground || generation != _generation) return StateLocked();
                    if (!SameIdentity(_target, foreground))
                    {
                        _target = foreground;
                        ResetLocked();
                        ScheduleLocked();
                    }
                    generation = _generation; target = Copy(_target);
                }
            }
            lock (_gate)
            {
                if (!DemandLocked(generation)) return StateLocked();
            }
            if (!Verify(generation, target)) { lock (_gate) return StateLocked(); }
            long now;
            try { now = _clock(); }
            catch (Exception)
            {
                lock (_gate) { FailLocked(generation, "FpsError", "QPC clock", 0); return StateLocked(); }
            }
            lock (_gate)
            {
                if (!DemandLocked(generation)) return StateLocked();
                ScheduleLocked();
                if (_accumulator == null) return StateLocked();
                FrameRateReading reading = _accumulator.Read(now);
                if (_accumulator.Failure != null)
                {
                    FailLocked(generation, _accumulator.Failure, "DirectX Present ETW", 0);
                    return StateLocked();
                }
                return new FrameRateReading(reading.Metric, _target.Name, reading.StateKey);
            }
        }

        private bool Verify(long generation, ProcessSnapshot target)
        {
            string state;
            int code = 0;
            try { state = _verify(Copy(target)); }
            catch (Win32Exception error) { code = error.NativeErrorCode; state = FramePresentNativeMethods.ErrorState(code); }
            catch (Exception) { state = "FpsError"; }
            if (state == null) return true;
            lock (_gate) FailLocked(generation, state, "Process identity", code);
            return false;
        }

        // There is one coalescing control work item, not a queue of selections.
        // Native factory/Dispose never runs under _gate or on SetActive/SetTarget/Dispose callers.
        private void ScheduleLocked()
        {
            if (_workerScheduled || (_capture == null && !DemandLocked(_generation))) return;
            _workerScheduled = true;
            if (!ThreadPool.QueueUserWorkItem(Control))
            {
                _workerScheduled = false;
                _failure = "FpsError"; _failureSource = "Capture worker"; _failureCode = 0;
            }
        }

        private void Control(object unused)
        {
            try
            {
                for (;;)
                {
                    IDisposable old;
                    ProcessSnapshot target;
                    long generation;
                    lock (_gate)
                    {
                        generation = _generation;
                        old = _capture != null && (!DemandLocked(_captureGeneration)) ? _capture : null;
                        target = old == null && _capture == null && DemandLocked(generation) ? Copy(_target) : null;
                        if (old == null && target == null) { _workerScheduled = false; return; }
                    }
                    if (old != null)
                    {
                        // Retain ownership while Stop blocks. A throwing Stop cannot
                        // prove the consumer exited, so permanently prevent another start.
                        try { old.Dispose(); }
                        catch (Exception)
                        {
                            lock (_gate)
                            {
                                _cleanupFailed = true;
                                FailLocked(_generation, "FpsError", "DirectX Present cleanup", 0);
                                _workerScheduled = false;
                            }
                            return;
                        }
                        lock (_gate) _capture = null;
                        continue;
                    }
                    if (!Verify(generation, target)) continue;
                    long epoch = _clock();
                    lock (_gate)
                    {
                        if (!DemandLocked(generation)) continue;
                        _accumulator = new FramePresentAccumulator(_frequency, target.Id, epoch);
                    }
                    IDisposable opened = null;
                    try
                    {
                        opened = _factory(Copy(target),
                            delegate(Guid provider, int id, int version, int pid, int tid, long timestamp, int pointerSize, byte[] payload, string[] names, int[] types)
                            { Receive(generation, provider, id, version, pid, tid, timestamp, pointerSize, payload, names, types); },
                            delegate(string state, int code) { lock (_gate) FailLocked(generation, state, "DirectX Present ETW", code); });
                        if (opened == null) { lock (_gate) FailLocked(generation, "FpsError", "DirectX Present ETW", 0); }
                    }
                    catch (Win32Exception error) { lock (_gate) FailLocked(generation, FramePresentNativeMethods.ErrorState(error.NativeErrorCode), "DirectX Present ETW", error.NativeErrorCode); }
                    catch (Exception) { lock (_gate) FailLocked(generation, "FpsError", "DirectX Present ETW", 0); }
                    lock (_gate)
                    {
                        // Even an obsolete result is owned and must be stopped before
                        // the worker considers the newest demand. Never drop its handle.
                        _capture = opened;
                        _captureGeneration = generation;
                    }
                }
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    FailLocked(_generation, "FpsError", "Capture worker", 0);
                    _workerScheduled = false;
                    ScheduleLocked();
                }
            }
        }

        private void Receive(long generation, Guid provider, int id, int version, int pid, int tid, long timestamp,
            int pointerSize, byte[] payload, string[] names, int[] types)
        {
            lock (_gate)
            {
                if (!DemandLocked(generation) || _accumulator == null) return;
                try
                {
                    _accumulator.Accept(provider, id, version, pid, tid, timestamp, pointerSize, payload, names, types);
                    if (_accumulator.Failure != null) FailLocked(generation, _accumulator.Failure, "DirectX Present ETW", 0);
                }
                catch (Exception) { FailLocked(generation, "FpsError", "DirectX Present ETW", 0); }
            }
        }

        /// <summary>Immediately disables publication. The control worker owns asynchronous native cleanup.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; _active = false;
                ResetLocked();
                ScheduleLocked();
            }
            GC.SuppressFinalize(this);
        }
    }
}
