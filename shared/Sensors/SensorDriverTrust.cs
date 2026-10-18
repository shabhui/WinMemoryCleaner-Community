using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinMemoryCleaner.Sensors
{
    internal sealed class SensorDriverState
    {
        internal bool CpuAllowed { get; private set; }
        internal bool ServicePresent { get; private set; }
        internal Version Version { get; private set; }
        internal string Status { get; private set; }
        internal SensorDriverState(bool cpuAllowed, bool servicePresent, Version version, string status)
        { CpuAllowed = cpuAllowed; ServicePresent = servicePresent; Version = version; Status = status ?? "InspectionFailed"; }
    }

    /// <summary>Read-only verification. Neither registry presence nor a signature alone proves the driver mode.</summary>
    internal static class SensorDriverTrust
    {
        private const string OfficialDriverHash = "f92de04e5a02256e86ffa1b4252fe669282198b7329227f40e51bca9267d133d";
        private const string OfficialInstallerHash = "a3a46226c5e2824f4cdd42be0eecbabfc672c86f7889710f5ab1e6ad385b47a0";
        private const string OfficialSignerThumbprint = "F380DCC9F706E2756A5047B832FFE719E1BC35F5";

        internal static SensorDriverState Evaluate(bool servicePresent, Version version, string imageHash, bool signatureValid, bool deviceReady)
        {
            if (!servicePresent) return new SensorDriverState(false, false, version, "NotInstalled");
            bool pinned = String.Equals(imageHash, OfficialDriverHash, StringComparison.OrdinalIgnoreCase);
            if (!pinned || !signatureValid || version == null || !version.Equals(new Version(2, 1, 0))) return new SensorDriverState(false, true, version, "UnverifiedDriver");
            return new SensorDriverState(deviceReady, true, version, deviceReady ? "Ready" : "DriverNotReady");
        }

        internal static SensorDriverState Inspect()
        {
            try
            {
                using (RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (RegistryKey service = hive.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO", false))
                {
                    if (service == null) return new SensorDriverState(false, false, null, "NotInstalled");
                    Version version; int error;
                    bool ready = ReadDeviceVersion(out version, out error);
                    string path = ResolveImagePath(service.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                    if (path == null) return new SensorDriverState(false, true, version, "UnverifiedDriver");
                    CheckFilePath(path);
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        string hash = Hash(input);
                        bool signature = String.Equals(hash, OfficialDriverHash, StringComparison.OrdinalIgnoreCase) && VerifySignature(path);
                        SensorDriverState result = Evaluate(true, version, hash, signature, ready);
                        if (error == 5 && !result.CpuAllowed) return new SensorDriverState(false, true, version, "AccessDenied");
                        return result;
                    }
                }
            }
            catch (UnauthorizedAccessException) { return new SensorDriverState(false, true, null, "AccessDenied"); }
            catch (SecurityException) { return new SensorDriverState(false, true, null, "AccessDenied"); }
            catch (IOException) { return new SensorDriverState(false, true, null, "UnverifiedDriver"); }
            catch (ArgumentException) { return new SensorDriverState(false, true, null, "UnverifiedDriver"); }
            catch (CryptographicException) { return new SensorDriverState(false, true, null, "UnverifiedDriver"); }
        }

        internal static bool VerifyInstaller(string path)
        {
            try
            {
                CheckFilePath(path);
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!String.Equals(Hash(input), OfficialInstallerHash, StringComparison.OrdinalIgnoreCase) || !VerifySignature(path)) return false;
                    X509Certificate signed = X509Certificate.CreateFromSignedFile(path);
                    try
                    {
                        var certificate = new X509Certificate2(signed);
                        try { return String.Equals(certificate.Thumbprint, OfficialSignerThumbprint, StringComparison.OrdinalIgnoreCase); }
                        finally { certificate.Reset(); }
                    }
                    finally { signed.Reset(); }
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            catch (ArgumentException) { return false; }
            catch (CryptographicException) { return false; }
        }

        private static string ResolveImagePath(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 1024) return null;
            string path = Environment.ExpandEnvironmentVariables(value.Trim());
            if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path.Substring(4);
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows), path.Substring(12));
            if (path.StartsWith("\"", StringComparison.Ordinal) && path.EndsWith("\"", StringComparison.Ordinal)) path = path.Substring(1, path.Length - 2);
            if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !String.Equals(Path.GetFileName(path), "PawnIO.sys", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetFullPath(path);
        }

        private static void CheckFilePath(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new InvalidDataException("Missing signed file.");
            for (string part = Path.GetFullPath(path); !String.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Signed file path contains a reparse point.");
        }

        private static string Hash(Stream input)
        {
            using (var hash = SHA256.Create())
            { var text = new StringBuilder(); foreach (byte value in hash.ComputeHash(input)) text.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); }
        }

        private static bool ReadDeviceVersion(out Version version, out int error)
        {
            version = null; error = 0;
            // Only the documented version query is used. No module is loaded for probing.
            using (SafeFileHandle device = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (device.IsInvalid) { error = Marshal.GetLastWin32Error(); return false; }
                uint encoded, bytes;
                if (!DeviceIoControl(device, 0xA1B22184, IntPtr.Zero, 0, out encoded, 4, out bytes, IntPtr.Zero)) { error = Marshal.GetLastWin32Error(); return false; }
                if (bytes != 4) return false;
                version = new Version((int)(encoded >> 16), (int)((encoded >> 8) & 255), (int)(encoded & 255));
                return true;
            }
        }

        private static bool VerifySignature(string path)
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            try
            {
                Marshal.StructureToPtr(file, buffer, false);
                // No UI or security-policy downgrade. Revocation is checked using the system cache.
                var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2, UnionChoice = 1, File = buffer, ProviderFlags = 0x3080 };
                Guid action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
            }
            finally { Marshal.DestroyStructure(buffer, typeof(TrustFile)); Marshal.FreeHGlobal(buffer); }
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, uint inputLength, out uint output, uint outputLength, out uint bytes, IntPtr overlapped);
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile
        {
            internal uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] internal string Path;
            internal IntPtr Handle, KnownSubject;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            internal uint Size;
            internal IntPtr PolicyCallback, SipData;
            internal uint UiChoice, RevocationChecks, UnionChoice;
            internal IntPtr File;
            internal uint StateAction;
            internal IntPtr StateData, Url;
            internal uint ProviderFlags, UiContext;
            internal IntPtr SignatureSettings;
        }
    }
}
