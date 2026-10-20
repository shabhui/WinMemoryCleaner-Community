using System;

namespace WinMemoryCleaner
{
    internal interface ITemperatureSource : IDisposable
    {
        void Sample(out MetricValue cpu,out MetricValue gpu);
        void Suspend();
    }
    internal interface ISensorBackend : IDisposable
    {
        string BackendStatus { get; }
        string DriverStatus { get; }
        int ProcessId { get; }
        string Diagnostic { get; }
        void GetSnapshot(out MetricValue cpu,out MetricValue gpu);
        void Configure(int seconds);
        void Suspend();
        void ResetDriverState();
    }
}
