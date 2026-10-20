using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.CompilerServices;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    // Only this query boundary references System.Management. Absent-provider gates never enter it.
    internal static class WmiTemperatureQuery
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static IList<SensorReading> Read(string provider)
        {
            try { return ReadSensors(provider); }
            catch(ManagementException error)
            {
                string detail=provider+" WMI: "+error.ErrorCode;
                if(error.ErrorCode==ManagementStatus.AccessDenied)throw new UnauthorizedAccessException(detail,error);
                throw new NotSupportedException(detail,error);
            }
        }
        private static IList<SensorReading> ReadSensors(string provider)
        {
            var options=new EnumerationOptions { ReturnImmediately=true,Timeout=TimeSpan.FromSeconds(2) };
            var scope=new ManagementScope(@"\\.\root\"+provider);
            var hardware=new Dictionary<string,SensorReading>(StringComparer.OrdinalIgnoreCase);
            using(var search=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT Identifier,HardwareType,Name FROM Hardware"),options))
            using(var values=search.Get())
            {
                foreach(ManagementObject value in values)
                using(value)
                {
                    string type=Convert.ToString(value["HardwareType"],CultureInfo.InvariantCulture);
                    // OpenHardwareMonitor's historical AMD identifier names the same GPU class.
                    if(type=="GpuAti")type="GpuAmd";
                    string id=Convert.ToString(value["Identifier"],CultureInfo.InvariantCulture);
                    string name=Convert.ToString(value["Name"],CultureInfo.InvariantCulture);
                    if((TemperatureSelection.IsCpu(type)||TemperatureSelection.IsGpu(type))
                        &&TemperatureSelection.ValidText(id,512)&&TemperatureSelection.ValidText(name,256)&&hardware.Count<64)
                        hardware[id]=new SensorReading { HardwareType=type,HardwareId=id,DeviceName=name };
                }
            }
            var readings=new List<SensorReading>();
            using(var search=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT Identifier,Parent,Name,Value FROM Sensor WHERE SensorType='Temperature'"),options))
            using(var values=search.Get())
            {
                foreach(ManagementObject value in values)
                using(value)
                {
                    if(readings.Count>=8192)break;
                    SensorReading owner;
                    if(!hardware.TryGetValue(Convert.ToString(value["Parent"],CultureInfo.InvariantCulture),out owner)||value["Value"]==null)continue;
                    readings.Add(new SensorReading { HardwareId=owner.HardwareId,HardwareType=owner.HardwareType,DeviceName=owner.DeviceName,
                        SensorId=Convert.ToString(value["Identifier"],CultureInfo.InvariantCulture),SensorName=Convert.ToString(value["Name"],CultureInfo.InvariantCulture),
                        SensorType="Temperature",Celsius=Convert.ToDouble(value["Value"],CultureInfo.InvariantCulture) });
                }
            }
            return readings;
        }
    }
}
