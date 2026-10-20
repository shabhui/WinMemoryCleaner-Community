using LibreHardwareMonitor.Hardware;

namespace WinMemoryCleaner.Sensors
{
    /// <summary>Never restores user control modes or persists hardware settings.</summary>
    internal sealed class DefaultSensorSettings : ISettings
    {
        public bool Contains(string name) { return false; }
        public string GetValue(string name,string defaultValue) { return defaultValue; }
        public void SetValue(string name,string value) { }
        public void Remove(string name) { }
    }
}
