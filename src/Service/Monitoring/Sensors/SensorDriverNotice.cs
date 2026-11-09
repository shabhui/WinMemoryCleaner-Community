using System;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>Explains an explicit temperature-enable action without treating every failure as a missing driver.</summary>
    internal sealed class SensorDriverNotice
    {
        internal string MessageKey { get; private set; }
        internal bool OfferInstallation { get; private set; }

        private SensorDriverNotice(string messageKey, bool offerInstallation)
        { MessageKey = messageKey; OfferInstallation = offerInstallation; }

        internal static SensorDriverNotice FromState(SensorDriverState state)
        {
            if (state != null && state.CpuAllowed) return null;
            if (state == null) return new SensorDriverNotice("TemperatureDriverInspectionFailed", false);
            if (!state.ServicePresent && String.Equals(state.Status, "NotInstalled", StringComparison.Ordinal))
                return new SensorDriverNotice("TemperatureDriverNeeded", true);
            switch (state.Status)
            {
                case "AccessDenied": return new SensorDriverNotice("TemperatureDriverAdministrator", false);
                case "DriverNotReady": return new SensorDriverNotice("TemperatureDriverNotReady", false);
                case "UnverifiedDriver":
                case "KeepExistingDriver": return new SensorDriverNotice("TemperatureDriverUnverified", false);
                default: return new SensorDriverNotice("TemperatureDriverInspectionFailed", false);
            }
        }
    }
}
