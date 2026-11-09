using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    public sealed class MonitoringRefreshTests
    {
        private sealed class Monitor : IResourceMonitor
        {
            internal volatile ResourceSnapshot Next;
            internal int Disposals;
            public ResourceSnapshot Sample(bool processes, bool temperatures) { return Next; }
            public void Suspend() { }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }

        private sealed class Context : IDisposable
        {
            private readonly MainViewModel _cleaner;
            private readonly HotkeyService _hotkey;
            private readonly NotificationService _notifications;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Ownership transfers to Model's MonitoringSession. Context.Dispose waits for exactly one monitor disposal; disposing this observation reference would double-dispose and invalidate that lifecycle assertion.")]
            private readonly Monitor _monitor;
            internal readonly MonitoringViewModel Model;
            internal MainViewModel Cleaner { get { return _cleaner; } }
            internal readonly List<string> Changes = new List<string>();

            internal Context()
            {
                Assert.IsTrue(App.IsTestBuild);
                Settings.Reset(true);
                Settings.UseHotkey = false;
                Localizer.Language = new Language(CultureInfo.GetCultureInfo("en"));
                _hotkey = new HotkeyService();
                _notifications = new NotificationService(null);
                _cleaner = new MainViewModel(new ComputerService(), _hotkey, _notifications);
                _monitor = new Monitor { Next = Snapshot(12.01) };
                Model = new MonitoringViewModel(_cleaner, Dispatcher.CurrentDispatcher, delegate { return _monitor; });
                Model.RefreshSeconds = 5;
                Model.PropertyChanged += delegate(object sender, System.ComponentModel.PropertyChangedEventArgs args) { Changes.Add(args.PropertyName); };
                Model.SetViewState(true, false);
                WaitFor(_monitor.Next);
                Changes.Clear();
            }

            internal void Apply(ResourceSnapshot snapshot)
            {
                _monitor.Next = snapshot;
                Model.RefreshNow();
                WaitFor(snapshot);
            }

            private void WaitFor(ResourceSnapshot snapshot)
            {
                var last = typeof(MonitoringViewModel).GetField("_last", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsTrue(Until(delegate { return ReferenceEquals(last.GetValue(Model), snapshot); }), "Snapshot was not published on the UI thread.");
            }

            public void Dispose()
            {
                Model.Dispose();
                _cleaner.Dispose();
                _hotkey.Dispose();
                _notifications.Dispose();
                Assert.IsTrue(Until(delegate { return _monitor.Disposals == 1; }));
                Settings.Reset(true);
                GC.SuppressFinalize(this);
            }
        }

        private static bool Until(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            while (!ready() && watch.ElapsedMilliseconds < 3000)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(2);
            }
            return ready();
        }

        private static ResourceSnapshot Snapshot(double cpu)
        {
            return new ResourceSnapshot
            {
                CpuUsage = MetricValue.Available(cpu, "CPU fixture"),
                GpuUsage = MetricValue.Available(20.0, "GPU fixture"),
                CpuTemperature = MetricValue.Available(50.0, "CPU temperature fixture"),
                GpuTemperature = MetricValue.Available(45.0, "GPU temperature fixture"),
                GpuName = "Fixture GPU",
                SensorBackendStatus = "Running",
                SensorDriverStatus = "NotChecked",
                SensorDiagnostic = "Fixture diagnostic"
            };
        }

        [Test]
        public void RepeatedEquivalentSamplesDoNotNotifyUnchangedBindings()
        {
            using (var context = new Context())
            {
                for (int i = 0; i < 5; i++) context.Apply(Snapshot(12.01));
                Assert.IsEmpty(context.Changes, "Stable display values must not invalidate all WPF bindings each tick.");
                Assert.AreEqual("12.0%", context.Model.CpuText);
                Assert.AreEqual("12%", context.Model.OverlayCpuText);
            }
        }

        [Test]
        public void NotificationsRespectTheDifferentMainAndOverlayPrecisions()
        {
            using (var context = new Context())
            {
                context.Apply(Snapshot(12.04));
                Assert.IsEmpty(context.Changes);
                context.Apply(Snapshot(12.06));
                CollectionAssert.AreEquivalent(new[] { "CpuText" }, context.Changes);
                context.Changes.Clear();
                context.Apply(Snapshot(12.6));
                CollectionAssert.AreEquivalent(new[] { "CpuText", "OverlayCpuText" }, context.Changes);
                Assert.AreEqual("13%", context.Model.OverlayCpuText);
            }
        }

        [Test]
        public void SourceOnlyChangesDoNotInvalidateNumericText()
        {
            using (var context = new Context())
            {
                var snapshot = Snapshot(12.01);
                snapshot.CpuUsage = MetricValue.Available(12.01, "Replacement provider");
                context.Apply(snapshot);
                CollectionAssert.AreEquivalent(new[] { "CpuDetail" }, context.Changes);
                Assert.AreEqual("Replacement provider", context.Model.CpuDetail);
            }
        }

        [Test]
        public void FailureAndRecoveryRefreshTemperatureDetailsWithoutInventingZero()
        {
            using (var context = new Context())
            {
                var snapshot = Snapshot(12.01);
                snapshot.CpuTemperature = MetricValue.Missing(MetricStatus.Error, "Controlled sensor error");
                context.Apply(snapshot);
                CollectionAssert.AreEquivalent(new[] { "CpuTemperatureText", "OverlayCpuTemperatureText", "CpuTemperatureDetail", "TemperatureStatusDetail" }, context.Changes);
                Assert.AreEqual("—", context.Model.OverlayCpuTemperatureText);
                StringAssert.Contains("Controlled sensor error", context.Model.TemperatureStatusDetail);
                context.Changes.Clear();
                context.Apply(Snapshot(12.01));
                CollectionAssert.AreEquivalent(new[] { "CpuTemperatureText", "OverlayCpuTemperatureText", "CpuTemperatureDetail", "TemperatureStatusDetail" }, context.Changes);
                Assert.AreEqual("50°C", context.Model.OverlayCpuTemperatureText);
            }
        }

        [Test]
        public void LanguageAndDemandChangesRefreshCachedReadings()
        {
            using (var context = new Context())
            {
                var snapshot = Snapshot(12.01);
                snapshot.CpuUsage = MetricValue.Missing(MetricStatus.Unsupported, "No CPU data");
                context.Apply(snapshot);
                string english = context.Model.CpuText;
                context.Changes.Clear();
                Localizer.Language = new Language(CultureInfo.GetCultureInfo("zh-Hans"));
                Assert.AreNotEqual(english, context.Model.CpuText);
                CollectionAssert.Contains(context.Changes, "CpuText");
                CollectionAssert.DoesNotContain(context.Changes, "GpuText");
                context.Model.SetViewState(false, false);
                Assert.AreEqual("—", context.Model.OverlayCpuTemperatureText);
                Assert.AreEqual("—", context.Model.OverlayGpuTemperatureText);
            }
        }

        [Test]
        public void CachedMetricGettersDoNotAllocateNewFormattedStrings()
        {
            using (var context = new Context())
            {
                Assert.AreSame(context.Model.CpuText, context.Model.CpuText);
                Assert.AreSame(context.Model.OverlayCpuText, context.Model.OverlayCpuText);
                Assert.AreSame(context.Model.CpuTemperatureText, context.Model.CpuTemperatureText);
            }
        }

        [Test]
        public void RefreshChoicesAreSharedAndReadOnly()
        {
            using (var context = new Context())
            {
                Assert.AreSame(context.Model.RefreshChoices, context.Model.RefreshChoices);
                Assert.IsTrue(context.Model.RefreshChoices.IsReadOnly);
                CollectionAssert.AreEqual(new[] { 1, 2, 5 }, context.Model.RefreshChoices);
                Assert.Throws<NotSupportedException>(delegate { context.Model.RefreshChoices[0] = 10; });
            }
        }
        private static void MemoryDisplayAction(MainViewModel cleaner,string method,object value)
        {
            var action=typeof(MainViewModel).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.IsNotNull(action,"Memory display publication must be independently demand-driven: "+method);
            action.Invoke(cleaner,new[]{value});
        }

        private static Memory MemoryWithUsage(int percentage)
        {
            var memory=new Memory();memory.Physical.Used.Percentage=percentage;return memory;
        }

        [Test]
        public void HiddenMemoryPublicationKeepsTheLatestSnapshotAndCoalescesDisplayNotifications()
        {
            using(var context=new Context())
            {
                var cleaner=context.Cleaner;var changes=new List<string>();
                cleaner.Computer.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e) { changes.Add(e.PropertyName); };
                cleaner.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e)
                { if(e.PropertyName=="PhysicalMemoryHeader"||e.PropertyName=="VirtualMemoryHeader")changes.Add(e.PropertyName); };
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",false);
                Memory latest=null;
                for(int i=40;i<60;i++) { latest=MemoryWithUsage(i);MemoryDisplayAction(cleaner,"PublishMemory",latest); }
                Assert.IsEmpty(changes,"Hidden bindings must not be invalidated on each memory sample.");
                Assert.AreSame(latest,cleaner.Computer.Memory,"Tray and automatic-cleaning decisions must still see the latest snapshot.");
                Assert.AreEqual("59%",context.Model.MemoryText,"The independent overlay reads the current snapshot, not the deferred main display.");
                Assert.AreEqual(5,cleaner.MemoryRefreshSeconds,"Display deferral must not change the source cadence.");
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",true);
                CollectionAssert.AreEquivalent(new[]{"Memory","PhysicalMemoryHeader","VirtualMemoryHeader"},changes);
                changes.Clear();MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",true);
                Assert.IsEmpty(changes,"Repeated visible-state notifications must not replay an already published snapshot.");
            }
        }

        [Test]
        public void BackgroundMemoryReplacementsArePublishedOnceWhenTheDisplayResumes()
        {
            using(var context=new Context())
            {
                var cleaner=context.Cleaner;int notifications=0,notificationThread=0;
                cleaner.Computer.PropertyChanged+=delegate { notificationThread=Thread.CurrentThread.ManagedThreadId;Interlocked.Increment(ref notifications); };
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",false);
                Memory latest=null;Exception failure=null;
                var worker=new Thread(new ThreadStart(delegate
                {
                    try { for(int i=0;i<128;i++) { latest=MemoryWithUsage(i%100);cleaner.Computer.Memory=latest; } }
                    catch(Exception ex) { failure=ex; }
                })) { IsBackground=true };
                worker.Start();Assert.IsTrue(worker.Join(3000));Assert.IsNull(failure);
                Assert.AreEqual(0,notifications);Assert.AreSame(latest,cleaner.Computer.Memory);
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",true);
                Assert.AreEqual(1,notifications);Assert.AreEqual(Thread.CurrentThread.ManagedThreadId,notificationThread);
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",false);
                MemoryDisplayAction(cleaner,"SetMemoryDisplayActive",true);
                Assert.AreEqual(1,notifications,"Resuming without a new memory sample must not replay Memory.");
            }
        }

        [Test]
        public void VisibleMemoryPublicationStillUpdatesBothHeadersAndTheMemoryBindings()
        {
            using(var context=new Context())
            {
                var cleaner=context.Cleaner;var changes=new List<string>();
                cleaner.Computer.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e) { changes.Add(e.PropertyName); };
                cleaner.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e)
                { if(e.PropertyName=="PhysicalMemoryHeader"||e.PropertyName=="VirtualMemoryHeader")changes.Add(e.PropertyName); };
                var latest=MemoryWithUsage(73);MemoryDisplayAction(cleaner,"PublishMemory",latest);
                Assert.AreSame(latest,cleaner.Computer.Memory);
                CollectionAssert.AreEquivalent(new[]{"Memory","PhysicalMemoryHeader","VirtualMemoryHeader"},changes);
                Assert.AreEqual("73%",context.Model.MemoryText);
            }
        }

    }
}
#pragma warning restore 1591
