using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace WinMemoryCleaner.Sensors
{
    internal static class SensorParentIdentity
    {
        internal static bool TryParse(string[] args,out int pid,out long ticks)
        {
            pid=0;ticks=0;
            return args!=null&&args.Length==4&&args[0]=="--parent-pid"&&args[2]=="--parent-start-ticks"&&
                Int32.TryParse(args[1],NumberStyles.None,CultureInfo.InvariantCulture,out pid)&&pid>0&&
                Int64.TryParse(args[3],NumberStyles.None,CultureInfo.InvariantCulture,out ticks)&&ticks>0&&ticks<=DateTime.MaxValue.Ticks;
        }
        internal static bool Matches(Process process,int pid,long ticks)
        {
            if(process==null)return false;
            try { return process.Id==pid&&!process.HasExited&&process.StartTime.ToUniversalTime().Ticks==ticks; }
            catch(InvalidOperationException) { return false; }
            catch(Win32Exception) { return false; }
        }
    }
}
