using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using WinMemoryCleaner.Sensors;

namespace WinMemoryCleaner
{
    /// <summary>Explicit, interactive installation only. Never takes over an existing shared driver.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The per-operation CancellationTokenSource belongs to the worker. ReleaseBusy disposes it on every start failure and completion path; the facade only signals cancellation and must not dispose a source still in use by that worker.")]
    internal sealed class SensorDriverInstaller
    {
        private static int _globalBusy;
        private readonly Func<SensorDriverState> _inspect;
        private readonly Func<string, CancellationToken, SensorPayloadLease> _prepare;
        private readonly Func<string, bool> _verify;
        private readonly Func<string, int> _install;
        private readonly Action _completed;
        private volatile string _status = "NotStarted";
        private int _busy;
        private readonly object _stateLock = new object();
        private CancellationTokenSource _cancellation;
        private bool _preparing;

        internal SensorDriverInstaller(Action completed)
            : this(SensorDriverTrust.Inspect, delegate(string path, CancellationToken cancellation) { return SensorInstallerPackage.CreateDefault().Prepare(path, cancellation); }, SensorDriverTrust.VerifyInstaller, RunOfficialInstaller, completed) { }
        internal SensorDriverInstaller(Func<SensorDriverState> inspect, Func<string, CancellationToken, SensorPayloadLease> prepare, Func<string, bool> verify, Func<string, int> install, Action completed)
        {
            if (inspect == null) throw new ArgumentNullException("inspect");
            if (prepare == null) throw new ArgumentNullException("prepare");
            if (verify == null) throw new ArgumentNullException("verify");
            if (install == null) throw new ArgumentNullException("install");
            _inspect = inspect; _prepare = prepare; _verify = verify; _install = install; _completed = completed ?? delegate { };
        }
        internal bool IsInstalling { get { return Interlocked.CompareExchange(ref _busy, 0, 0) != 0; } }
        internal string Status { get { return _status; } }
        internal bool CanCancelPreparation { get { lock (_stateLock) return _preparing && _cancellation != null && !_cancellation.IsCancellationRequested; } }
        internal bool Begin(bool explicitlyConfirmed)
        { return Begin(explicitlyConfirmed, null); }
        internal bool Begin(bool explicitlyConfirmed, string localInstallerPath)
        {
            if (!explicitlyConfirmed) return false;
            if (Interlocked.CompareExchange(ref _globalBusy, 1, 0) != 0) return false;
            Interlocked.Exchange(ref _busy, 1); _status = "PreparingInstaller";
            try
            {
                CancellationToken cancellation;
                lock (_stateLock) { _cancellation = new CancellationTokenSource(); cancellation = _cancellation.Token; _preparing = true; }
                // Preparation is cancellable and network-bounded. Once started, the official installer owns its UI.
                // A foreground worker retains the verified file lease even if our UI closes during installation.
                var worker = new Thread(delegate() { Run(localInstallerPath, cancellation); }) { IsBackground = false, Name = "Confirmed PawnIO installation" };
                worker.Start(); return true;
            }
            catch (Exception error)
            { _status = Failure(error); ReleaseBusy(); return false; }
        }
        internal bool CancelPreparation()
        {
            lock (_stateLock)
            {
                if (!_preparing || _cancellation == null || _cancellation.IsCancellationRequested) return false;
                _cancellation.Cancel(); return true;
            }
        }
        private void ReleaseBusy()
        {
            lock (_stateLock)
            {
                _preparing = false;
                if (_cancellation != null) { _cancellation.Dispose(); _cancellation = null; }
            }
            Interlocked.Exchange(ref _busy, 0); Interlocked.Exchange(ref _globalBusy, 0);
        }
        private bool CanInstall(SensorDriverState state)
        {
            if (state == null) { _status = "InspectionFailed"; return false; }
            if (state.CpuAllowed) { _status = "Ready"; return false; }
            if (state.ServicePresent) { _status = "KeepExistingDriver"; return false; }
            if (!String.Equals(state.Status, "NotInstalled", StringComparison.Ordinal)) { _status = state.Status; return false; }
            return true;
        }
        private void Run(string localInstallerPath, CancellationToken cancellation)
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
                        cancellation.ThrowIfCancellationRequested();
                        if (!CanInstall(_inspect())) return;
                        using (SensorPayloadLease lease = _prepare(localInstallerPath, cancellation))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (lease == null || !_verify(lease.Installer)) { _status = "SignatureInvalid"; return; }
                            if (!CanInstall(_inspect())) return;
                            lock (_stateLock) { cancellation.ThrowIfCancellationRequested(); _preparing = false; _status = "Installing"; }
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
            catch (OperationCanceledException) { _status = "Cancelled"; }
            catch (Win32Exception error) { _status = error.NativeErrorCode == 1223 ? "Cancelled" : Failure(error); }
            catch (Exception error) { _status = Failure(error); }
            finally
            {
                try { if (attempted) _completed(); }
                finally { ReleaseBusy(); }
            }
        }
        private static string Failure(Exception error)
        { string message = error == null ? "Unknown error" : error.Message; if (message.Length > 192) message = message.Substring(0, 192); return "InstallFailed: " + message; }
        private static int RunOfficialInstaller(string path)
        {
            // Fixed -install selects the normal official edition; no unrestricted, silent, uninstall or reboot switches.
            var start = new ProcessStartInfo(path, "-install") { UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.SystemDirectory };
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new Win32Exception("Official installer did not start.");
                process.WaitForExit(); return process.ExitCode;
            }
        }
    }
}
