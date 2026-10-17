using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinMemoryCleaner
{
    /// <summary>Reads only the identified CPU temperature exposed by the already-installed ASUS driver.</summary>
    internal sealed class AsusCpuTemperatureSource : IDisposable
    {
        private const string Source = "ASUS System Control Interface / CPU sensor 0x00120094";
        private readonly byte[] _request = CreateRequest();
        private readonly byte[] _response = new byte[16];
        private SafeFileHandle _handle;
        private DateTime _retry;
        private bool _disposed;
        private MetricValue _failure = MetricValue.Missing(MetricStatus.Unsupported, Source + ": driver not available");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);

        public AsusCpuTemperatureSource() { }

        internal static byte[] CreateRequest()
        {
            var request = new byte[16];
            BitConverter.GetBytes(0x53545344u).CopyTo(request, 0); // DSTS: get status, never a setting method.
            BitConverter.GetBytes(8u).CopyTo(request, 4);
            BitConverter.GetBytes(0x00120094u).CopyTo(request, 8); // CPU temperature, not an ACPI thermal zone.
            return request;
        }

        internal static MetricValue DecodeResponse(byte[] response, int returned)
        {
            if (response == null || returned < 4 || returned > response.Length)
                return MetricValue.Missing(MetricStatus.Error, Source + ": incomplete driver response");
            uint raw = BitConverter.ToUInt32(response, 0);
            int temperature = (int)(raw & 0xffffu);
            if ((raw & 0xffff0000u) != 0x00010000u || temperature < 1 || temperature > 150)
                return MetricValue.Missing(MetricStatus.Unsupported, Source + ": sensor absent or invalid reading (0x" + raw.ToString("X8") + ")");
            return MetricValue.Available(temperature, Source);
        }

        private static MetricValue DriverFailure(int error, string operation)
        {
            var status = error == 5 ? MetricStatus.AccessDenied : error == 2 || error == 3 ? MetricStatus.Unsupported : MetricStatus.Error;
            return MetricValue.Missing(status, Source + ": " + operation + " (" + error + "; " + new Win32Exception(error).Message + ")");
        }

        internal MetricValue Sample()
        {
            if (_disposed) throw new ObjectDisposedException("AsusCpuTemperatureSource");
            if (DateTime.UtcNow < _retry) return _failure;
            try
            {
                if (_handle == null)
                {
                    // The driver's status IOCTL needs no write access. Do not install or configure a driver.
                    _handle = CreateFile(@"\\.\ATKACPI", 0, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
                    if (_handle.IsInvalid) return Fail(DriverFailure(Marshal.GetLastWin32Error(), "open driver"));
                }
                Array.Clear(_response, 0, _response.Length);
                uint returned;
                if (!DeviceIoControl(_handle, 0x0022240C, _request, (uint)_request.Length, _response, (uint)_response.Length, out returned, IntPtr.Zero))
                    return Fail(DriverFailure(Marshal.GetLastWin32Error(), "read CPU sensor"));
                var reading = DecodeResponse(_response, returned > int.MaxValue ? -1 : (int)returned);
                if (reading.Status != MetricStatus.Available) return Fail(reading);
                _retry = DateTime.MinValue;
                return reading;
            }
            catch (Exception ex)
            {
                return Fail(MetricValue.Missing(MetricStatus.Error, Source + ": " + ex.Message));
            }
        }

        private MetricValue Fail(MetricValue failure)
        {
            _failure = failure;
            Suspend();
            _retry = DateTime.UtcNow.AddSeconds(30);
            return failure;
        }

        internal void Suspend()
        {
            if (_handle != null) _handle.Dispose();
            _handle = null;
        }

        public void Dispose()
        {
            _disposed = true;
            Suspend();
        }
    }
}
