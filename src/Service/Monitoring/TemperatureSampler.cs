using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    internal sealed partial class TemperatureSampler : IDisposable
    {
        private AsusCpuTemperatureSource _asus;
        private IntPtr _module;
        private QueryInterface _query;
        private Thermal _thermal;
        private GpuName _gpuName;
        private Unload _unload;
        private readonly List<IntPtr> _handles=new List<IntPtr>();
        private DateTime _nvRetry;
        private volatile bool _disposed;
        private MetricValue _nvFailure=MetricValue.Missing(MetricStatus.Unsupported,"NVIDIA driver thermal API not available");
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
                var pointer=MonitoringNativeMethods.GetProcAddress(_module,Encoding.ASCII.GetBytes("nvapi_QueryInterface"+'\0'));
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
        private void CloseNvidia()
        {
            if(_unload!=null){try{_unload();}catch(Exception){}}
            _unload=null;_thermal=null;_query=null;_gpuName=null;_handles.Clear();
            if(_module!=IntPtr.Zero){MonitoringNativeMethods.FreeLibrary(_module);_module=IntPtr.Zero;}
        }
        ~TemperatureSampler(){Dispose(false);}
        public void Dispose(){Dispose(true);GC.SuppressFinalize(this);}
        private void Dispose(bool disposing)
        {
            if(_disposed)return;
            _disposed=true;
            if(disposing)DisposeSources();
            CloseNvidia();
        }
    }
}
#pragma warning restore 1591
