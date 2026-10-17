using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    internal sealed class GpuSample
    {
        internal MetricValue Usage;
        internal string Name;
        internal readonly Dictionary<int,double> ProcessUsage=new Dictionary<int,double>();
        internal readonly Dictionary<int,long> ProcessMemory=new Dictionary<int,long>();
    }
    internal sealed class GpuSampler : IDisposable
    {
        private IntPtr _query,_engine,_memory;
        private readonly List<ulong> _luids=new List<ulong>();
        private string _name="No identified physical adapter";
        private DateTime _retry;
        private MetricValue _failure=MetricValue.Missing(MetricStatus.Unsupported,"Windows GPU performance counters unavailable");
        private bool _first;
        [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhOpenQuery(string source,IntPtr data,out IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhAddEnglishCounterW")]private static extern uint AddCounter(IntPtr query,string path,IntPtr data,out IntPtr counter);
        [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll",CharSet=CharSet.Unicode,EntryPoint="PdhGetFormattedCounterArrayW")]private static extern uint CounterArray(IntPtr counter,uint format,ref uint bytes,out uint count,IntPtr items);
        [StructLayout(LayoutKind.Explicit,Size=16)]private struct CounterValue
        {
            [FieldOffset(0)]internal uint Status;
            [FieldOffset(8)]internal double Double;
            [FieldOffset(8)]internal long Large;
        }
        [StructLayout(LayoutKind.Sequential)]private struct CounterItem{internal IntPtr Name;internal CounterValue Value;}
        [DllImport("dxgi.dll")]private static extern int CreateDXGIFactory1(ref Guid id,out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int EnumAdapter(IntPtr factory,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetDescription(IntPtr adapter,out AdapterDescription description);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string Name;
            internal uint Vendor,Device,SubSystem,Revision;
            internal UIntPtr VideoMemory,SystemMemory,SharedMemory;
            internal uint LuidLow;internal int LuidHigh;internal uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)]private struct OpenAdapter{internal uint Low;internal int High;internal uint Handle;}
        [StructLayout(LayoutKind.Sequential)]private struct AdapterQuery{internal uint Handle,Type;internal IntPtr Data;internal uint Bytes;}
        [DllImport("gdi32.dll")]private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
        [DllImport("gdi32.dll")]private static extern int D3DKMTQueryAdapterInfo(ref AdapterQuery query);
        [DllImport("gdi32.dll")]private static extern int D3DKMTCloseAdapter(ref uint adapter);
        private static uint? ReadAdapterType(AdapterDescription description)
        {
            var adapter=new OpenAdapter{Low=description.LuidLow,High=description.LuidHigh};
            try
            {
                if(D3DKMTOpenAdapterFromLuid(ref adapter)!=0)return null;
                try
                {
                    var data=Marshal.AllocHGlobal(4);
                    try
                    {
                        Marshal.WriteInt32(data,0);
                        var query=new AdapterQuery{Handle=adapter.Handle,Type=15,Data=data,Bytes=4};
                        return D3DKMTQueryAdapterInfo(ref query)==0?(uint?)unchecked((uint)Marshal.ReadInt32(data)):null;
                    }
                    finally{Marshal.FreeHGlobal(data);}
                }
                finally{int closeResult=D3DKMTCloseAdapter(ref adapter.Handle);}
            }
            catch(EntryPointNotFoundException){return null;}
            catch(DllNotFoundException){return null;}
        }
        private static Delegate Method(IntPtr instance,int index,Type type)
        {
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),index*IntPtr.Size),type);
        }
        private void IdentifyAdapters()
        {
            _luids.Clear();var names=new List<string>();IntPtr factory=IntPtr.Zero;
            try
            {
                var iid=new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if(CreateDXGIFactory1(ref iid,out factory)!=0)return;
                var enumerate=(EnumAdapter)Method(factory,12,typeof(EnumAdapter));
                for(uint index=0;index<32;index++)
                {
                    IntPtr adapter;if(enumerate(factory,index,out adapter)!=0)break;
                    try
                    {
                        AdapterDescription description;
                        if(((GetDescription)Method(adapter,10,typeof(GetDescription)))(adapter,out description)!=0)continue;
                        string name=description.Name??string.Empty;
                        if(!ResourceCalculation.IsPhysicalGpu(description.Vendor,description.Flags,ReadAdapterType(description),name))continue;
                        ulong luid=((ulong)(uint)description.LuidHigh<<32)|description.LuidLow;
                        if(!_luids.Contains(luid)){_luids.Add(luid);names.Add(name);}
                    }
                    finally{Marshal.Release(adapter);}
                }
            }
            catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
            finally{if(factory!=IntPtr.Zero)Marshal.Release(factory);}
            _name=names.Count==0?"No identified physical adapter":string.Join(" / ",names.ToArray());
        }
        private bool Open()
        {
            if(_query!=IntPtr.Zero)return true;
            if(DateTime.UtcNow<_retry)return false;
            _retry=DateTime.UtcNow.AddSeconds(30);
            try
            {
                IdentifyAdapters();
                if(_luids.Count==0){_failure=MetricValue.Missing(MetricStatus.Unsupported,"DXGI / D3DKMT: no verified physical render adapter");return false;}
                uint status=PdhOpenQuery(null,IntPtr.Zero,out _query);
                if(status==0)status=AddCounter(_query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out _engine);
                if(status!=0){Fail(status);return false;}
                // A failed VRAM counter does not invalidate supported utilization counters.
                if(AddCounter(_query,@"\GPU Process Memory(*)\Dedicated Usage",IntPtr.Zero,out _memory)!=0)_memory=IntPtr.Zero;
                _retry=DateTime.MinValue;_first=true;return true;
            }
            catch(EntryPointNotFoundException){_failure=MetricValue.Missing(MetricStatus.Unsupported,"GPU counters require supported Windows / WDDM driver");Suspend();return false;}
            catch(DllNotFoundException){_failure=MetricValue.Missing(MetricStatus.Unsupported,"Windows GPU counter provider is absent");Suspend();return false;}
        }
        private void Fail(uint status)
        {
            _failure=MetricValue.Missing(status==5?MetricStatus.AccessDenied:MetricStatus.Unsupported,"Windows PDH GPU counter status 0x"+status.ToString("X8",CultureInfo.InvariantCulture));
            CloseQuery();_retry=DateTime.UtcNow.AddSeconds(30);
        }
        private static uint ReadArray(IntPtr counter,uint format,Action<string,CounterValue> read)
        {
            uint bytes=0,count;uint status=CounterArray(counter,format,ref bytes,out count,IntPtr.Zero);
            if(status!=0&&status!=0x800007D2)return status;
            if(bytes==0)return 0;
            if(bytes>33554432)return 0xC0000BC2;
            var buffer=Marshal.AllocHGlobal((int)bytes);
            try
            {
                status=CounterArray(counter,format,ref bytes,out count,buffer);
                if(status!=0)return status;
                int size=Marshal.SizeOf(typeof(CounterItem));
                if(count>bytes/size)return 0xC0000BC2;
                for(int i=0;i<count;i++)
                {
                    var item=(CounterItem)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64()+i*size),typeof(CounterItem));
                    if((item.Value.Status==0||item.Value.Status==1)&&item.Name!=IntPtr.Zero)read(Marshal.PtrToStringUni(item.Name),item.Value);
                }
                return 0;
            }
            finally{Marshal.FreeHGlobal(buffer);}
        }
        internal GpuSample Sample(bool includeProcesses)
        {
            var result=new GpuSample();
            if(!Open()){result.Name=_name;result.Usage=_failure;return result;}
            result.Name=_name;
            uint status=PdhCollectQueryData(_query);
            if(status!=0){Fail(status);result.Usage=_failure;return result;}
            if(_first){_first=false;result.Usage=MetricValue.Missing(MetricStatus.WarmingUp,"Windows PDH: two GPU samples required");return result;}
            var aggregate=new GpuAggregation(_luids);
            status=ReadArray(_engine,0x200|0x8000,delegate(string name,CounterValue value){aggregate.AddEngine(name,value.Double);});
            if(status!=0){Fail(status);result.Usage=_failure;return result;}
            result.Usage=MetricValue.Available(aggregate.Usage,"Windows PDH / busiest physical GPU engine");
            if(includeProcesses)
            {
                foreach(var pair in aggregate.ProcessUsage)result.ProcessUsage.Add(pair.Key,pair.Value);
                if(_memory!=IntPtr.Zero&&ReadArray(_memory,0x400,delegate(string name,CounterValue value){aggregate.AddMemory(name,value.Large);})==0)
                    foreach(var pair in aggregate.ProcessMemory)result.ProcessMemory.Add(pair.Key,pair.Value);
            }
            return result;
        }
        private void CloseQuery(){if(_query!=IntPtr.Zero){uint closeResult=PdhCloseQuery(_query);}_query=_engine=_memory=IntPtr.Zero;}
        internal void Suspend(){CloseQuery();_luids.Clear();}
        public void Dispose(){Suspend();}
    }
}
#pragma warning restore 1591
