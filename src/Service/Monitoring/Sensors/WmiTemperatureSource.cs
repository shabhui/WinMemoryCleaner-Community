using System;
using System.Collections.Generic;
using System.Diagnostics;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    internal sealed class WmiTemperatureSource : ITemperatureSource
    {
        private readonly string _provider;
        private readonly Func<bool> _present;
        private readonly Func<IList<SensorReading>> _query;
        private readonly Func<long> _clock;
        private long _gateUntil,_retryAt;
        private bool _disposed;
        private MetricValue _cpuFailure,_gpuFailure;
        internal int Attempts { get; private set; }
        internal Func<bool> PresenceOverride { get; set; }

        internal WmiTemperatureSource(string provider)
            : this(provider,delegate { return ProcessExists(provider); },delegate { return WmiTemperatureQuery.Read(provider); },Stopwatch.GetTimestamp) { }
        internal WmiTemperatureSource(string provider,Func<bool> present,Func<IList<SensorReading>> query,Func<long> clock)
        {
            if(provider!="LibreHardwareMonitor"&&provider!="OpenHardwareMonitor")throw new ArgumentException("Unknown temperature provider","provider");
            if(present==null)throw new ArgumentNullException("present");
            if(query==null)throw new ArgumentNullException("query");
            if(clock==null)throw new ArgumentNullException("clock");
            _provider=provider;_present=present;_query=query;_clock=clock;
            SetFailure(MetricStatus.Unsupported,"not sampled");
        }
        private static bool ProcessExists(string provider)
        {
            var processes=Process.GetProcessesByName(provider);
            try { return processes.Length>0; }
            finally { foreach(var process in processes)process.Dispose(); }
        }
        internal bool ProviderExists()
        {
            if(_disposed)return false;
            long now=_clock();if(now<_gateUntil)return true;
            try
            {
                var check=PresenceOverride??_present;bool present=check();
                if(present)_gateUntil=now+30*Stopwatch.Frequency;
                return present;
            }
            catch(Exception) { return true; } // Broken discovery allows a real query, but is never cached as evidence.
        }
        public void Sample(out MetricValue cpu,out MetricValue gpu)
        {
            if(_disposed){cpu=gpu=MetricValue.Missing(MetricStatus.Disabled,_provider+" WMI disabled");return;}
            long now=_clock();
            if(now<_retryAt){cpu=_cpuFailure;gpu=_gpuFailure;return;}
            if(!ProviderExists())
            {
                SetFailure(MetricStatus.Unsupported,"not detected (its WMI namespace requires the provider running under the default process name)");
                _retryAt=now+30*Stopwatch.Frequency;cpu=_cpuFailure;gpu=_gpuFailure;return;
            }
            try
            {
                Attempts++;
                var candidates=_query();if(candidates==null)throw new InvalidOperationException("No sensor collection returned");
                var selected=TemperatureSelection.Select(candidates);
                cpu=ConvertReading(TemperatureSelection.Highest(selected,true),true);
                gpu=ConvertReading(TemperatureSelection.Highest(selected,false),false);
                if(cpu.Status!=MetricStatus.Available&&gpu.Status!=MetricStatus.Available)
                { _cpuFailure=cpu;_gpuFailure=gpu;_retryAt=_clock()+30*Stopwatch.Frequency; }
                else _retryAt=0;
            }
            catch(UnauthorizedAccessException error) { Failed(MetricStatus.AccessDenied,error.Message,out cpu,out gpu); }
            catch(NotSupportedException error) { Failed(MetricStatus.Unsupported,error.Message,out cpu,out gpu); }
            catch(Exception error) { Failed(MetricStatus.Error,error.Message,out cpu,out gpu); }
        }
        private MetricValue ConvertReading(SensorReading reading,bool cpu)
        {
            if(reading==null)return MetricValue.Missing(MetricStatus.Unsupported,_provider+" WMI: no identified "+(cpu?"CPU":"GPU core")+" temperature sensor");
            return MetricValue.Available(reading.Celsius.Value,_provider+" WMI / "+reading.DeviceName+" / "+reading.Measurement+" / "+reading.SensorName);
        }
        private void SetFailure(MetricStatus status,string detail)
        { _cpuFailure=_gpuFailure=MetricValue.Missing(status,_provider+" WMI: "+detail); }
        private void Failed(MetricStatus status,string detail,out MetricValue cpu,out MetricValue gpu)
        { SetFailure(status,detail);_retryAt=_clock()+30*Stopwatch.Frequency;cpu=_cpuFailure;gpu=_gpuFailure; }
        public void Suspend()
        { _gateUntil=0;_retryAt=0;SetFailure(MetricStatus.Unsupported,"not sampled"); }
        public void Dispose()
        { if(_disposed)return;_disposed=true;Suspend(); }
    }
}
