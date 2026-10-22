using System;

namespace WinMemoryCleaner
{
    [Flags]
    internal enum ProcessTableChange
    {
        None=0, Membership=1, Name=2, WorkingSetBytes=4,
        PrivateBytes=8, CpuUsage=16, GpuUsage=32, GpuMemoryBytes=64
    }
    internal static class ProcessTableChanges
    {
        internal static bool RequiresRefresh(ProcessTableChange changes,string field,bool hasQuery)
        {
            if((changes&ProcessTableChange.Membership)!=0)return true;
            if((changes&ProcessTableChange.Name)!=0&&(hasQuery||field=="Name"))return true;
            ProcessTableChange sorted=ProcessTableChange.None;
            if(field=="WorkingSetBytes")sorted=ProcessTableChange.WorkingSetBytes;
            else if(field=="PrivateBytes")sorted=ProcessTableChange.PrivateBytes;
            else if(field=="CpuUsage")sorted=ProcessTableChange.CpuUsage;
            else if(field=="GpuUsage")sorted=ProcessTableChange.GpuUsage;
            else if(field=="GpuMemoryBytes")sorted=ProcessTableChange.GpuMemoryBytes;
            return (changes&sorted)!=0;
        }
        internal static ProcessTableChange Between(ProcessSnapshot before,ProcessSnapshot after)
        {
            ProcessTableChange changes=ProcessTableChange.None;
            if(before.Name!=after.Name)changes|=ProcessTableChange.Name;
            if(before.WorkingSetBytes!=after.WorkingSetBytes)changes|=ProcessTableChange.WorkingSetBytes;
            if(before.PrivateBytes!=after.PrivateBytes)changes|=ProcessTableChange.PrivateBytes;
            if(before.CpuUsage!=after.CpuUsage)changes|=ProcessTableChange.CpuUsage;
            if(before.GpuUsage!=after.GpuUsage)changes|=ProcessTableChange.GpuUsage;
            if(before.GpuMemoryBytes!=after.GpuMemoryBytes)changes|=ProcessTableChange.GpuMemoryBytes;
            return changes;
        }
    }
}
