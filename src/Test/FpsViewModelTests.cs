using System;
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
    [Category("Desktop")]
    public sealed class FpsViewModelTests
    {
        private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

        private sealed class Resources : IResourceMonitor
        {
            internal int Samples,Disposals;
            internal ProcessSnapshot AdditionalProcess;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                Interlocked.Increment(ref Samples);
                var snapshot=new ResourceSnapshot();
                if(processes)
                {
                    snapshot.Processes.Add(new ProcessSnapshot { Id=8101,CreationTime=2101,Name="first-game.exe" });
                    snapshot.Processes.Add(new ProcessSnapshot { Id=8102,CreationTime=2102,Name="second-game.exe" });
                    if(AdditionalProcess!=null)snapshot.Processes.Add(AdditionalProcess);
                }
                return snapshot;
            }
            public void Suspend() { }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }
        private sealed class Frames : IFrameRateMonitor
        {
            internal volatile bool Active;
            internal volatile string Error;
            internal volatile FrameRateReading Reading=Available(60,"first-game.exe");
            internal ProcessSnapshot Target;
            internal bool Follow;
            internal int Samples,Disposals,TargetChanges,SampleThread;
            internal ManualResetEvent Entered,Release;
            internal int BlockNext;
            public void SetActive(bool active)
            {
                if(active&&Error=="active")throw new InvalidOperationException("Controlled private activation detail");
                Active=active;
            }
            public void SetTarget(bool followForeground,ProcessSnapshot target)
            {
                if(Error=="target")throw new InvalidOperationException("Controlled private target detail");
                Follow=followForeground;Target=target;Interlocked.Increment(ref TargetChanges);
            }
            public FrameRateReading Sample()
            {
                SampleThread=Thread.CurrentThread.ManagedThreadId;
                FrameRateReading result=Reading;
                Interlocked.Increment(ref Samples);
                if(Interlocked.Exchange(ref BlockNext,0)!=0)
                {
                    Entered.Set();if(!Release.WaitOne(5000))throw new TimeoutException("Controlled FPS sample was not released");
                }
                if(Error=="sample")throw new InvalidOperationException("Controlled private sampling detail");
                return result;
            }
            public void Dispose() { Active=false;Interlocked.Increment(ref Disposals); }
        }
        private sealed class Context : IDisposable
        {
            private readonly MainViewModel _cleaner;
            private readonly HotkeyService _hotkey;
            private readonly NotificationService _notifications;
            private bool _disposed;
            internal readonly Resources Resource=new Resources();
            internal readonly Frames Fps;
            internal readonly MonitoringViewModel Model;
            internal int Creations,ResourceCreations;
            internal string FactoryError;
            internal Context(Frames fps=null,string factoryError=null)
            {
                // Reflection makes the pre-implementation build fail an assertion,
                // rather than failing to compile because the new overload is absent.
                var constructor=typeof(MonitoringViewModel).GetConstructor(Members,null,new[]
                { typeof(MainViewModel),typeof(Dispatcher),typeof(Func<IResourceMonitor>),typeof(Func<IFrameRateMonitor>) },null);
                Assert.IsNotNull(constructor,"The monitoring model must expose an injectable FPS collector without changing existing constructors.");
                Assert.IsTrue((bool)typeof(App).GetProperty("IsTestBuild",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null,null));
                Settings.Reset(true);Settings.UseHotkey=false;
                Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));
                Fps=fps??new Frames();FactoryError=factoryError;
                _hotkey=new HotkeyService();_notifications=new NotificationService(null);
                _cleaner=new MainViewModel(new ComputerService(),_hotkey,_notifications);
                Func<IResourceMonitor> resources=delegate { Interlocked.Increment(ref ResourceCreations);return Resource; };
                Func<IFrameRateMonitor> frames=delegate
                {
                    Interlocked.Increment(ref Creations);
                    if(FactoryError=="throw")throw new InvalidOperationException("Controlled private factory detail");
                    return FactoryError=="null"?null:Fps;
                };
                Model=(MonitoringViewModel)constructor.Invoke(new object[] { _cleaner,Dispatcher.CurrentDispatcher,resources,frames });
                Model.RefreshSeconds=5;
            }
            internal T Get<T>(string property)
            {
                var member=typeof(MonitoringViewModel).GetProperty(property,Members);
                Assert.IsNotNull(member,"Missing FPS presentation property: "+property);return (T)member.GetValue(Model,null);
            }
            internal void Set(string property,object value)
            {
                var member=typeof(MonitoringViewModel).GetProperty(property,Members);
                Assert.IsNotNull(member,"Missing FPS presentation property: "+property);member.SetValue(Model,value,null);
            }
            internal bool UseSelected()
            {
                var method=typeof(MonitoringViewModel).GetMethod("UseSelectedFpsTarget",Members);
                Assert.IsNotNull(method,"Manual FPS target selection must be explicit and validated.");return (bool)method.Invoke(Model,null);
            }
            internal void ClearTarget()
            {
                var method=typeof(MonitoringViewModel).GetMethod("ClearFpsTarget",Members);
                Assert.IsNotNull(method);method.Invoke(Model,null);
            }
            internal void Processes(int expectedCount=2)
            {
                Model.SetViewState(true,true);
                Assert.IsTrue(Until(delegate { return Model.ProcessCount==expectedCount; }),"Controlled process snapshots must be published.");
            }
            internal void Select(int index) { Model.SelectedProcess=(ProcessRow)Model.ProcessesView.GetItemAt(index); }
            internal void SelectId(int id)
            {
                foreach(ProcessRow row in Model.ProcessesView)
                    if(row.Id==id) { Model.SelectedProcess=row;return; }
                Assert.Fail("The controlled process row must exist before selection.");
            }
            internal bool HasPendingApply
            { get { return (int)typeof(MonitoringViewModel).GetField("_pending",Members).GetValue(Model)!=0; } }
            internal bool WorkerIdle
            {
                get
                {
                    object pump=typeof(MonitoringViewModel).GetField("_pump",Members).GetValue(Model);
                    object sync=pump.GetType().GetField("_sync",Members).GetValue(pump);
                    lock(sync)return !(bool)pump.GetType().GetField("_working",Members).GetValue(pump);
                }
            }
            internal void Stale()
            {
                object presentation=typeof(MonitoringViewModel).GetField("_processes",Members).GetValue(Model);
                presentation.GetType().GetMethod("Apply",Members).Invoke(presentation,new object[] { null,"Controlled stale process data" });
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                if(Fps.Release!=null)Fps.Release.Set();
                Model.Dispose();_cleaner.Dispose();_hotkey.Dispose();_notifications.Dispose();
                if(ResourceCreations==0)Resource.Dispose();else Assert.IsTrue(Until(delegate { return Resource.Disposals==1; },3000));
                Settings.Reset(true);GC.SuppressFinalize(this);
            }
        }
        private static FrameRateReading Available(double value,string target)
        { return new FrameRateReading(MetricValue.Available(value,"Controlled successful DirectX presents"),target,"FpsAvailable"); }
        private static bool Until(Func<bool> ready,int milliseconds=2500)
        {
            var watch=Stopwatch.StartNew();
            while(!ready()&&watch.ElapsedMilliseconds<milliseconds) { Drain();Thread.Sleep(3); }
            return ready();
        }
        private static void Drain()
        {
            var frame=new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { frame.Continue=false; }));
            Dispatcher.PushFrame(frame);
        }
        private static bool WithoutDispatcher(Func<bool> ready,int milliseconds=2000)
        {
            var watch=Stopwatch.StartNew();
            while(!ready()&&watch.ElapsedMilliseconds<milliseconds)Thread.Sleep(3);
            return ready();
        }

        [Test]
        public void FpsIsLazyAndOptInEvenWhileOtherResourcesAreVisible()
        {
            using(var context=new Context())
            {
                Assert.IsFalse(context.Get<bool>("ShowFps"));Assert.AreEqual("—",context.Get<string>("FpsText"));
                context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Resource.Samples>0; }));
                Assert.AreEqual(0,context.Creations);Assert.AreEqual(0,context.Fps.Samples);
                context.Set("FpsTargetMode",1);context.Set("ShowFps",true);Drain();
                Assert.AreEqual(0,context.Creations,"An empty manual target does not allocate a collector.");
                StringAssert.Contains(context.Model.Labels["FpsChooseTarget"],context.Get<string>("FpsDetail"));
            }
        }

        [Test]
        public void FpsRunsOnlyForOverviewOrOverlayAndStopsOnDisable()
        {
            using(var context=new Context())
            {
                context.Set("ShowFps",true);Assert.AreEqual(0,context.Creations);
                context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                Assert.IsTrue(context.Fps.Active);Assert.AreEqual(1,context.Creations);
                Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId,context.Fps.SampleThread,"FPS reads use the existing background worker, never the WPF thread.");
                context.Model.SetViewState(true,true);Assert.IsFalse(context.Fps.Active);Assert.AreEqual("—",context.Get<string>("FpsText"));
                context.Model.SetViewState(false,true);Assert.IsFalse(context.Fps.Active);Assert.AreEqual(0,context.Model.ProcessCount);
                context.Set("OverlayEnabled",true);Assert.IsTrue(context.Fps.Active);
                Assert.IsTrue(Until(delegate { return context.Get<string>("OverlayFpsText")=="60"; }));
                context.Set("OverlayEnabled",false);Assert.IsFalse(context.Fps.Active);Assert.AreEqual("—",context.Get<string>("FpsText"));
                context.Model.SetViewState(true,false);Assert.IsTrue(context.Fps.Active);
                context.Set("ShowFps",false);Assert.IsFalse(context.Fps.Active);Assert.AreEqual("—",context.Get<string>("OverlayFpsText"));
                StringAssert.Contains(context.Model.Labels["FpsDisabled"],context.Get<string>("FpsDetail"));Assert.AreEqual(1,context.Creations);
            }
        }

        [Test]
        public void CurrentFpsSampleReplacesAQueuedStalePublicationWithoutResampling()
        {
            using(var context=new Context())
            {
                context.Set("ShowFps",true);context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                context.Fps.Reading=Available(61,"first-game.exe");context.Model.RefreshNow();
                // Keep the old UI publication queued while the background worker settles.
                Assert.IsTrue(WithoutDispatcher(delegate { return context.HasPendingApply&&context.WorkerIdle; }));
                int samples=context.Fps.Samples;
                context.Model.SetViewState(false,false);Assert.AreEqual("—",context.Get<string>("FpsText"));
                context.Fps.Reading=Available(72,"first-game.exe");context.Set("OverlayEnabled",true);
                Assert.IsTrue(WithoutDispatcher(delegate { return context.Fps.Samples>samples&&context.WorkerIdle; }));
                Assert.IsTrue(context.HasPendingApply,"Current data shares the one queued publication slot.");
                int collected=context.Fps.Samples;Drain();
                Assert.AreEqual("72",context.Get<string>("OverlayFpsText"),"The collected current reading must replace the queued stale one.");
                Assert.AreEqual(collected,context.Fps.Samples,"Publishing current data must not need another hardware or FPS sample.");
                Assert.IsFalse(context.HasPendingApply);
            }
        }

        [Test]
        public void ManualSelectionCopiesIdentityAndRejectsStaleRowsWithoutEnablingCapture()
        {
            using(var context=new Context())
            {
                context.Processes();Assert.IsFalse(context.UseSelected());context.Select(0);
                Assert.IsTrue(context.Get<bool>("CanUseSelectedFpsTarget"));
                ProcessSnapshot source=context.Model.SelectedProcess.Snapshot;
                Assert.IsTrue(context.UseSelected());Assert.IsFalse(context.Get<bool>("ShowFps"));Assert.AreEqual(1,context.Get<int>("FpsTargetMode"));
                string first=context.Get<string>("FpsTargetText");StringAssert.Contains("first-game.exe",first);StringAssert.Contains("8101",first);
                source.Name="mutated.exe";source.CreationTime=9999;source.Id=8999;
                context.Set("OverlayEnabled",true);context.Set("ShowFps",true);
                Assert.AreEqual(8101,context.Fps.Target.Id);Assert.AreEqual(2101,context.Fps.Target.CreationTime);Assert.AreEqual("first-game.exe",context.Fps.Target.Name);
                Assert.AreNotSame(source,context.Fps.Target);Assert.IsFalse(context.Fps.Follow);
                context.Select(1);Assert.AreEqual(first,context.Get<string>("FpsTargetText"),"Highlighting another row is not a target-selection action.");
                context.Stale();context.Select(1);Assert.IsFalse(context.Get<bool>("CanUseSelectedFpsTarget"));Assert.IsFalse(context.UseSelected());
                Assert.AreEqual(first,context.Get<string>("FpsTargetText"));
            }
        }

        private static ProcessSnapshot SelfTarget()
        { return new ProcessSnapshot { Id=(int)MonitoringNativeMethods.GetCurrentProcessId(),CreationTime=2103,Name="WinMemoryCleaner.Community.exe" }; }

        [Test]
        public void SelfProcessIsNotAnEligibleManualFpsTarget()
        {
            using(var context=new Context())
            {
                context.Resource.AdditionalProcess=SelfTarget();context.Processes(3);
                context.SelectId(context.Resource.AdditionalProcess.Id);
                Assert.IsTrue(context.Model.CanActOnSelectedProcess,"Self remains eligible for general process operations.");
                Assert.IsTrue(context.Model.IsCurrentProcessTarget(context.Model.SelectedProcess.Snapshot));
                Assert.IsFalse(context.Get<bool>("CanUseSelectedFpsTarget"),"The cleaner cannot be its own FPS target.");
                Assert.IsFalse(context.UseSelected());Assert.IsFalse(context.Get<bool>("HasFpsTarget"));
                Assert.AreEqual(0,context.Get<int>("FpsTargetMode"));Assert.IsFalse(context.Get<bool>("ShowFps"));
                Assert.AreEqual(0,context.Creations);Assert.IsNull(context.Fps.Target);
            }
        }

        [Test]
        public void RejectedSelfSelectionPreservesTheExistingManualFpsTarget()
        {
            using(var context=new Context())
            {
                context.Resource.AdditionalProcess=SelfTarget();context.Processes(3);context.SelectId(8101);
                Assert.IsTrue(context.UseSelected());string targetText=context.Get<string>("FpsTargetText");
                int eligibilityChanges=0;
                context.Model.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs args)
                { if(args.PropertyName=="CanUseSelectedFpsTarget")eligibilityChanges++; };
                context.SelectId(context.Resource.AdditionalProcess.Id);
                Assert.IsTrue(context.Model.CanActOnSelectedProcess);Assert.Greater(eligibilityChanges,0);
                Assert.IsFalse(context.UseSelected(),"Selecting self must be rejected without replacing the prior target.");
                Assert.IsFalse(context.Get<bool>("CanUseSelectedFpsTarget"));Assert.IsTrue(context.Get<bool>("HasFpsTarget"));
                Assert.AreEqual(targetText,context.Get<string>("FpsTargetText"));Assert.AreEqual(1,context.Get<int>("FpsTargetMode"));
                Assert.IsFalse(context.Get<bool>("ShowFps"));Assert.AreEqual(0,context.Creations);
                context.Set("OverlayEnabled",true);context.Set("ShowFps",true);
                Assert.AreEqual(8101,context.Fps.Target.Id);Assert.AreEqual(2101,context.Fps.Target.CreationTime);
                Assert.AreEqual("first-game.exe",context.Fps.Target.Name);Assert.IsFalse(context.Fps.Follow);
            }
        }

        [Test]
        public void TargetAndModeChangesInvalidateTheVisibleRateImmediately()
        {
            using(var context=new Context())
            {
                context.Processes();context.Select(0);Assert.IsTrue(context.UseSelected());
                context.Set("OverlayEnabled",true);context.Set("ShowFps",true);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                context.Select(1);Assert.IsTrue(context.UseSelected());Assert.AreEqual("—",context.Get<string>("FpsText"));
                Assert.AreEqual(8102,context.Fps.Target.Id);Assert.IsTrue(context.Get<bool>("HasFpsTarget"));
                context.ClearTarget();Assert.IsFalse(context.Get<bool>("HasFpsTarget"));Assert.AreEqual("—",context.Get<string>("OverlayFpsText"));
                Assert.IsFalse(context.Fps.Active);StringAssert.Contains(context.Model.Labels["FpsChooseTarget"],context.Get<string>("FpsDetail"));
                context.Set("FpsTargetMode",0);Assert.IsTrue(context.Fps.Follow);Assert.IsNull(context.Fps.Target);
                Assert.AreEqual("—",context.Get<string>("FpsText"));context.Set("FpsTargetMode",-1);Assert.AreEqual(0,context.Get<int>("FpsTargetMode"));
            }
        }

        [Test]
        public void LateFpsSampleCannotOverwriteARapidlySelectedNewTarget()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            using(var context=new Context(new Frames { Entered=entered,Release=release }))
            {
                context.Processes();context.Select(0);Assert.IsTrue(context.UseSelected());
                context.Set("OverlayEnabled",true);context.Set("ShowFps",true);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                int staleNotifications=0;
                context.Model.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs args)
                { if(args.PropertyName=="FpsText"&&context.Get<string>("FpsText")=="99")staleNotifications++; };
                context.Fps.Reading=Available(99,"first-game.exe");context.Fps.BlockNext=1;context.Model.RefreshNow();
                Assert.IsTrue(entered.WaitOne(2000));
                var watch=Stopwatch.StartNew();context.Select(1);Assert.IsTrue(context.UseSelected());watch.Stop();
                Assert.Less(watch.ElapsedMilliseconds,250,"Target selection cannot wait for a blocked native consumer.");Assert.AreEqual("—",context.Get<string>("FpsText"));
                context.Fps.Reading=new FrameRateReading(MetricValue.Missing(MetricStatus.WarmingUp,"Controlled new target"),"second-game.exe","FpsWarmingUp");
                int samples=context.Fps.Samples;release.Set();Assert.IsTrue(Until(delegate { return context.Fps.Samples>samples; }));Drain();
                Assert.AreEqual(0,staleNotifications);Assert.AreEqual("—",context.Get<string>("FpsText"));StringAssert.Contains("second-game.exe",context.Get<string>("FpsTargetText"));
            }
        }

        [Test]
        public void DisposeIsFastAndRejectsTheBlockedCollectorsLateReading()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            using(var context=new Context(new Frames { Entered=entered,Release=release }))
            {
                context.Set("ShowFps",true);context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                context.Fps.Reading=Available(99,"first-game.exe");context.Fps.BlockNext=1;context.Model.RefreshNow();Assert.IsTrue(entered.WaitOne(2000));
                int changes=0;context.Model.PropertyChanged+=delegate { changes++; };
                var watch=Stopwatch.StartNew();context.Model.Dispose();watch.Stop();
                Assert.Less(watch.ElapsedMilliseconds,250);Assert.AreEqual(1,context.Fps.Disposals);Assert.IsFalse(context.Fps.Active);
                int stoppedChanges=changes;context.Model.Dispose();release.Set();Assert.IsTrue(Until(delegate { return context.Resource.Disposals==1; },3000));Drain();
                Assert.AreEqual(stoppedChanges,changes,"Disposed monitoring cannot publish late readings.");Assert.AreEqual(1,context.Fps.Disposals);
            }
        }

        [Test]
        public void SearchSortAndTranslationDoNotRequestAdditionalFpsOrHardwareSamples()
        {
            using(var context=new Context())
            {
                context.Processes();context.Select(0);Assert.IsTrue(context.UseSelected());context.Set("OverlayEnabled",true);context.Set("ShowFps",true);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsText")=="60"; }));
                int frames=context.Fps.Samples,resources=context.Resource.Samples,targets=context.Fps.TargetChanges;
                context.Model.SearchQuery="first";context.Model.ClearSearch();context.Model.Sort("Name");
                Localizer.Language=new Language(CultureInfo.GetCultureInfo("zh-Hans"));Drain();
                Assert.AreEqual(frames,context.Fps.Samples);Assert.AreEqual(resources,context.Resource.Samples);Assert.AreEqual(targets,context.Fps.TargetChanges);
                Assert.AreEqual(1,context.Creations);Assert.AreEqual(1,context.Get<int>("FpsTargetMode"));StringAssert.Contains("first-game.exe",context.Get<string>("FpsTargetText"));
                StringAssert.Contains(context.Model.Labels["FpsAvailable"],context.Get<string>("FpsDetail"));
            }
        }

        [TestCase("factory")]
        [TestCase("null")]
        [TestCase("active")]
        [TestCase("target")]
        [TestCase("sample")]
        public void CollectorFailuresAreContainedAndNeverBecomeFakeFps(string failure)
        {
            var frames=new Frames { Error=failure };
            using(var context=new Context(frames,failure=="factory"?"throw":failure=="null"?"null":null))
            {
                context.Set("ShowFps",true);context.Model.SetViewState(true,false);
                Assert.IsTrue(Until(delegate { return context.Get<string>("FpsDetail").Contains(context.Model.Labels["FpsError"]); }));
                Assert.AreEqual("—",context.Get<string>("FpsText"));Assert.AreEqual("—",context.Get<string>("OverlayFpsText"));
                StringAssert.DoesNotContain("private",context.Get<string>("FpsDetail"));
                Assert.IsTrue(Until(delegate { return context.Resource.Samples>0; }),"A failed FPS source must not stop existing resource sampling.");
            }
        }
    }
}
#pragma warning restore 1591
