using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace WinMemoryCleaner.Sensors
{
    /// <summary>Native, read-only compatibility check shared with the net40 parent.</summary>
    internal static class SensorRuntimeSupport
    {
        internal static bool Evaluate(int architecture,int major,int build,int release,out string detail)
        {
            if(architecture!=9) { detail="NativeAmd64Required";return false; }
            if(major<10||build<17763) { detail="Windows10_1809Required";return false; }
            if(release<461808) { detail="Net472Required";return false; }
            detail="Ready";return true;
        }
        internal static bool Check(out string detail)
        {
            try
            {
                if(Environment.OSVersion.Platform!=PlatformID.Win32NT) { detail="WindowsRequired";return false; }
                SystemInfo system;GetNativeSystemInfo(out system);
                var version=new OsVersion { Size=(uint)Marshal.SizeOf(typeof(OsVersion)) };
                if(RtlGetVersion(ref version)!=0) { detail="RuntimeInspectionFailed";return false; }
                using(RegistryKey hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32))
                using(RegistryKey key=hive.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",false))
                {
                    object value=key==null?null:key.GetValue("Release",null);
                    int release=value is int?(int)value:-1;
                    return Evaluate(system.Architecture,(int)version.Major,(int)version.Build,release,out detail);
                }
            }
            catch(UnauthorizedAccessException) { detail="RuntimeInspectionDenied";return false; }
            catch(SecurityException) { detail="RuntimeInspectionDenied";return false; }
            catch(IOException) { detail="RuntimeInspectionFailed";return false; }
            catch(DllNotFoundException) { detail="RuntimeInspectionFailed";return false; }
            catch(EntryPointNotFoundException) { detail="RuntimeInspectionFailed";return false; }
        }
        [DllImport("kernel32.dll",ExactSpelling=true)]
        private static extern void GetNativeSystemInfo(out SystemInfo system);
        [DllImport("ntdll.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]
        private static extern int RtlGetVersion(ref OsVersion version);
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemInfo
        {
            internal ushort Architecture,Reserved;
            internal uint PageSize;
            internal IntPtr MinimumAddress,MaximumAddress;
            internal UIntPtr ActiveMask;
            internal uint ProcessorCount,ProcessorType,AllocationGranularity;
            internal ushort ProcessorLevel,ProcessorRevision;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct OsVersion
        {
            internal uint Size,Major,Minor,Build,Platform;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string ServicePack;
        }
    }
}
