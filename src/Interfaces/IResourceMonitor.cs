using System;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public interface IResourceMonitor : IDisposable
    {
        ResourceSnapshot Sample(bool includeProcesses, bool includeTemperatures);
        void Suspend();
    }
}
#pragma warning restore 1591
