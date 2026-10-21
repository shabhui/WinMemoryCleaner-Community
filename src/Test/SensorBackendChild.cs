#if SENSOR_BACKEND_CHILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// A controlled test executable, never compiled into the production cleaner or payload.
internal static class SensorBackendChild
{
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    private static int Main()
    {
        using(var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true),true,4096))return Run(input);
    }
    private static int Run(TextReader input)
    {
        string mode=File.ReadAllText("mode.txt");
        var config=Json.DeserializeObject(input.ReadLine()) as Dictionary<string,object>;
        if(config==null)return 2;
        string session=(string)config["session"];
        using(var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false),4096) { AutoFlush=true })
        {
            if(mode=="exit")return 2;
            if(mode=="hang") { while(true)Thread.Sleep(100); }
            if(mode=="broken-json") { output.WriteLine("{broken");return 0; }
            if(mode=="partial-eof") { output.Write("{");return 0; }
            if(mode=="long-line") { output.Write(new string('a',65537));while(true)Thread.Sleep(100); }
            if(mode=="invalid-utf8") { using(Stream raw=Console.OpenStandardOutput())raw.Write(new byte[] { 0xC3,0x28,10 },0,3);return 0; }
            if(mode=="stderr")
            {
                using(Stream error=Console.OpenStandardError())
                { byte[] bytes=Encoding.UTF8.GetBytes(new string('x',8192));for(int i=0;i<32;i++)error.Write(bytes,0,bytes.Length);bytes=Encoding.UTF8.GetBytes("END");error.Write(bytes,0,bytes.Length); }
            }
            var hello=Base("hello",mode=="wrong-session"?"fedcba9876543210fedcba9876543210":session);
            if(mode=="wrong-version")hello["protocol"]=2;
            if(mode!="no-hello")output.WriteLine(Json.Serialize(hello));
            long sequence=0;Snapshot(output,config,sequence++,mode=="future");
            if(mode=="one-frame") { while(true)Thread.Sleep(100); }
            if(mode=="duplicate") { while(true) { Thread.Sleep(50);Snapshot(output,config,0,false); } }
            string line;
            while((line=input.ReadLine())!=null)
            {
                config=Json.DeserializeObject(line) as Dictionary<string,object>;
                if((string)config["kind"]=="stop")
                {
                    if((string)config["session"]!=session)return 3;
                    if(mode.StartsWith("stop-marker:",StringComparison.Ordinal))File.WriteAllText(mode.Substring(12),"session-stop");
                    return 0;
                }
                Snapshot(output,config,sequence++,false);
            }
        }
        return 0;
    }
    private static Dictionary<string,object> Base(string kind,string session)
    { return new Dictionary<string,object> { {"kind",kind},{"protocol",1},{"payload","lhm-0.9.6-p1"},{"session",session} }; }
    private static void Snapshot(TextWriter output,Dictionary<string,object> config,long sequence,bool future)
    {
        var frame=Base("snapshot",(string)config["session"]);
        frame.Add("sequence",sequence);frame.Add("utc",DateTime.UtcNow.AddSeconds(future?6:0).ToString("o",CultureInfo.InvariantCulture));
        frame.Add("cpuStatus","Unsupported");frame.Add("gpuStatus","Available");frame.Add("detail","Controlled backend");
        frame.Add("readings",new[] { new Dictionary<string,object> { {"hardwareId","/gpu/0"},{"hardwareType","GpuNvidia"},{"device","Controlled GPU"},
            {"sensorId","/gpu/0/temperature/0"},{"sensor","GPU Core"},{"sensorType","Temperature"},{"measurement","GpuCore"},{"celsius",40+(int)config["refresh"]} } });
        output.WriteLine(Json.Serialize(frame));
    }
}
#endif
