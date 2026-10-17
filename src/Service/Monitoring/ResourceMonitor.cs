using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    // Serialized by MonitoringPump. No timer, dispatcher or Process objects are retained here.
    public sealed class ResourceMonitor : IResourceMonitor
    {
        private sealed class ProcessTime
        {
            internal long Created, Cpu;
        }
        private long? _idle, _kernel, _user;
        private long _processTimestamp;
        private Dictionary<int,ProcessTime> _times=new Dictionary<int,ProcessTime>();
        private IntPtr _buffer;
        private int _bufferBytes;
        private bool _disposed;
        private readonly GpuSampler _gpu=new GpuSampler();
        private readonly TemperatureSampler _temperature=new TemperatureSampler();

        public ResourceSnapshot Sample(bool includeProcesses,bool includeTemperatures)
        {
            if(_disposed)throw new ObjectDisposedException("ResourceMonitor");
            var result=new ResourceSnapshot();
            long idle,kernel,user;
            if(MonitoringNativeMethods.GetSystemTimes(out idle,out kernel,out user))
            {
                var cpu=ResourceCalculation.SystemCpu(_idle,_kernel,_user,idle,kernel,user);
                result.CpuUsage=cpu.HasValue?MetricValue.Available(cpu.Value,"Windows GetSystemTimes"):MetricValue.Missing(MetricStatus.WarmingUp,"Windows GetSystemTimes: two samples required");
                _idle=idle;_kernel=kernel;_user=user;
            }
            else
            {
                result.CpuUsage=MetricValue.Missing(MetricStatus.Error,new Win32Exception(Marshal.GetLastWin32Error()).Message);
                _idle=_kernel=_user=null;
            }
            if(includeProcesses)foreach(var process in ReadProcesses())result.Processes.Add(process);
            else ClearProcesses();
            var gpu=_gpu.Sample(includeProcesses);
            result.GpuUsage=gpu.Usage; result.GpuName=gpu.Name;
            foreach(var process in result.Processes)
            {
                double usage; long memory;
                if(gpu.ProcessUsage.TryGetValue(process.Id,out usage)){process.GpuUsage=usage;process.GpuStatus=MetricStatus.Available;}
                else process.GpuStatus=gpu.Usage.Status==MetricStatus.Available?MetricStatus.Unsupported:gpu.Usage.Status;
                if(gpu.ProcessMemory.TryGetValue(process.Id,out memory))process.GpuMemoryBytes=memory;
            }
            if(includeTemperatures)
            {
                _temperature.Sample(out resultCpuTemperature,out resultGpuTemperature);
                result.CpuTemperature=resultCpuTemperature;result.GpuTemperature=resultGpuTemperature;
            }
            else
            {
                result.CpuTemperature=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling is not requested");
                result.GpuTemperature=result.CpuTemperature;
                _temperature.Suspend();
            }
            return result;
        }
        private MetricValue resultCpuTemperature,resultGpuTemperature;

        private IList<ProcessSnapshot> ReadProcesses()
        {
            int needed=0,status=0;
            if(_buffer==IntPtr.Zero)ResizeBuffer(262144);
            for(int attempt=0;attempt<6;attempt++)
            {
                status=MonitoringNativeMethods.NtQuerySystemInformation(5,_buffer,_bufferBytes,out needed);
                if(status!=unchecked((int)0xC0000004))break;
                int next=Math.Max(needed+65536,_bufferBytes*2);
                if(next>33554432)throw new InvalidOperationException("Process snapshot exceeds the bounded native buffer.");
                ResizeBuffer(next);
            }
            var rows=new List<ProcessSnapshot>();
            if(status!=0)return ReadFallback();
            int position=0,prefix=Marshal.SizeOf(typeof(MonitoringNativeMethods.ProcessInfo));
            while(position>=0&&position<=_bufferBytes-prefix)
            {
                var pointer=new IntPtr(_buffer.ToInt64()+position);
                var info=(MonitoringNativeMethods.ProcessInfo)Marshal.PtrToStructure(pointer,typeof(MonitoringNativeMethods.ProcessInfo));
                long pid=info.UniqueProcessId.ToInt64();
                if(pid>=0&&pid<=int.MaxValue)
                {
                    string name=info.ImageName.Buffer==IntPtr.Zero?(pid==0?"System Idle Process":pid==4?"System":"PID "+pid):Marshal.PtrToStringUni(info.ImageName.Buffer,info.ImageName.Length/2);
                    rows.Add(new ProcessSnapshot{Id=(int)pid,Name=name,CreationTime=info.CreateTime,WorkingSetBytes=Size(info.WorkingSetSize),PrivateBytes=Size(info.PrivatePageCount),CpuStatus=MetricStatus.WarmingUp,Detail="Windows native process snapshot"});
                    ApplyCpu(rows[rows.Count-1],info.KernelTime+info.UserTime);
                }
                if(info.NextEntryOffset==0)break;
                if(info.NextEntryOffset<prefix||info.NextEntryOffset>_bufferBytes-position)throw new InvalidOperationException("Invalid process snapshot offset.");
                position+=(int)info.NextEntryOffset;
            }
            FinishProcessTimes(rows);
            return rows;
        }
        private readonly Dictionary<int,long> _newCpu=new Dictionary<int,long>();
        private void ApplyCpu(ProcessSnapshot row,long cpu)
        {
            ProcessTime previous;
            double elapsed=_processTimestamp==0?0:(Stopwatch.GetTimestamp()-_processTimestamp)/(double)Stopwatch.Frequency;
            long? old=_times.TryGetValue(row.Id,out previous)&&previous.Created==row.CreationTime?(long?)previous.Cpu:null;
            row.CpuUsage=ResourceCalculation.ProcessCpu(old,cpu,elapsed,Environment.ProcessorCount);
            row.CpuStatus=row.CpuUsage.HasValue?MetricStatus.Available:MetricStatus.WarmingUp;
            _newCpu[row.Id]=cpu;
        }
        private void FinishProcessTimes(IList<ProcessSnapshot> rows)
        {
            var next=new Dictionary<int,ProcessTime>(rows.Count);
            foreach(var row in rows)
            {
                long cpu;if(_newCpu.TryGetValue(row.Id,out cpu))next[row.Id]=new ProcessTime{Created=row.CreationTime,Cpu=cpu};
            }
            _times=next;_newCpu.Clear();_processTimestamp=Stopwatch.GetTimestamp();
        }
        private IList<ProcessSnapshot> ReadFallback()
        {
            var rows=new List<ProcessSnapshot>();
            foreach(var process in Process.GetProcesses())
            {
                try
                {
                    var row=new ProcessSnapshot{Id=process.Id,Name="PID "+process.Id,CpuStatus=MetricStatus.AccessDenied,Detail="Process query restricted or process exited"};
                    try
                    {
                        row.Name=process.ProcessName;row.CreationTime=process.StartTime.ToUniversalTime().ToFileTimeUtc();
                        row.WorkingSetBytes=process.WorkingSet64;row.PrivateBytes=process.PrivateMemorySize64;
                        ApplyCpu(row,process.TotalProcessorTime.Ticks);
                    }
                    catch(Win32Exception){}catch(InvalidOperationException){}
                    rows.Add(row);
                }
                finally{process.Dispose();}
            }
            FinishProcessTimes(rows);return rows;
        }
        private static long? Size(UIntPtr value)
        {
            ulong number=value.ToUInt64();return number<=long.MaxValue?(long?)number:null;
        }
        private void ResizeBuffer(int bytes)
        {
            if(_buffer!=IntPtr.Zero)Marshal.FreeHGlobal(_buffer);
            _buffer=IntPtr.Zero;_bufferBytes=0;
            _buffer=Marshal.AllocHGlobal(bytes);_bufferBytes=bytes;
        }
        private void ClearProcesses()
        {
            _times.Clear();_newCpu.Clear();_processTimestamp=0;
            if(_buffer!=IntPtr.Zero){Marshal.FreeHGlobal(_buffer);_buffer=IntPtr.Zero;_bufferBytes=0;}
        }
        public void Suspend()
        {
            _idle=_kernel=_user=null;ClearProcesses();_gpu.Suspend();_temperature.Suspend();
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;Suspend();_gpu.Dispose();_temperature.Dispose();
        }
    }
}
#pragma warning restore 1591
