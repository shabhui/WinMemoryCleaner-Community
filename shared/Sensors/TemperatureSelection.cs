using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinMemoryCleaner.Sensors
{
    internal static class TemperatureSelection
    {
        private static readonly Regex CcdName=new Regex(@"\ACCD[0-9]+ \(Tdie\)\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        private static readonly Regex CoreName=new Regex(@"\A(?:CPU )?Core #[0-9]+\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

        internal static bool IsCpu(string type)
        { return string.Equals(type,"Cpu",StringComparison.OrdinalIgnoreCase); }
        internal static bool IsGpu(string type)
        {
            return string.Equals(type,"GpuNvidia",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(type,"GpuAmd",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(type,"GpuIntel",StringComparison.OrdinalIgnoreCase);
        }
        internal static bool ValidValue(double? value)
        { return value.HasValue&&!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)&&value.Value>=1&&value.Value<=150; }
        internal static bool ValidText(string value,int maximum)
        { return !string.IsNullOrEmpty(value)&&value.Length<=maximum&&value.IndexOf('\0')<0; }
        private static bool Same(string value,string expected)
        { return string.Equals(value,expected,StringComparison.OrdinalIgnoreCase); }
        private static int Priority(SensorReading reading,out string measurement)
        {
            measurement=null;
            if(reading==null||!ValidValue(reading.Celsius)||!ValidText(reading.HardwareId,512)
                ||!ValidText(reading.SensorId,512)||!ValidText(reading.DeviceName,256)||!ValidText(reading.SensorName,256)
                ||!Same(reading.SensorType,"Temperature"))return 0;
            string name=reading.SensorName;
            if(IsGpu(reading.HardwareType))
            {
                if(Same(name,"GPU Core")||Same(name,"GPU Temperature")){measurement="GPU core";return 1;}
                return 0;
            }
            if(!IsCpu(reading.HardwareType))return 0;
            if(Same(name,"CPU Package")){measurement="Package";return 1;}
            if(Same(name,"Core (Tdie)")||Same(name,"CPU (Tdie)")){measurement="Tdie";return 2;}
            if(Same(name,"Core (Tctl/Tdie)")||Same(name,"CPU (Tctl/Tdie)")){measurement="Tctl/Tdie";return 3;}
            if(Same(name,"CPU Die (average)")){measurement="Die average";return 4;}
            if(Same(name,"CCDs Max (Tdie)")||CcdName.IsMatch(name)){measurement="CCD max (Tdie)";return 5;}
            if(Same(name,"Core Max")||CoreName.IsMatch(name)){measurement="Core max";return 6;}
            return 0;
        }
        private static bool Better(SensorReading candidate,SensorReading current)
        {
            if(current==null||candidate.Celsius.Value>current.Celsius.Value)return true;
            if(candidate.Celsius.Value<current.Celsius.Value)return false;
            int hardware=string.Compare(candidate.HardwareId,current.HardwareId,StringComparison.Ordinal);
            return hardware<0||(hardware==0&&string.Compare(candidate.SensorId,current.SensorId,StringComparison.Ordinal)<0);
        }
        internal static IList<SensorReading> Select(IEnumerable<SensorReading> candidates)
        {
            if(candidates==null)throw new ArgumentNullException("candidates");
            var best=new Dictionary<string,SensorReading>(StringComparer.Ordinal);
            foreach(var candidate in candidates)
            {
                string measurement;int priority=Priority(candidate,out measurement);
                if(priority==0)continue;
                SensorReading current;
                if(best.TryGetValue(candidate.HardwareId,out current))
                {
                    if(!Same(current.HardwareType,candidate.HardwareType))continue;
                    string ignored;int previous=Priority(current,out ignored);
                    if(priority>previous||(priority==previous&&!Better(candidate,current)))continue;
                }
                else if(best.Count>=64)continue;
                best[candidate.HardwareId]=new SensorReading { HardwareId=candidate.HardwareId,HardwareType=candidate.HardwareType,
                    DeviceName=candidate.DeviceName,SensorId=candidate.SensorId,SensorName=candidate.SensorName,
                    SensorType="Temperature",Celsius=candidate.Celsius,Measurement=measurement };
            }
            var result=new List<SensorReading>(best.Values);
            result.Sort(delegate(SensorReading left,SensorReading right) { return string.Compare(left.HardwareId,right.HardwareId,StringComparison.Ordinal); });
            return result;
        }
        internal static SensorReading Highest(IEnumerable<SensorReading> readings,bool cpu)
        {
            if(readings==null)throw new ArgumentNullException("readings");
            SensorReading best=null;
            foreach(var reading in readings)
            {
                string measurement;
                if(Priority(reading,out measurement)==0||(cpu?!IsCpu(reading.HardwareType):!IsGpu(reading.HardwareType)))continue;
                if(Better(reading,best))best=reading;
            }
            return best;
        }
    }
}
