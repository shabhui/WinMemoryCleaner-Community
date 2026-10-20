using System;
using System.Threading;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    // One background worker, no replay queue. The UI rejects stale generations.
    public sealed class MonitoringPump : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Action<MonitoringDemand, int> _sample;
        private readonly Action _suspend;
        private readonly Timer _timer;
        private MonitoringDemand _demand;
        private int _generation;
        private bool _disposed;
        private bool _working;

        public MonitoringPump(Action<MonitoringDemand, int> sample, Action suspend)
        {
            if (sample == null || suspend == null) throw new ArgumentNullException(sample == null ? "sample" : "suspend");
            _sample = sample; _suspend = suspend;
            _timer = new Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
        }
        public void SetDemand(MonitoringDemand demand, int seconds)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _demand = demand; _generation++;
                _timer.Change(0, demand.CpuGpu ? MonitoringPolicy.NormalizeRefresh(seconds) * 1000 : Timeout.Infinite);
            }
        }
        public bool IsCurrent(int generation)
        {
            lock (_sync) return MonitoringPolicy.IsCurrent(generation, _generation, _disposed);
        }
        private void Tick(object state)
        {
            MonitoringDemand demand; int generation;
            lock (_sync)
            {
                if (_working) return;
                _working = true; demand = _demand; generation = _generation;
            }
            try
            {
                if (demand.CpuGpu && IsCurrent(generation)) _sample(demand, generation);
            }
            catch (Exception)
            {
                // Provider failures are represented by the owner; they never kill a pool thread.
            }
            finally
            {
                bool suspend;int settledGeneration;
                lock (_sync) { suspend = !_demand.CpuGpu || _disposed; settledGeneration = _generation; }
                // Keep _working true, but never hold the UI state lock during provider shutdown.
                if (suspend) { try { _suspend(); } catch (Exception) { } }
                bool retry;
                lock (_sync)
                {
                    _working = false;
                    retry = _generation != settledGeneration || (!_disposed && _demand.CpuGpu && _generation != generation);
                }
                if (retry) ThreadPool.QueueUserWorkItem(Tick);
            }
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true; _generation++; _demand = new MonitoringDemand(false, false, false);
                _timer.Dispose();
            }
            // Suspend runs off the UI thread and stays serialized with any current sample.
            ThreadPool.QueueUserWorkItem(Tick);
        }
    }
}
#pragma warning restore 1591
