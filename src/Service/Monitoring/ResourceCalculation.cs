using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public static class ResourceCalculation
    {
        public static bool IsPhysicalGpu(uint vendor,uint dxgiFlags,uint? adapterType,string name)
        {
            bool knownVendor=vendor==0x10de||vendor==0x8086||vendor==0x1002||vendor==0x1022||vendor==0x5143;
            if(!knownVendor||(dxgiFlags&3)!=0||!adapterType.HasValue||string.IsNullOrEmpty(name))return false;
            // D3DKMT_ADAPTERTYPE: render supported; not software, indirect-display or paravirtualized.
            uint type=adapterType.Value;
            return (type&1)!=0&&(type&(4|64|128))==0&&name.IndexOf("virtual",StringComparison.OrdinalIgnoreCase)<0&&name.IndexOf("remote",StringComparison.OrdinalIgnoreCase)<0;
        }

        public static double? ProcessCpu(long? previous,long current,double elapsed,int processors)
        {
            if(!previous.HasValue||current<previous.Value||previous.Value<0||elapsed<=0||double.IsNaN(elapsed)||double.IsInfinity(elapsed)||processors<=0)return null;
            return Math.Min(100,(current-previous.Value)/10000000.0/elapsed/processors*100);
        }
        public static double? SystemCpu(long? oldIdle,long? oldKernel,long? oldUser,long idle,long kernel,long user)
        {
            if(!oldIdle.HasValue||!oldKernel.HasValue||!oldUser.HasValue||idle<oldIdle.Value||kernel<oldKernel.Value||user<oldUser.Value)return null;
            double total=(kernel-oldKernel.Value)+(double)(user-oldUser.Value), idleDelta=idle-oldIdle.Value;
            return total<=0||idleDelta>total?(double?)null:Math.Max(0,Math.Min(100,(total-idleDelta)/total*100));
        }
    }
    public sealed class GpuAggregation
    {
        private static readonly Regex Instance=new Regex(@"^pid_(\d+)_luid_0x([0-9a-f]+)_0x([0-9a-f]+)_phys_(\d+)(?:_eng_(\d+))?",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        private readonly HashSet<ulong> _physical;
        private readonly HashSet<string> _seenEngines=new HashSet<string>(StringComparer.OrdinalIgnoreCase), _seenMemory=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,double> _engines=new Dictionary<string,double>();
        public Dictionary<int,double> ProcessUsage { get; private set; }
        public Dictionary<int,long> ProcessMemory { get; private set; }
        public double Usage { get; private set; }
        public GpuAggregation(IEnumerable<ulong> physicalLuids)
        {
            _physical=new HashSet<ulong>(physicalLuids);ProcessUsage=new Dictionary<int,double>();ProcessMemory=new Dictionary<int,long>();
        }
        private bool Parse(string instance,out int pid,out string engine)
        {
            pid=0;engine=null;if(instance==null)return false;
            var match=Instance.Match(instance);uint high,low;
            if(!match.Success||!int.TryParse(match.Groups[1].Value,out pid)||!uint.TryParse(match.Groups[2].Value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out high)||!uint.TryParse(match.Groups[3].Value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out low))return false;
            if(!_physical.Contains(((ulong)high<<32)|low))return false;
            engine=high.ToString("X8",CultureInfo.InvariantCulture)+low.ToString("X8",CultureInfo.InvariantCulture)+"/"+match.Groups[4].Value+"/"+match.Groups[5].Value;return true;
        }
        public void AddEngine(string instance,double value)
        {
            int pid;string engine;if(double.IsNaN(value)||double.IsInfinity(value)||value<0||!Parse(instance,out pid,out engine)||!_seenEngines.Add(instance))return;
            double previous;value=Math.Min(100,value);ProcessUsage.TryGetValue(pid,out previous);ProcessUsage[pid]=Math.Max(previous,value);
            _engines.TryGetValue(engine,out previous);_engines[engine]=previous+value;Usage=Math.Max(Usage,Math.Min(100,previous+value));
        }
        public void AddMemory(string instance,long value)
        {
            int pid;string engine;if(value<0||!Parse(instance,out pid,out engine)||!_seenMemory.Add(instance))return;
            long previous;ProcessMemory.TryGetValue(pid,out previous);ProcessMemory[pid]=value>long.MaxValue-previous?long.MaxValue:previous+value;
        }
    }
}
#pragma warning restore 1591
