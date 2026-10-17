using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    internal sealed class TemperatureSampler : IDisposable
    {
        private AsusCpuTemperatureSource _asus;
        private IntPtr _module;
        private QueryInterface _query;
        private Thermal _thermal;
        private GpuName _gpuName;
        private Unload _unload;
        private readonly List<IntPtr> _handles=new List<IntPtr>();
        private DateTime _nvRetry,_wmiRetry,_gateUntil;
        private bool _disposed;
        /// <summary>Test seam. Null = real check.</summary>
        internal Func<bool> ProviderRunning { get; set; }
        /// <summary>Number of real LibreHardwareMonitor WMI attempts in this process.</summary>
        internal int WmiAttempts;
        /// <summary>True when the LibreHardwareMonitor WMI provider is likely present. Positive results are trusted for 30 seconds; a broken check fails open.</summary>
        internal bool ProviderExists()
        {
            if(DateTime.UtcNow<_gateUntil)return true;
            try
            {
                bool present;
                var injected=ProviderRunning;
                if(injected!=null)present=injected();
                else
                {
                    var running=Process.GetProcessesByName("LibreHardwareMonitor");
                    try{ present=running.Length>0; }
                    finally{ foreach(var process in running)process.Dispose(); }
                }
                if(present)_gateUntil=DateTime.UtcNow.AddSeconds(30);
                return present;
            }
            catch
            {
                return true; // Fail open: a broken check must not hide real sensors.
            }
        }
        private MetricValue _nvFailure=MetricValue.Missing(MetricStatus.Unsupported,"NVIDIA driver thermal API not available");
        private MetricValue _cpuFailure=MetricValue.Missing(MetricStatus.Unsupported,"LibreHardwareMonitor WMI: no installed, identified CPU temperature sensor");
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate IntPtr QueryInterface(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Initialize();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Unload();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Enumerate([Out,MarshalAs(UnmanagedType.LPArray,SizeConst=64)]IntPtr[] handles,out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int Thermal(IntPtr handle,uint sensor,ref ThermalSettings settings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)]private delegate int GpuName(IntPtr handle,StringBuilder name);
        [StructLayout(LayoutKind.Sequential)]private struct Sensor{internal int Controller;internal uint Min,Max,Current;internal int Target;}
        [StructLayout(LayoutKind.Sequential)]private struct ThermalSettings
        {
            internal uint Version,Count;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]internal Sensor[] Sensors;
        }
        private Delegate Get(uint id,Type type)
        {
            var function=_query(id);return function==IntPtr.Zero?null:Marshal.GetDelegateForFunctionPointer(function,type);
        }
        private bool OpenNvidia()
        {
            if(_thermal!=null)return true;
            if(DateTime.UtcNow<_nvRetry)return false;
            _nvRetry=DateTime.UtcNow.AddSeconds(30);
            try
            {
                _module=MonitoringNativeMethods.LoadLibrary(Path.Combine(Environment.SystemDirectory,IntPtr.Size==8?"nvapi64.dll":"nvapi.dll"));
                if(_module==IntPtr.Zero)return false;
                var pointer=MonitoringNativeMethods.GetProcAddress(_module,"nvapi_QueryInterface");
                if(pointer==IntPtr.Zero){CloseNvidia();return false;}
                _query=(QueryInterface)Marshal.GetDelegateForFunctionPointer(pointer,typeof(QueryInterface));
                var initialize=(Initialize)Get(0x0150E828,typeof(Initialize));
                var enumerate=(Enumerate)Get(0xE5AC921F,typeof(Enumerate));
                _thermal=(Thermal)Get(0xE3640A56,typeof(Thermal));
                _gpuName=(GpuName)Get(0xCEEE8E9F,typeof(GpuName));
                _unload=(Unload)Get(0xD22BDD7E,typeof(Unload));
                if(initialize==null||enumerate==null||_thermal==null||initialize()!=0){CloseNvidia();return false;}
                var handles=new IntPtr[64];int count;
                if(enumerate(handles,out count)!=0||count<1||count>64){CloseNvidia();return false;}
                for(int i=0;i<count;i++)_handles.Add(handles[i]);return true;
            }
            catch(Exception ex){_nvFailure=MetricValue.Missing(MetricStatus.Error,"NVIDIA thermal API: "+ex.Message);CloseNvidia();return false;}
        }
        private MetricValue NvidiaTemperature()
        {
            if(!OpenNvidia())return _nvFailure;
            double? highest=null;string source=null;int lastStatus=0;
            foreach(var handle in _handles)
            {
                var settings=new ThermalSettings{Version=(uint)Marshal.SizeOf(typeof(ThermalSettings))|0x10000,Count=3,Sensors=new Sensor[3]};
                lastStatus=_thermal(handle,15,ref settings);
                if(lastStatus!=0||settings.Count>3)continue;
                for(int i=0;i<settings.Count;i++)
                {
                    var sensor=settings.Sensors[i];
                    if(sensor.Target!=1||sensor.Current<1||sensor.Current>150)continue;
                    if(!highest.HasValue||sensor.Current>highest.Value)
                    {
                        highest=sensor.Current;var name=new StringBuilder(64);
                        source="NVIDIA NVAPI";if(_gpuName!=null&&_gpuName(handle,name)==0)source+=" / "+name;
                    }
                }
            }
            if(highest.HasValue)return MetricValue.Available(highest.Value,source);
            _nvFailure=MetricValue.Missing(MetricStatus.Unsupported,"NVIDIA driver did not expose a GPU thermal sensor ("+lastStatus+")");
            CloseNvidia();_nvRetry=DateTime.UtcNow.AddSeconds(30);return _nvFailure;
        }
        private void LibreTemperatures(out MetricValue cpu,out MetricValue gpu)
        {
            cpu=_cpuFailure;gpu=null;
            if(DateTime.UtcNow<_wmiRetry)return;
            if(!ProviderExists())
            {
                // root\LibreHardwareMonitor is hosted by a running LibreHardwareMonitor process.
                // Without one the namespace cannot exist, so the managed WMI stack stays unloaded.
                _wmiRetry=DateTime.UtcNow.AddSeconds(30);
                cpu=_cpuFailure=MetricValue.Missing(MetricStatus.Unsupported,"LibreHardwareMonitor not detected (its WMI namespace requires a LibreHardwareMonitor process running under the default name)");
                return;
            }
            WmiAttempts++;
            try
            {
                var options=new EnumerationOptions{ReturnImmediately=true,Timeout=TimeSpan.FromSeconds(2)};
                var hardware=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                using(var search=new ManagementObjectSearcher(new ManagementScope(@"\\.\root\LibreHardwareMonitor"),new ObjectQuery("SELECT Identifier,HardwareType,Name FROM Hardware"),options))
                using(var values=search.Get())
                {
                    foreach(ManagementObject value in values)
                    using(value)
                    {
                        string type=Convert.ToString(value["HardwareType"],CultureInfo.InvariantCulture);
                        if(type=="Cpu"||type.StartsWith("Gpu",StringComparison.OrdinalIgnoreCase))hardware[Convert.ToString(value["Identifier"],CultureInfo.InvariantCulture)]=type+" / "+Convert.ToString(value["Name"],CultureInfo.InvariantCulture);
                    }
                }
                double? cpuValue=null,gpuValue=null;string cpuSource=null,gpuSource=null;
                using(var search=new ManagementObjectSearcher(new ManagementScope(@"\\.\root\LibreHardwareMonitor"),new ObjectQuery("SELECT Parent,Name,Value FROM Sensor WHERE SensorType='Temperature'"),options))
                using(var values=search.Get())
                {
                    foreach(ManagementObject value in values)
                    using(value)
                    {
                        string owner;if(!hardware.TryGetValue(Convert.ToString(value["Parent"],CultureInfo.InvariantCulture),out owner))continue;
                        if(value["Value"]==null)continue;
                        double reading=Convert.ToDouble(value["Value"],CultureInfo.InvariantCulture);
                        if(double.IsNaN(reading)||double.IsInfinity(reading)||reading<1||reading>150)continue;
                        string source="LibreHardwareMonitor WMI / "+owner+" / "+Convert.ToString(value["Name"],CultureInfo.InvariantCulture);
                        if(owner.StartsWith("Cpu /",StringComparison.Ordinal)&&(!cpuValue.HasValue||reading>cpuValue.Value)){cpuValue=reading;cpuSource=source;}
                        else if(owner.StartsWith("Gpu",StringComparison.Ordinal)&&(!gpuValue.HasValue||reading>gpuValue.Value)){gpuValue=reading;gpuSource=source;}
                    }
                }
                if(cpuValue.HasValue)cpu=MetricValue.Available(cpuValue.Value,cpuSource);
                if(gpuValue.HasValue)gpu=MetricValue.Available(gpuValue.Value,gpuSource);
                if(!cpuValue.HasValue)_wmiRetry=DateTime.UtcNow.AddSeconds(30);
            }
            catch(ManagementException ex)
            {
                _cpuFailure=MetricValue.Missing(ex.ErrorCode==ManagementStatus.AccessDenied?MetricStatus.AccessDenied:MetricStatus.Unsupported,"LibreHardwareMonitor WMI: "+ex.ErrorCode);
                cpu=_cpuFailure;_wmiRetry=DateTime.UtcNow.AddSeconds(30);
            }
            catch(UnauthorizedAccessException)
            {
                cpu=_cpuFailure=MetricValue.Missing(MetricStatus.AccessDenied,"LibreHardwareMonitor WMI access denied");_wmiRetry=DateTime.UtcNow.AddSeconds(30);
            }
            catch(Exception ex)
            {
                cpu=_cpuFailure=MetricValue.Missing(MetricStatus.Error,"LibreHardwareMonitor WMI: "+ex.Message);_wmiRetry=DateTime.UtcNow.AddSeconds(30);
            }
        }
        internal void Sample(out MetricValue cpu,out MetricValue gpu)
        {
            if(_asus==null)_asus=new AsusCpuTemperatureSource();
            cpu=_asus.Sample(); gpu=NvidiaTemperature();
            if(cpu.Status==MetricStatus.Available&&gpu.Status==MetricStatus.Available)return;
            MetricValue libreCpu,libreGpu;LibreTemperatures(out libreCpu,out libreGpu);
            if(cpu.Status!=MetricStatus.Available)
            {
                if(libreCpu.Status==MetricStatus.Available)cpu=libreCpu;
                else
                {
                    var status=cpu.Status==MetricStatus.AccessDenied||cpu.Status==MetricStatus.Error?cpu.Status:libreCpu.Status;
                    cpu=MetricValue.Missing(status,cpu.Detail+"; "+libreCpu.Detail);
                }
            }
            if(gpu.Status!=MetricStatus.Available&&libreGpu!=null)gpu=libreGpu;
        }
        private void CloseNvidia()
        {
            if(_unload!=null){try{_unload();}catch(Exception){}}
            _unload=null;_thermal=null;_query=null;_gpuName=null;_handles.Clear();
            if(_module!=IntPtr.Zero){MonitoringNativeMethods.FreeLibrary(_module);_module=IntPtr.Zero;}
        }
        internal void Suspend(){CloseNvidia();if(_asus!=null)_asus.Suspend();}
        ~TemperatureSampler(){Dispose(false);}
        public void Dispose(){Dispose(true);GC.SuppressFinalize(this);}
        private void Dispose(bool disposing)
        {
            if(_disposed)return;
            _disposed=true;
            if(disposing){Suspend();if(_asus!=null)_asus.Dispose();}
            else CloseNvidia();
        }
    }
}
#pragma warning restore 1591
