using System;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public enum MetricStatus { Available, WarmingUp, Unsupported, AccessDenied, Error, Disabled }
    public sealed class MetricValue
    {
        public double? Value { get; private set; }
        public MetricStatus Status { get; private set; }
        public string Detail { get; private set; }
        private MetricValue(double? value, MetricStatus status, string detail) { Value=value; Status=status; Detail=detail??string.Empty; }
        public static MetricValue Available(double value, string source)
        {
            return double.IsNaN(value)||double.IsInfinity(value)?Missing(MetricStatus.Error,"Invalid sensor value"):new MetricValue(value,MetricStatus.Available,source);
        }
        public static MetricValue Missing(MetricStatus status, string reason)
        {
            if(status==MetricStatus.Available)throw new ArgumentException("An unavailable reading cannot have Available status.","status");
            return new MetricValue(null,status,reason);
        }
    }
}
#pragma warning restore 1591
