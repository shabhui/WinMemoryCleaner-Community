using System;
using System.Web.Script.Serialization;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    // One independent value keeps existing community settings compatible.
    public sealed class MonitoringOptions
    {
        public int RefreshSeconds { get; set; }
        // Foreground=0, explicitly selected process=1. Never persist a process PID here.
        public int FpsTargetMode { get; set; }
        public bool OverlayEnabled { get; set; }
        public bool ClickThrough { get; set; }
        // Horizontal=0, vertical=1. Older settings safely keep the horizontal default.
        public int Layout { get; set; }
        public string Screen { get; set; }
        public int Corner { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        // CPU=1, GPU=2, memory=4, CPU temperature=8, GPU temperature=16, FPS=32 (opt-in).
        public int Items { get; set; }

        public MonitoringOptions()
        {
            RefreshSeconds = 2;
            ClickThrough = true;
            Screen = string.Empty;
            Items = 7;
        }
        public void Normalize()
        {
            RefreshSeconds = MonitoringPolicy.NormalizeRefresh(RefreshSeconds);
            Corner = Corner >= 0 && Corner <= 4 ? Corner : 0;
            Layout = Layout == 1 ? 1 : 0;
            Items &= 63;
            FpsTargetMode = FpsTargetMode == 1 ? 1 : 0;
            if (Items == 0) Items = 4;
            Screen = Screen ?? string.Empty;
            if (double.IsNaN(X) || double.IsInfinity(X)) X = 16;
            if (double.IsNaN(Y) || double.IsInfinity(Y)) Y = 16;
        }
        public static MonitoringOptions Deserialize(string json)
        {
            MonitoringOptions options = null;
            try
            {
                if (!string.IsNullOrEmpty(json)) options = new JavaScriptSerializer().Deserialize<MonitoringOptions>(json);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            options = options ?? new MonitoringOptions();
            options.Normalize();
            return options;
        }
        public string Serialize()
        {
            Normalize();
            return new JavaScriptSerializer().Serialize(this);
        }
    }
}
#pragma warning restore 1591
