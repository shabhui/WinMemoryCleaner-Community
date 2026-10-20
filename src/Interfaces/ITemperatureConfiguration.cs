namespace WinMemoryCleaner
{
    // Optional extension: existing IResourceMonitor implementations need not start a sensor backend.
    internal interface ITemperatureConfiguration
    {
        void ConfigureTemperatures(int refreshSeconds);
        // Must only revoke/request cached backend demand; never wait for hardware or disposal.
        void SetTemperatureDemand(bool requested);
        void ResetTemperatureDriver();
    }
}
