using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>Explicit, interactive installation only. Never takes over an existing shared driver.</summary>
    internal sealed class SensorDriverInstaller
    {
        private static int _globalBusy;
        private readonly Func<SensorDriverState> _inspect;
        private readonly Func<SensorPayloadLease> _prepare;
        private readonly Func<string, bool> _verify;
        private readonly Func<string, int> _install;
        private readonly Action _completed;
        private volatile string _status = "NotStarted";
        private int _busy;

        internal SensorDriverInstaller(Action completed)
            : this(SensorDriverTrust.Inspect, delegate { return SensorPayload.CreateDefault().Prepare(); }, SensorDriverTrust.VerifyInstaller, RunOfficialInstaller, completed) { }
        internal SensorDriverInstaller(Func<SensorDriverState> inspect, Func<SensorPayloadLease> prepare, Func<string, bool> verify, Func<string, int> install, Action completed)
        {
            if (inspect == null) throw new ArgumentNullException("inspect");
            if (prepare == null) throw new ArgumentNullException("prepare");
            if (verify == null) throw new ArgumentNullException("verify");
            if (install == null) throw new ArgumentNullException("install");
            _inspect = inspect; _prepare = prepare; _verify = verify; _install = install; _completed = completed ?? delegate { };
        }
        internal bool IsInstalling { get { return Interlocked.CompareExchange(ref _busy, 0, 0) != 0; } }
        internal string Status { get { return _status; } }
        internal bool Begin(bool explicitlyConfirmed)
        {
            if (!explicitlyConfirmed) return false;
            if (Interlocked.CompareExchange(ref _globalBusy, 1, 0) != 0) return false;
            Interlocked.Exchange(ref _busy, 1); _status = "Installing";
            try
            {
                // A foreground worker keeps the verified file lease alive if the UI closes during an interactive install.
                var worker = new Thread(Run) { IsBackground = false, Name = "Confirmed PawnIO installation" };
                worker.Start(); return true;
            }
            catch (Exception error)
            { _status = Failure(error); Interlocked.Exchange(ref _busy, 0); Interlocked.Exchange(ref _globalBusy, 0); return false; }
        }
        private bool CanInstall(SensorDriverState state)
        {
            if (state == null) { _status = "InspectionFailed"; return false; }
            if (state.CpuAllowed) { _status = "Ready"; return false; }
            if (state.ServicePresent) { _status = "KeepExistingDriver"; return false; }
            if (!String.Equals(state.Status, "NotInstalled", StringComparison.Ordinal)) { _status = state.Status; return false; }
            return true;
        }
        private void Run()
        {
            bool attempted = false;
            try
            {
                using (var mutex = new Mutex(false, @"Local\WinMemoryCleaner.ConfirmedPawnIOInstall"))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { _status = "InstallationBusy"; return; }
                    try
                    {
                        if (!CanInstall(_inspect())) return;
                        using (SensorPayloadLease lease = _prepare())
                        {
                            if (lease == null || !_verify(lease.Installer)) { _status = "SignatureInvalid"; return; }
                            if (!CanInstall(_inspect())) return;
                            attempted = true;
                            int exitCode = _install(lease.Installer);
                            SensorDriverState after = _inspect();
                            if (exitCode == 3010) _status = "RestartRequired";
                            else if (exitCode == 1602) _status = "Cancelled";
                            else if (exitCode != 0) _status = "InstallFailed (" + exitCode.ToString(CultureInfo.InvariantCulture) + ")";
                            else _status = after != null && after.CpuAllowed ? "Ready" : "InstalledNotReady";
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Win32Exception error) { _status = error.NativeErrorCode == 1223 ? "Cancelled" : Failure(error); }
            catch (Exception error) { _status = Failure(error); }
            finally
            {
                try { if (attempted) _completed(); }
                finally { Interlocked.Exchange(ref _busy, 0); Interlocked.Exchange(ref _globalBusy, 0); }
            }
        }
        private static string Failure(Exception error)
        { string message = error == null ? "Unknown error" : error.Message; if (message.Length > 192) message = message.Substring(0, 192); return "InstallFailed: " + message; }
        private static int RunOfficialInstaller(string path)
        {
            // Fixed -install selects the normal official edition; no unrestricted, silent, uninstall or reboot switches.
            var start = new ProcessStartInfo(path, "-install") { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path) };
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new Win32Exception("Official installer did not start.");
                process.WaitForExit(); return process.ExitCode;
            }
        }
    }
}
