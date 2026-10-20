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
            internal volatile bool Requested;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                Interlocked.Increment(ref Samples);
                return new ResourceSnapshot
                {
                    CpuTemperature=temperatures&&Requested?MetricValue.Available(54,"Fixture CPU package"):MetricValue.Missing(MetricStatus.Disabled,"not requested"),
                    GpuTemperature=MetricValue.Missing(MetricStatus.Unsupported,"Fixture GPU core is unavailable"),
                    SensorBackendStatus="Running",SensorDriverStatus="KeepExistingDriver",SensorDiagnostic="Fixture backend diagnostic"
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
            private bool _disposed;
            internal Context(Func<string,int> execute)
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
                            delegate { Interlocked.Increment(ref Prepares);Directory.CreateDirectory(_root);return new SensorPayloadLease(_root); },
                            delegate { return true; },
                            delegate(string path) { Interlocked.Increment(ref Executions);return execute(path); },completed);
                        return Installer;
                    });
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                Model.Dispose();_cleaner.Dispose();_hotkey.Dispose();_notifications.Dispose();
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
                    Assert.AreEqual("Install temperature driver…",context.Model.Labels["InstallSensorDriver"]);
                }
                context.Model.SetViewState(false,false);Assert.IsFalse(context.Monitor.Requested);
                StringAssert.Contains("Not requested",context.Model.TemperatureStatusText);Assert.AreEqual("—",context.Model.OverlayCpuTemperatureText);
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
    }
}
#pragma warning restore 1591
