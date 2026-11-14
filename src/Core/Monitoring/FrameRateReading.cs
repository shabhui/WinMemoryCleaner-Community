using System;
using System.Globalization;

namespace WinMemoryCleaner
{
    /// <summary>A selected application's successful DirectX Present rate, not scan-out FPS.</summary>
    public sealed class FrameRateReading
    {
        private readonly MetricValue _metric;
        private readonly string _targetName;
        private readonly string _stateKey;

        /// <summary>Creates an immutable reading. The target is reduced to a display basename.</summary>
        /// <param name="metric">An available rate or an unavailable metric with no value.</param>
        /// <param name="targetName">The process display name, never an executable path.</param>
        /// <param name="stateKey">A localized FPS state key.</param>
        public FrameRateReading(MetricValue metric, string targetName, string stateKey)
        {
            if (metric == null) throw new ArgumentNullException("metric");
            _metric = metric;
            _targetName = DisplayName(targetName);
            _stateKey = stateKey ?? "FpsError";
        }

        /// <summary>Gets the rate, or a non-Available metric whose Value is null.</summary>
        public MetricValue Metric { get { return _metric; } }
        /// <summary>Gets only the process display name.</summary>
        public string TargetName { get { return _targetName; } }
        /// <summary>Gets the FPS state key used by the presentation layer.</summary>
        public string StateKey { get { return _stateKey; } }

        internal static string DisplayName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int separator = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            value = value.Substring(separator + 1).Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
            if (value.Length > 128) value = value.Substring(0, 128);
            foreach (char character in value)
                if (char.IsControl(character) || character == ':' || character == '@') return string.Empty;
            return value;
        }

        internal static string FailureKey(string state)
        {
            switch (state)
            {
                case "FpsAccessDenied": case "FpsUnsupported": case "FpsTargetExited":
                case "FpsIdentityChanged": case "FpsEventLoss": case "FpsError": return state;
                default: return "FpsError";
            }
        }

        internal static FrameRateReading Missing(string state, string targetName, string source, int code)
        {
            MetricStatus status;
            switch (state)
            {
                case "FpsDisabled": status = MetricStatus.Disabled; break;
                case "FpsChooseTarget": case "FpsWarmingUp": case "FpsNoFrames": status = MetricStatus.WarmingUp; break;
                case "FpsUnsupported": status = MetricStatus.Unsupported; break;
                case "FpsAccessDenied": status = MetricStatus.AccessDenied; break;
                default: status = MetricStatus.Error; break;
            }
            string detail = code == 0 ? source : source + " (" + code.ToString(CultureInfo.InvariantCulture) + ")";
            return new FrameRateReading(MetricValue.Missing(status, detail), targetName, state);
        }
    }

    /// <summary>Demand-driven, read-only application Present FPS monitoring.</summary>
    public interface IFrameRateMonitor : IDisposable
    {
        /// <summary>Changes demand without opening, closing, or joining native capture on the caller.</summary>
        /// <param name="active">True when the FPS presentation is visible and enabled.</param>
        void SetActive(bool active);
        /// <summary>Copies a manual identity or follows the foreground process on the sampling worker.</summary>
        /// <param name="followForeground">True to follow the foreground process, excluding this tool.</param>
        /// <param name="target">The transient manual PID, creation FILETIME and display name; may be null.</param>
        void SetTarget(bool followForeground, ProcessSnapshot target);
        /// <summary>Samples on the existing monitoring worker, never on the UI thread.</summary>
        /// <returns>A fresh reading, or an explicit unavailable state.</returns>
        FrameRateReading Sample();
    }
}
