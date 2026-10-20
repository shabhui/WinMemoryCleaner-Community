using System;
using System.Threading;

namespace WinMemoryCleaner
{
    // The work lock serializes creation, sampling, suspension and release. The short state lock
    // never waits for hardware: UI callers can revoke cached temperature demand during a sample.
    internal sealed class MonitoringSession : IDisposable
    {
        private readonly object _state=new object(),_work=new object();
        private readonly Func<IResourceMonitor> _create;
        private IResourceMonitor _monitor;
        private ITemperatureConfiguration _configuration;
        private MonitoringDemand _demand;
        private bool _disposed,_reset;

        internal MonitoringSession(Func<IResourceMonitor> create)
        { if(create==null)throw new ArgumentNullException("create");_create=create; }
        internal void SetDemand(MonitoringDemand demand)
        {
            lock(_state)
            {
                if(_disposed)return;
                _demand=demand;
                if(_configuration!=null)_configuration.SetTemperatureDemand(demand.CpuGpu&&demand.Temperatures);
            }
        }
        internal void RequestDriverReset() { lock(_state) { if(!_disposed)_reset=true; } }
        private static ResourceSnapshot DisabledSnapshot()
        {
            var disabled=MetricValue.Missing(MetricStatus.Disabled,"Monitoring is not requested");
            return new ResourceSnapshot { CpuUsage=disabled,GpuUsage=disabled,CpuTemperature=disabled,GpuTemperature=disabled };
        }
        internal ResourceSnapshot Sample(MonitoringDemand demand,int refreshSeconds)
        {
            lock(_work)
            {
                lock(_state) { if(_disposed||!_demand.CpuGpu)return DisabledSnapshot(); }
                if(_monitor==null)
                {
                    var created=_create();if(created==null)throw new InvalidOperationException("The monitor factory returned no monitor");
                    bool cancelled;
                    lock(_state)
                    {
                        cancelled=_disposed;
                        if(!cancelled) { _monitor=created;_configuration=created as ITemperatureConfiguration; }
                    }
                    if(cancelled) { created.Dispose();return DisabledSnapshot(); }
                }
                bool temperatures,processes,reset;ITemperatureConfiguration configuration;
                lock(_state)
                {
                    if(_disposed||!_demand.CpuGpu)return DisabledSnapshot();
                    temperatures=demand.Temperatures&&_demand.Temperatures;
                    processes=demand.Processes&&_demand.Processes;
                    configuration=_configuration;
                    if(configuration!=null)configuration.SetTemperatureDemand(temperatures);
                    reset=_reset;_reset=false;
                }
                if(configuration!=null)
                {
                    configuration.ConfigureTemperatures(MonitoringPolicy.NormalizeRefresh(refreshSeconds));
                    if(reset)configuration.ResetTemperatureDriver();
                }
                var snapshot=_monitor.Sample(processes,temperatures);
                lock(_state)
                {
                    if(_disposed||!_demand.CpuGpu)return DisabledSnapshot();
                    if(snapshot!=null&&!_demand.Temperatures)
                    {
                        snapshot.CpuTemperature=snapshot.GpuTemperature=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling is not requested");
                        snapshot.SensorBackendStatus="Disabled";
                    }
                    if(snapshot!=null&&!_demand.Processes)snapshot.Processes.Clear();
                }
                return snapshot;
            }
        }
        internal void Suspend()
        {
            lock(_work)
            {
                if(_monitor==null)return;
                _monitor.Suspend();
            }
        }
        private void ReleaseOwned(object state)
        {
            lock(_work)
            {
                lock(_state) { _configuration=null; }
                if(_monitor==null)return;
                try { _monitor.Dispose(); }
                catch(Exception) { } // Release is best-effort; the backend's own Job still owns process cleanup.
                finally { _monitor=null; }
            }
        }
        public void Dispose()
        {
            lock(_state)
            {
                if(_disposed)return;
                _disposed=true;_demand=new MonitoringDemand(false,false,false);_reset=false;
                if(_configuration!=null)_configuration.SetTemperatureDemand(false);
            }
            ThreadPool.QueueUserWorkItem(ReleaseOwned);
            GC.SuppressFinalize(this);
        }
    }
}
