using System;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    internal sealed partial class TemperatureSampler
    {
        private readonly ISensorBackend _backend;
        private readonly ITemperatureSource _native;
        private readonly ITemperatureSource _libre;
        private readonly ITemperatureSource _open;
        private readonly object _demandGate=new object();
        private bool _requested=true;

        internal TemperatureSampler() : this(new SensorBackendController()) { }
        // A null backend permits isolated verification of the existing native sources.
        internal TemperatureSampler(ISensorBackend backend)
            : this(backend,null,new WmiTemperatureSource("LibreHardwareMonitor"),new WmiTemperatureSource("OpenHardwareMonitor")) { }
        internal TemperatureSampler(ISensorBackend backend,ITemperatureSource native,ITemperatureSource libre,ITemperatureSource open)
        {
            if(libre==null)throw new ArgumentNullException("libre");
            if(open==null)throw new ArgumentNullException("open");
            _backend=backend;_native=native;_libre=libre;_open=open;
        }
        internal string BackendStatus { get { lock(_demandGate) { return _disposed||!_requested||_backend==null?"Disabled":_backend.BackendStatus; } } }
        internal string DriverStatus { get { return _backend==null?"NotChecked":_backend.DriverStatus; } }
        internal string Diagnostic { get { return _backend==null?"Universal backend disabled":_backend.Diagnostic; } }
        internal int ProcessId { get { return _backend==null?0:_backend.ProcessId; } }
        internal void Configure(int seconds)
        { if(!_disposed&&_backend!=null)_backend.Configure(SensorProtocol.NormalizeRefresh(seconds)); }
        internal void ResetDriverState()
        { lock(_demandGate) { if(!_disposed&&_backend!=null)_backend.ResetDriverState(); } }
        internal void SetDemand(bool requested)
        {
            lock(_demandGate)
            {
                if(_disposed)return;
                _requested=requested;
                if(!requested&&_backend!=null)_backend.Suspend();
            }
        }

        // Compatibility seams retain the native WMI tests without starting a backend.
        internal Func<bool> ProviderRunning
        {
            get { var source=_libre as WmiTemperatureSource;return source==null?null:source.PresenceOverride; }
            set { var source=_libre as WmiTemperatureSource;if(source!=null)source.PresenceOverride=value; }
        }
        internal int WmiAttempts { get { var source=_libre as WmiTemperatureSource;return source==null?0:source.Attempts; } }
        internal bool ProviderExists() { var source=_libre as WmiTemperatureSource;return source!=null&&source.ProviderExists(); }
        private void LibreTemperatures(out MetricValue cpu,out MetricValue gpu) { _libre.Sample(out cpu,out gpu); }

        internal void Sample(out MetricValue cpu,out MetricValue gpu)
        {
            ReadTemperatures(out cpu,out gpu);
            lock(_demandGate) { if(_disposed||!_requested)cpu=gpu=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling disabled"); }
        }
        private void ReadTemperatures(out MetricValue cpu,out MetricValue gpu)
        {
            lock(_demandGate)
            {
                if(_disposed||!_requested){cpu=gpu=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling disabled");return;}
                cpu=gpu=MetricValue.Missing(MetricStatus.Unsupported,"Universal backend disabled");
                if(_backend!=null)
                {
                    try { _backend.GetSnapshot(out cpu,out gpu); }
                    catch(Exception error) { cpu=gpu=Failure("Universal backend",error); }
                }
            }
            cpu=Validate(cpu);gpu=Validate(gpu);
            if(Complete(cpu,gpu))return;
            MetricValue nextCpu,nextGpu;
            if(_native==null)SampleNative(out nextCpu,out nextGpu);
            else ReadSource(_native,"Native temperature source",out nextCpu,out nextGpu);
            cpu=Merge(cpu,nextCpu);gpu=Merge(gpu,nextGpu);
            if(Complete(cpu,gpu))return;
            ReadSource(_libre,"LibreHardwareMonitor WMI",out nextCpu,out nextGpu);
            cpu=Merge(cpu,nextCpu);gpu=Merge(gpu,nextGpu);
            if(Complete(cpu,gpu))return;
            ReadSource(_open,"OpenHardwareMonitor WMI",out nextCpu,out nextGpu);
            cpu=Merge(cpu,nextCpu);gpu=Merge(gpu,nextGpu);
        }
        private void SampleNative(out MetricValue cpu,out MetricValue gpu)
        {
            try { if(_asus==null)_asus=new AsusCpuTemperatureSource();cpu=_asus.Sample(); }
            catch(Exception error) { cpu=Failure("ASUS CPU temperature",error); }
            try { gpu=NvidiaTemperature(); }
            catch(Exception error) { gpu=Failure("NVIDIA thermal API",error);CloseNvidia();_nvRetry=DateTime.UtcNow.AddSeconds(30); }
        }
        private static void ReadSource(ITemperatureSource source,string name,out MetricValue cpu,out MetricValue gpu)
        {
            try { source.Sample(out cpu,out gpu); }
            catch(Exception error) { cpu=gpu=Failure(name,error); }
        }
        private static MetricValue Failure(string name,Exception error)
        { return MetricValue.Missing(error is UnauthorizedAccessException?MetricStatus.AccessDenied:MetricStatus.Error,name+": "+error.Message); }
        private static bool Complete(MetricValue cpu,MetricValue gpu)
        { return cpu.Status==MetricStatus.Available&&gpu.Status==MetricStatus.Available; }
        private static MetricValue Validate(MetricValue value)
        {
            if(value==null)return MetricValue.Missing(MetricStatus.Error,"No temperature reading returned");
            if(value.Status==MetricStatus.Available&&!TemperatureSelection.ValidValue(value.Value))
                return MetricValue.Missing(MetricStatus.Error,"invalid temperature reading: "+value.Detail);
            return value;
        }
        private static int FailurePriority(MetricStatus status)
        {
            switch(status)
            {
                case MetricStatus.Error:return 5;
                case MetricStatus.AccessDenied:return 4;
                case MetricStatus.WarmingUp:return 3;
                case MetricStatus.Unsupported:return 2;
                default:return 1;
            }
        }
        private static MetricValue Merge(MetricValue selected,MetricValue candidate)
        {
            if(selected.Status==MetricStatus.Available)return selected;
            candidate=Validate(candidate);
            if(candidate.Status==MetricStatus.Available)
                return MetricValue.Available(candidate.Value.Value,candidate.Detail+"; fallback: "+selected.Detail);
            var status=FailurePriority(selected.Status)>=FailurePriority(candidate.Status)?selected.Status:candidate.Status;
            return MetricValue.Missing(status,selected.Detail+"; "+candidate.Detail);
        }
        internal void Suspend()
        {
            if(_disposed)return;
            lock(_demandGate) { if(_backend!=null)_backend.Suspend(); }
            if(_native!=null)_native.Suspend();
            _libre.Suspend();_open.Suspend();CloseNvidia();if(_asus!=null)_asus.Suspend();
        }
        private void DisposeSources()
        {
            if(_backend!=null)_backend.Dispose();
            if(_native!=null)_native.Dispose();
            _libre.Dispose();_open.Dispose();if(_asus!=null)_asus.Dispose();
        }
    }
}
