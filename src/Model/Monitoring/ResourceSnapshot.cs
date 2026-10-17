using System.Collections.Generic;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public sealed class ProcessSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long CreationTime { get; set; }
        public long? WorkingSetBytes { get; set; }
        public long? PrivateBytes { get; set; }
        public long? GpuMemoryBytes { get; set; }
        public double? CpuUsage { get; set; }
        public double? GpuUsage { get; set; }
        public MetricStatus CpuStatus { get; set; }
        public MetricStatus GpuStatus { get; set; }
        public string Detail { get; set; }
    }
    public sealed class ResourceSnapshot
    {
        public MetricValue CpuUsage { get; set; }
        public MetricValue GpuUsage { get; set; }
        public MetricValue CpuTemperature { get; set; }
        public MetricValue GpuTemperature { get; set; }
        public string GpuName { get; set; }
        public IList<ProcessSnapshot> Processes { get; set; }
        public ResourceSnapshot()
        {
            CpuUsage=GpuUsage=CpuTemperature=GpuTemperature=MetricValue.Missing(MetricStatus.WarmingUp,"No sample yet");
            Processes=new List<ProcessSnapshot>();
        }
    }
}
#pragma warning restore 1591
