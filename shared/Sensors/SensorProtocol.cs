using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinMemoryCleaner.Sensors
{
    internal sealed class SensorMessage
    {
        private readonly List<SensorReading> _readings=new List<SensorReading>();
        internal string Kind { get; set; }
        internal string Session { get; set; }
        internal long Sequence { get; set; }
        internal DateTime SampleUtc { get; set; }
        internal int RefreshSeconds { get; set; }
        internal bool CpuEnabled { get; set; }
        internal string CpuStatus { get; set; }
        internal string GpuStatus { get; set; }
        internal string Detail { get; set; }
        internal IList<SensorReading> Readings { get { return _readings; } }
    }
    internal static class SensorProtocol
    {
        internal const int Version=1;
        internal const string PayloadVersion="lhm-0.9.6-p1";
        internal const int MaximumFrameBytes=65536;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        internal static int NormalizeRefresh(int seconds) { return seconds==1||seconds==5?seconds:2; }
        internal static int MaximumAge(int seconds) { return Math.Max(10,3*NormalizeRefresh(seconds)); }
        private static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength=MaximumFrameBytes,RecursionLimit=16 }; }
        internal static string Encode(SensorMessage message)
        {
            if(message==null)throw new ArgumentNullException("message");
            var data=new Dictionary<string,object>(StringComparer.Ordinal) { {"kind",message.Kind},{"protocol",Version},{"payload",PayloadVersion},{"session",message.Session} };
            if(message.Kind=="configure")
            { data.Add("refresh",message.RefreshSeconds);data.Add("cpuEnabled",message.CpuEnabled); }
            else if(message.Kind=="snapshot")
            {
                if(message.Readings.Count>64)throw new InvalidDataException("Too many sensor devices.");
                data.Add("sequence",message.Sequence);data.Add("utc",message.SampleUtc.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture));
                data.Add("cpuStatus",message.CpuStatus);data.Add("gpuStatus",message.GpuStatus);data.Add("detail",message.Detail??string.Empty);
                var readings=new List<object>();
                foreach(var reading in TemperatureSelection.Select(message.Readings))
                    readings.Add(new Dictionary<string,object>(StringComparer.Ordinal) { {"hardwareId",reading.HardwareId},{"hardwareType",reading.HardwareType},
                        {"device",reading.DeviceName},{"sensorId",reading.SensorId},{"sensor",reading.SensorName},{"sensorType",reading.SensorType},
                        {"measurement",reading.Measurement},{"celsius",reading.Celsius} });
                data.Add("readings",readings);
            }
            else if(message.Kind!="hello"&&message.Kind!="stop")throw new InvalidDataException("Unknown sensor command.");
            string line;
            try { line=Serializer().Serialize(data); }
            catch(InvalidOperationException ex) { throw new InvalidDataException("Sensor frame cannot be encoded.",ex); }
            Decode(line); // Apply exactly the receiver's structural limits to our output.
            return line;
        }
        private static object Required(IDictionary<string,object> data,string key)
        { object value;if(!data.TryGetValue(key,out value))throw new InvalidDataException("Missing sensor field: "+key);return value; }
        private static string Text(IDictionary<string,object> data,string key,int maximum,bool allowEmpty)
        {
            var value=Required(data,key) as string;
            if(value==null||value.Length>maximum||value.IndexOf('\0')>=0||(!allowEmpty&&value.Length==0))throw new InvalidDataException("Invalid sensor text: "+key);
            return value;
        }
        private static long Integer(IDictionary<string,object> data,string key)
        {
            object value=Required(data,key);
            if(!(value is int)&&!(value is long))throw new InvalidDataException("Invalid sensor integer: "+key);
            return Convert.ToInt64(value,CultureInfo.InvariantCulture);
        }
        private static void Fields(IDictionary<string,object> data,params string[] expected)
        {
            if(data.Count!=expected.Length)throw new InvalidDataException("Unexpected sensor fields.");
            foreach(string field in expected)if(!data.ContainsKey(field))throw new InvalidDataException("Missing sensor field: "+field);
        }
        private static string Status(IDictionary<string,object> data,string key)
        {
            string value=Text(data,key,32,false);
            if(value!="Available"&&value!="WarmingUp"&&value!="Unsupported"&&value!="AccessDenied"&&value!="Error"&&value!="Disabled")throw new InvalidDataException("Invalid sensor status.");
            return value;
        }
        private static SensorReading Reading(object item)
        {
            var data=item as Dictionary<string,object>;
            if(data==null)throw new InvalidDataException("Invalid sensor device structure.");
            Fields(data,"hardwareId","hardwareType","device","sensorId","sensor","sensorType","measurement","celsius");
            // Field types are structural errors; semantic defects exclude just this device.
            string hardware=Required(data,"hardwareId") as string,type=Required(data,"hardwareType") as string,device=Required(data,"device") as string;
            string sensorId=Required(data,"sensorId") as string,sensor=Required(data,"sensor") as string,sensorType=Required(data,"sensorType") as string;
            if(hardware==null||type==null||device==null||sensorId==null||sensor==null||sensorType==null||!(Required(data,"measurement") is string))throw new InvalidDataException("Invalid sensor device field type.");
            object raw=Required(data,"celsius");double? value=null;
            if(raw!=null)
            {
                if(!(raw is int)&&!(raw is long)&&!(raw is decimal)&&!(raw is double))throw new InvalidDataException("Invalid sensor numeric field.");
                value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);
            }
            return new SensorReading { HardwareId=hardware,HardwareType=type,DeviceName=device,SensorId=sensorId,SensorName=sensor,SensorType=sensorType,Celsius=value };
        }
        private static void CheckDepth(string line)
        {
            int depth=0;bool quoted=false,escaped=false;
            foreach(char character in line)
            {
                if(quoted)
                { if(escaped)escaped=false;else if(character=='\\')escaped=true;else if(character=='"')quoted=false;continue; }
                if(character=='"')quoted=true;
                else if(character=='{'||character=='[') { if(++depth>16)throw new InvalidDataException("Sensor frame is too deep."); }
                else if(character=='}'||character==']')depth--;
            }
        }
        internal static SensorMessage Decode(string line)
        {
            if(line==null)throw new ArgumentNullException("line");
            try
            {
                if(Utf8.GetByteCount(line)>MaximumFrameBytes)throw new InvalidDataException("Sensor frame exceeds 64 KiB.");
                CheckDepth(line);
                var data=Serializer().DeserializeObject(line) as Dictionary<string,object>;
                if(data==null)throw new InvalidDataException("Sensor frame must be a JSON object.");
                if(Integer(data,"protocol")!=Version||Text(data,"payload",64,false)!=PayloadVersion)throw new InvalidDataException("Incompatible sensor protocol or payload.");
                string session=Text(data,"session",32,false);Guid parsed;
                if(session.Length!=32||!Guid.TryParseExact(session,"N",out parsed))throw new InvalidDataException("Invalid sensor session.");
                var message=new SensorMessage { Kind=Text(data,"kind",16,false),Session=session };
                if(message.Kind=="hello"||message.Kind=="stop")Fields(data,"kind","protocol","payload","session");
                else if(message.Kind=="configure")
                {
                    Fields(data,"kind","protocol","payload","session","refresh","cpuEnabled");
                    long seconds=Integer(data,"refresh");if(seconds!=1&&seconds!=2&&seconds!=5)throw new InvalidDataException("Invalid sensor refresh interval.");
                    message.RefreshSeconds=(int)seconds;object enabled=Required(data,"cpuEnabled");if(!(enabled is bool))throw new InvalidDataException("Invalid CPU configuration.");message.CpuEnabled=(bool)enabled;
                }
                else if(message.Kind=="snapshot")
                {
                    Fields(data,"kind","protocol","payload","session","sequence","utc","cpuStatus","gpuStatus","detail","readings");
                    message.Sequence=Integer(data,"sequence");if(message.Sequence<0)throw new InvalidDataException("Invalid sensor sequence.");
                    DateTime timestamp;if(!DateTime.TryParseExact(Text(data,"utc",64,false),"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out timestamp)||timestamp.Kind!=DateTimeKind.Utc)throw new InvalidDataException("Invalid sensor UTC time.");
                    message.SampleUtc=timestamp;message.CpuStatus=Status(data,"cpuStatus");message.GpuStatus=Status(data,"gpuStatus");message.Detail=Text(data,"detail",1024,true);
                    var items=Required(data,"readings") as object[];if(items==null||items.Length>64)throw new InvalidDataException("Invalid sensor device list.");
                    var candidates=new List<SensorReading>();foreach(var item in items)candidates.Add(Reading(item));
                    foreach(var reading in TemperatureSelection.Select(candidates))message.Readings.Add(reading);
                }
                else throw new InvalidDataException("Unknown sensor message.");
                return message;
            }
            catch(ArgumentException ex) { throw new InvalidDataException("Malformed sensor JSON or UTF-8.",ex); }
            catch(InvalidOperationException ex) { throw new InvalidDataException("Malformed sensor JSON.",ex); }
        }
    }
}
