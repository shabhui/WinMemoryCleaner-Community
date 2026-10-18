namespace WinMemoryCleaner.Sensors
{
    // Shared C#4 data only. No library types or arbitrary deserialization metadata.
    internal sealed class SensorReading
    {
        internal string HardwareId { get; set; }
        internal string HardwareType { get; set; }
        internal string DeviceName { get; set; }
        internal string SensorId { get; set; }
        internal string SensorName { get; set; }
        internal string SensorType { get; set; }
        internal string Measurement { get; set; }
        internal double? Celsius { get; set; }
    }
}
