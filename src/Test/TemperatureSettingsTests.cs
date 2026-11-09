using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    public sealed class TemperatureSettingsTests
    {
        private sealed class Monitor : IResourceMonitor,ITemperatureConfiguration
        {
            internal int Samples,Resets,Disposals;
            internal string BackendStatus="Running";
            internal volatile bool Requested;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                Interlocked.Increment(ref Samples);
                return new ResourceSnapshot
                {
                    CpuTemperature=temperatures&&Requested?MetricValue.Available(54,"Fixture CPU package"):MetricValue.Missing(MetricStatus.Disabled,"not requested"),
                    GpuTemperature=MetricValue.Missing(MetricStatus.Unsupported,"Fixture GPU core is unavailable"),
                    SensorBackendStatus=BackendStatus,SensorDriverStatus="KeepExistingDriver",SensorDiagnostic="Fixture backend diagnostic"
                };
            }
            public void ConfigureTemperatures(int seconds) { }
            public void SetTemperatureDemand(bool requested) { Requested=requested; }
            public void ResetTemperatureDriver() { Interlocked.Increment(ref Resets); }
            public void Suspend() { }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }
        private sealed class Context : IDisposable
        {
            private readonly MainViewModel _cleaner;
            private readonly HotkeyService _hotkey;
            private readonly NotificationService _notifications;
            private readonly string _root;
            internal readonly Monitor Monitor=new Monitor();
            internal readonly MonitoringViewModel Model;
            internal int MonitorCreations,InstallerCreations,Prepares,Executions;
            internal Action Completed;
            internal SensorDriverInstaller Installer;
            internal string SelectedInstallerPath;
            private bool _disposed;
            internal Context(Func<string,int> execute)
                : this(execute,null) { }
            internal Context(Func<string,int> execute,Action<CancellationToken> prepare)
            {
                Assert.IsTrue(App.IsTestBuild);
                Settings.Reset(true);Settings.UseHotkey=false;
                Localizer.Language=new Language(CultureInfo.GetCultureInfo("zh-Hans"));
                _root=Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR")??TestContext.CurrentContext.WorkDirectory,"temperature-settings-"+Guid.NewGuid().ToString("N"));
                _hotkey=new HotkeyService();_notifications=new NotificationService(null);
                _cleaner=new MainViewModel(new ComputerService(),_hotkey,_notifications);
                Model=new MonitoringViewModel(_cleaner,Dispatcher.CurrentDispatcher,
                    delegate { Interlocked.Increment(ref MonitorCreations);return Monitor; },
                    delegate(Action completed)
                    {
                        Interlocked.Increment(ref InstallerCreations);Completed=completed;
                        Installer=new SensorDriverInstaller(
                            delegate { return new SensorDriverState(false,false,null,"NotInstalled"); },
                            delegate(string path,CancellationToken cancellation) { SelectedInstallerPath=path;Interlocked.Increment(ref Prepares);if(prepare!=null)prepare(cancellation);Directory.CreateDirectory(_root);return new SensorPayloadLease(_root); },
                            delegate { return true; },
                            delegate(string path) { Interlocked.Increment(ref Executions);return execute(path); },completed);
                        return Installer;
                    });
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                Model.Dispose();_cleaner.Dispose();_hotkey.Dispose();_notifications.Dispose();
                Assert.IsTrue(Until(delegate { return Installer==null||!Installer.IsInstalling; },5000),"Owned installer substitute must finish before cleanup.");
                if(MonitorCreations==0)Monitor.Dispose();
                else Assert.IsTrue(Until(delegate { return Monitor.Disposals==1; },2000),"The model must release its transferred monitor exactly once");
                if(Directory.Exists(_root))Directory.Delete(_root,true);
                Settings.Reset(true);GC.SuppressFinalize(this);
            }
        }
        private static bool Until(Func<bool> ready,int milliseconds)
        {
            var watch=Stopwatch.StartNew();
            while(!ready()&&watch.ElapsedMilliseconds<milliseconds)
            {
                var frame=new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { frame.Continue=false; }));
                Dispatcher.PushFrame(frame);Thread.Sleep(5);
            }
            return ready();
        }
        [Test]
        public void CancelledInstallationIsLazyAndDisposedModelRejectsConfirmation()
        {
            using(var context=new Context(delegate { Assert.Fail("No installer substitute should execute");return 0; }))
            {
                Assert.IsFalse(context.Model.BeginDriverInstallation(false));
                Assert.IsFalse(context.Model.BeginDriverInstallation(false,"unused-offline.exe"));
                Assert.AreEqual(0,context.InstallerCreations);Assert.AreEqual(0,context.Prepares);Assert.AreEqual(0,context.Executions);Assert.AreEqual(0,context.MonitorCreations);
                context.Model.Dispose();Assert.IsFalse(context.Model.BeginDriverInstallation(true));Assert.IsFalse(context.Model.CanInstallDriver);
                Assert.AreEqual(0,context.InstallerCreations);Assert.AreEqual(0,context.MonitorCreations);
            }
        }
        [Test]
        public void StatusAndDetailsRefreshForChineseEnglishAndEnglishFallback()
        {
            using(var context=new Context(delegate { return 0; }))
            {
                context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Model.TemperatureStatusText.Contains("运行中"); },2000));
                StringAssert.Contains("已保留现有共享驱动",context.Model.TemperatureStatusText);
                StringAssert.Contains("Fixture CPU package",context.Model.TemperatureStatusDetail);
                StringAssert.Contains("Fixture GPU core is unavailable",context.Model.TemperatureStatusDetail);
                StringAssert.Contains("Fixture backend diagnostic",context.Model.TemperatureStatusDetail);
                foreach(string culture in new[]{"en","de"})
                {
                    Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));
                    StringAssert.Contains("Universal temperature: Running",context.Model.TemperatureStatusText);
                    StringAssert.Contains("Existing shared driver kept",context.Model.TemperatureStatusText);
                    Assert.AreEqual("Install PawnIO driver…",context.Model.Labels["InstallSensorDriver"]);
                    foreach(string key in new[]{"SelectSensorInstaller","CancelSensorPreparation","SensorPreparingInstaller","DownloadSensorInstallerNote","LocalSensorInstallerNote"})Assert.AreNotEqual(key,context.Model.Labels[key]);
                }
                context.Model.SetViewState(false,false);Assert.IsFalse(context.Monitor.Requested);
                StringAssert.Contains("Not requested",context.Model.TemperatureStatusText);Assert.AreEqual("—",context.Model.OverlayCpuTemperatureText);
            }
        }
        [TestCase("zh-Hans","NotNeeded","无需通用后端")][TestCase("zh-Hans","WaitingForDriver","等待可信 CPU 驱动")]
        [TestCase("en","NotNeeded","Not needed")][TestCase("en","WaitingForDriver","Waiting for trusted CPU driver")]
        [TestCase("de","NotNeeded","Not needed")][TestCase("de","WaitingForDriver","Waiting for trusted CPU driver")]
        public void OnDemandBackendStatesAreLocalizedInActualStatusText(string culture,string status,string expected)
        {
            using(var context=new Context(delegate { return 0; }))
            {
                context.Monitor.BackendStatus=status;Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));
                context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Model.TemperatureStatusText.Contains(expected); },2000));
                Assert.AreEqual(expected,context.Model.Labels["Sensor"+status]);Assert.AreEqual(0,context.InstallerCreations);
            }
        }
        [Test]
        public void ConfirmedSubstituteReportsProgressFailureAndDefersResetToBackgroundSample()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            using(var context=new Context(delegate { entered.Set();if(!release.WaitOne(5000))throw new TimeoutException();return 5; }))
            {
                try
                {
                    Assert.IsTrue(context.Model.BeginDriverInstallation(true));Assert.IsTrue(entered.WaitOne(2000));
                    Assert.IsTrue(context.Model.IsInstallingDriver);Assert.IsFalse(context.Model.CanInstallDriver);
                    Assert.IsFalse(context.Model.BeginDriverInstallation(true));Assert.AreEqual(1,context.Executions);
                    StringAssert.Contains("安装器运行中",context.Model.TemperatureStatusText);
                    release.Set();Assert.IsTrue(Until(delegate { return !context.Model.IsInstallingDriver; },2000));
                    StringAssert.Contains("安装失败 (5)",context.Model.TemperatureStatusText);
                    Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));StringAssert.Contains("Installation failed (5)",context.Model.TemperatureStatusText);
                    Assert.AreEqual(0,context.MonitorCreations);Assert.AreEqual(0,context.Monitor.Resets);
                    context.Model.SetViewState(true,false);
                    Assert.IsTrue(Until(delegate { return context.Monitor.Resets==1; },2000));Assert.AreEqual(1,context.MonitorCreations);
                }
                finally { release.Set();Until(delegate { return context.Installer==null||!context.Installer.IsInstalling; },5000); }
            }
        }
        [Test]
        public void CompletionAfterDemandCancellationOrDisposeNeverRestoresTemperatureDemand()
        {
            using(var context=new Context(delegate { return 0; }))
            {
                context.Model.SetViewState(true,false);Assert.IsTrue(Until(delegate { return context.Monitor.Samples>0; },2000));
                context.Model.SetViewState(false,false);Assert.IsFalse(context.Monitor.Requested);
                Assert.IsTrue(context.Model.BeginDriverInstallation(true));Assert.IsTrue(Until(delegate { return !context.Model.IsInstallingDriver; },2000));
                Assert.IsFalse(context.Monitor.Requested);Assert.AreEqual(0,context.Monitor.Resets);
                context.Model.Dispose();context.Completed();
                Assert.IsTrue(Until(delegate { return context.Monitor.Disposals==1; },2000));
                Assert.IsFalse(context.Monitor.Requested);Assert.AreEqual(0,context.Monitor.Resets);Assert.IsFalse(context.Model.CanInstallDriver);
            }
        }
        [Test]
        public void LocalInstallerPathIsForwardedWithoutStartingMonitoring()
        {
            using(var context=new Context(delegate { return 0; }))
            {
                string path=Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR")??TestContext.CurrentContext.WorkDirectory,"chosen-offline.exe");
                Assert.IsTrue(context.Model.BeginDriverInstallation(true,path));Assert.IsTrue(Until(delegate { return !context.Model.IsInstallingDriver; },2000));
                Assert.AreEqual(path,context.SelectedInstallerPath);Assert.AreEqual(1,context.Prepares);Assert.AreEqual(1,context.Executions);Assert.AreEqual(0,context.MonitorCreations);
            }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CancelOrDisposeDuringPreparationPreventsExecution(bool dispose)
        {
            using(var entered=new ManualResetEvent(false))
            using(var context=new Context(delegate { Assert.Fail("Cancelled preparation must never execute.");return 0; },delegate(CancellationToken cancellation)
            { entered.Set();if(!cancellation.WaitHandle.WaitOne(5000))throw new TimeoutException();cancellation.ThrowIfCancellationRequested(); }))
            {
                Assert.IsTrue(context.Model.BeginDriverInstallation(true));Assert.IsTrue(entered.WaitOne(2000));Assert.IsTrue(context.Model.CanCancelDriverPreparation);
                if(dispose)context.Model.Dispose();else Assert.IsTrue(context.Model.CancelDriverPreparation());
                Assert.IsTrue(Until(delegate { return !context.Model.IsInstallingDriver; },2000));Assert.IsFalse(context.Model.CanCancelDriverPreparation);
                Assert.AreEqual("Cancelled",context.Installer.Status);Assert.AreEqual(0,context.Executions);Assert.AreEqual(0,context.Monitor.Resets);
            }
        }
    }
}
#pragma warning restore 1591
