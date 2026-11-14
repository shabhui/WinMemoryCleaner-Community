using System;
using System.Collections.Generic;

namespace WinMemoryCleaner
{
    // One target, no frame queue and no UI callbacks. Callers serialize Accept/Read.
    internal sealed class FramePresentAccumulator
    {
        private const int MaximumPending = 256;
        private const int MaximumSwapchains = 16;
        private readonly long _frequency;
        private readonly int _processId;
        private readonly long _epoch;
        private readonly Dictionary<FramePresentPairKey, FramePresentStart> _pending = new Dictionary<FramePresentPairKey, FramePresentStart>(MaximumPending);
        private readonly Dictionary<FramePresentChainKey, FramePresentWindow> _chains = new Dictionary<FramePresentChainKey, FramePresentWindow>(MaximumSwapchains);
        private readonly FramePresentPairKey[] _expiredPending = new FramePresentPairKey[MaximumPending];
        private readonly FramePresentChainKey[] _expiredChains = new FramePresentChainKey[MaximumSwapchains];
        private long _lastSweep;
        private string _failure;
        private bool _wasAvailable;

        internal FramePresentAccumulator(long frequency, int processId, long epoch)
        {
            if (frequency <= 0 || frequency > long.MaxValue / 4) throw new ArgumentOutOfRangeException("frequency");
            if (processId <= 0) throw new ArgumentOutOfRangeException("processId");
            if (epoch < 0) throw new ArgumentOutOfRangeException("epoch");
            _frequency = frequency; _processId = processId; _epoch = epoch; _lastSweep = epoch;
        }

        internal string Failure { get { return _failure; } }

        internal void Fail(string state)
        {
            _failure = FrameRateReading.FailureKey(state);
            _pending.Clear();
            _chains.Clear();
        }

        internal void Accept(Guid provider, int eventId, int version, int processId, int threadId, long timestamp,
            int pointerSize, byte[] payload, string[] names, int[] types)
        {
            if (_failure != null || processId != _processId || threadId <= 0 || timestamp < _epoch || !FramePresentDecoder.Relevant(provider, eventId)) return;
            FramePresentPairKey key = new FramePresentPairKey(provider, threadId, FramePresentDecoder.Family(provider, eventId));
            ulong swapchain;
            FramePresentDecodeResult decoded = FramePresentDecoder.Decode(provider, eventId, version, pointerSize, payload, names, types, out swapchain);
            if (decoded == FramePresentDecodeResult.Unsupported) { Fail("FpsUnsupported"); return; }
            if (decoded != FramePresentDecodeResult.Valid) { _pending.Remove(key); return; }
            bool start = FramePresentDecoder.StartEvent(provider, eventId);
            if (start)
            {
                if (_pending.Count >= MaximumPending || (timestamp >= _lastSweep && timestamp - _lastSweep >= Math.Max(1, _frequency / 4))) Sweep(timestamp);
                if (_pending.Count >= MaximumPending && !_pending.ContainsKey(key)) { Fail("FpsEventLoss"); return; }
                // A duplicate/re-entrant Start replaces the unpaired old Start;
                // it cannot add a successful frame without its own validated Stop.
                _pending[key] = new FramePresentStart(timestamp, pointerSize, swapchain);
                return;
            }
            FramePresentStart pending;
            if (!_pending.TryGetValue(key, out pending)) return;
            _pending.Remove(key);
            if (pending.PointerSize != pointerSize || timestamp < pending.Timestamp || timestamp - pending.Timestamp > _frequency * 2) return;
            FramePresentChainKey chainKey = new FramePresentChainKey(provider, pending.Swapchain);
            FramePresentWindow window;
            if (!_chains.TryGetValue(chainKey, out window))
            {
                if (_chains.Count >= MaximumSwapchains) Sweep(timestamp);
                if (_chains.Count >= MaximumSwapchains) { Fail("FpsEventLoss"); return; }
                window = new FramePresentWindow();
                _chains.Add(chainKey, window);
            }
            window.Add(pending.Timestamp, _frequency * 2);
        }

        private void Sweep(long now)
        {
            int count = 0;
            foreach (KeyValuePair<FramePresentPairKey, FramePresentStart> entry in _pending)
                if (now >= entry.Value.Timestamp && now - entry.Value.Timestamp > _frequency * 2) _expiredPending[count++] = entry.Key;
            for (int i = 0; i < count; i++) _pending.Remove(_expiredPending[i]);
            count = 0;
            foreach (KeyValuePair<FramePresentChainKey, FramePresentWindow> entry in _chains)
            {
                entry.Value.Prune(now, _frequency * 2);
                if (entry.Value.Count == 0) _expiredChains[count++] = entry.Key;
            }
            for (int i = 0; i < count; i++) _chains.Remove(_expiredChains[i]);
            if (now >= _lastSweep) _lastSweep = now;
        }

        internal FrameRateReading Read(long now)
        {
            if (_failure != null) return FrameRateReading.Missing(_failure, string.Empty, "DirectX Present", 0);
            if (now < _epoch) return FrameRateReading.Missing("FpsError", string.Empty, "QPC clock", 0);
            Sweep(now);
            FramePresentWindow dominant = null;
            foreach (FramePresentWindow window in _chains.Values)
            {
                if (window.Count == 0 || window.Last > now || now - window.Last > _frequency) continue;
                if (dominant == null || window.Count > dominant.Count || (window.Count == dominant.Count && window.Last > dominant.Last)) dominant = window;
            }
            if (dominant != null && dominant.Count >= 2 && dominant.Last > dominant.First)
            {
                double rate = (dominant.Count - 1) * (double)_frequency / (dominant.Last - dominant.First);
                _wasAvailable = true;
                return new FrameRateReading(MetricValue.Available(rate, "DirectX application Present (dominant swapchain)"), string.Empty, "FpsAvailable");
            }
            string state = dominant != null ? "FpsWarmingUp" : (_wasAvailable || now - _epoch > _frequency * 2 ? "FpsNoFrames" : "FpsWarmingUp");
            return FrameRateReading.Missing(state, string.Empty, "DirectX Present", 0);
        }
    }

    internal sealed class FramePresentWindow
    {
        private readonly long[] _timestamps = new long[512];
        private int _head;
        private int _count;
        private long _lastAccepted = -1;
        internal int Count { get { return _count; } }
        internal long First { get { return _timestamps[_head]; } }
        internal long Last { get { return _timestamps[(_head + _count - 1) % _timestamps.Length]; } }

        internal void Add(long timestamp, long retention)
        {
            if (timestamp <= _lastAccepted) return;
            Prune(timestamp, retention);
            if (_count == _timestamps.Length) { _head = (_head + 1) % _timestamps.Length; _count--; }
            _timestamps[(_head + _count) % _timestamps.Length] = timestamp;
            _count++;
            _lastAccepted = timestamp;
        }

        internal void Prune(long now, long retention)
        {
            while (_count > 0 && now >= First && now - First > retention)
            {
                _timestamps[_head] = 0;
                _head = (_head + 1) % _timestamps.Length;
                _count--;
            }
        }
    }

    internal struct FramePresentStart
    {
        internal readonly long Timestamp;
        internal readonly int PointerSize;
        internal readonly ulong Swapchain;
        internal FramePresentStart(long timestamp, int pointerSize, ulong swapchain) { Timestamp = timestamp; PointerSize = pointerSize; Swapchain = swapchain; }
    }

    internal struct FramePresentPairKey : IEquatable<FramePresentPairKey>
    {
        private readonly Guid _provider;
        private readonly int _threadId;
        private readonly int _family;
        internal FramePresentPairKey(Guid provider, int threadId, int family) { _provider = provider; _threadId = threadId; _family = family; }
        public bool Equals(FramePresentPairKey other) { return _provider == other._provider && _threadId == other._threadId && _family == other._family; }
        public override bool Equals(object obj) { return obj is FramePresentPairKey && Equals((FramePresentPairKey)obj); }
        public override int GetHashCode() { return _provider.GetHashCode() ^ _threadId ^ _family; }
        public static bool operator ==(FramePresentPairKey left, FramePresentPairKey right) { return left.Equals(right); }
        public static bool operator !=(FramePresentPairKey left, FramePresentPairKey right) { return !left.Equals(right); }
    }

    internal struct FramePresentChainKey : IEquatable<FramePresentChainKey>
    {
        private readonly Guid _provider;
        private readonly ulong _swapchain;
        internal FramePresentChainKey(Guid provider, ulong swapchain) { _provider = provider; _swapchain = swapchain; }
        public bool Equals(FramePresentChainKey other) { return _provider == other._provider && _swapchain == other._swapchain; }
        public override bool Equals(object obj) { return obj is FramePresentChainKey && Equals((FramePresentChainKey)obj); }
        public override int GetHashCode() { return _provider.GetHashCode() ^ _swapchain.GetHashCode(); }
        public static bool operator ==(FramePresentChainKey left, FramePresentChainKey right) { return left.Equals(right); }
        public static bool operator !=(FramePresentChainKey left, FramePresentChainKey right) { return !left.Equals(right); }
    }
}
