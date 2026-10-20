using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WinMemoryCleaner.Sensors
{
    internal static class Program
    {
        internal static int Main(string[] args)
        {
            int pid;long ticks;
            if(!SensorParentIdentity.TryParse(args,out pid,out ticks))return 2;
            using(Process current=Process.GetCurrentProcess())if(current.Id==pid)return 2;
            Process parent;
            try { parent=Process.GetProcessById(pid); }
            catch(ArgumentException) { return 2; }
            using(parent)
            {
                if(!SensorParentIdentity.Matches(parent,pid,ticks))return 2;
                string detail;
                if(!SensorRuntimeSupport.Check(out detail)||!RestrictNativeSearch())
                { Console.Error.WriteLine("Unsupported sensor runtime or native loader policy: "+detail);return 2; }
                try
                {
                    using(Stream input=Console.OpenStandardInput())
                    using(Stream output=Console.OpenStandardOutput())
                    using(var source=new LhmTemperatureSource())
                    using(var host=new SensorHost(input,output,source.Sample,delegate { return SensorParentIdentity.Matches(parent,pid,ticks); }))
                    {
                        int code=host.Run();if(!String.IsNullOrEmpty(host.Diagnostic))Console.Error.WriteLine(host.Diagnostic);return code;
                    }
                }
                catch(Exception error) { Console.Error.WriteLine(error.GetType().Name+": "+error.Message);return 2; }
            }
        }
        private static bool RestrictNativeSearch()
        {
            try { return SetDllDirectory(string.Empty)&&SetDefaultDllDirectories(0x00000200U|0x00000800U); }
            catch(EntryPointNotFoundException) { return false; }
            catch(Win32Exception) { return false; }
        }
        [DllImport("kernel32.dll",EntryPoint="SetDllDirectoryW",ExactSpelling=true,CharSet=CharSet.Unicode,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectory(string directory);
        [DllImport("kernel32.dll",ExactSpelling=true,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint directories);
    }
}
