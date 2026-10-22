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
    public sealed class MonitoringProcessTests
    {
        private sealed class Monitor : IResourceMonitor
        {
            internal volatile string Mode="ready";
            internal int Samples,Disposals;
            internal ManualResetEvent Entered,Release,Returned,CurrentEntered,CurrentRelease;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                int sequence=Interlocked.Increment(ref Samples);string mode=Mode;
                if(sequence==1&&Entered!=null)
                {
                    Entered.Set();if(!Release.WaitOne(5000))throw new TimeoutException("Controlled old generation was not released");
                }
                if(sequence==2&&CurrentEntered!=null)
                {
                    CurrentEntered.Set();if(!CurrentRelease.WaitOne(5000))throw new TimeoutException("Controlled current generation was not released");
                }
                if(Returned!=null)Returned.Set();
                if(mode=="exception")throw new InvalidOperationException("controlled whole-list failure");
                if(mode=="null")return null;
                var snapshot=new ResourceSnapshot();
                if(processes&&mode!="empty")
                {
                    for(int i=0;i<3;i++)snapshot.Processes.Add(new ProcessSnapshot
                    { Id=7001+i,CreationTime=1001+i,Name=mode=="old"?"old.exe":"game"+i.ToString(CultureInfo.InvariantCulture)+".exe" });
                }
                return snapshot;
            }
            public void Suspend() { }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }
        private sealed class Context : IDisposable
        {
            private readonly MainViewModel _cleaner;
            private readonly HotkeyService _hotkey;
            private readonly NotificationService _notifications;
            internal readonly Monitor Monitor;
            internal readonly MonitoringViewModel Model;
            private int _creations;
            private bool _disposed;
            internal Context(Monitor monitor)
            {
                Assert.IsTrue(App.IsTestBuild);Settings.Reset(true);Settings.UseHotkey=false;
                Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Monitor=monitor;
                _hotkey=new HotkeyService();_notifications=new NotificationService(null);
                _cleaner=new MainViewModel(new ComputerService(),_hotkey,_notifications);
                Model=new MonitoringViewModel(_cleaner,Dispatcher.CurrentDispatcher,delegate { Interlocked.Increment(ref _creations);return Monitor; });
                Model.RefreshSeconds=5;
            }
            internal object Get(string name)
            {
                var property=typeof(MonitoringViewModel).GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                Assert.IsNotNull(property,name+" must forward independent process presentation");return property.GetValue(Model,null);
            }
            internal void ClearSearch()
            {
                var method=typeof(MonitoringViewModel).GetMethod("ClearSearch");Assert.IsNotNull(method);method.Invoke(Model,null);
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                if(Monitor.Release!=null)Monitor.Release.Set();
                if(Monitor.CurrentRelease!=null)Monitor.CurrentRelease.Set();
                Model.Dispose();_cleaner.Dispose();_hotkey.Dispose();_notifications.Dispose();
                if(_creations==0)Monitor.Dispose();else Assert.IsTrue(Until(delegate { return Monitor.Disposals==1; },3000));
                Settings.Reset(true);GC.SuppressFinalize(this);
            }
        }
        private static bool Until(Func<bool> ready,int milliseconds=2500)
        {
            var watch=Stopwatch.StartNew();
            while(!ready()&&watch.ElapsedMilliseconds<milliseconds)
            {
                var frame=new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { frame.Continue=false; }));
                Dispatcher.PushFrame(frame);Thread.Sleep(3);
            }
            return ready();
        }
        [TestCase("exception")]
        [TestCase("null")]
        public void WholeListFailureClearsSelectionAndUnavailableMetricsRemainSuccessful(string mode)
        {
            using(var context=new Context(new Monitor()))
            {
                context.Get("CanActOnSelectedProcess");context.Model.SetViewState(true,true);
                Assert.IsTrue(Until(delegate { return context.Model.ProcessCount==3; }));
                context.Model.SelectedProcess=(ProcessRow)context.Model.ProcessesView.GetItemAt(0);Assert.IsTrue((bool)context.Get("CanActOnSelectedProcess"));
                context.Monitor.Mode=mode;context.Model.RefreshNow();
                Assert.IsTrue(Until(delegate { return ((string)context.Get("ProcessStatusText")).Contains("last successful"); }));
                Assert.IsNull(context.Model.SelectedProcess);Assert.IsFalse((bool)context.Get("CanActOnSelectedProcess"));Assert.AreEqual(3,context.Model.ProcessesView.Count);
                if(mode=="exception")StringAssert.Contains("controlled whole-list failure",(string)context.Get("ProcessStatusDetail"));
                context.Model.SelectedProcess=(ProcessRow)context.Model.ProcessesView.GetItemAt(0);
                context.Monitor.Mode="ready";context.Model.RefreshNow();
                Assert.IsTrue(Until(delegate { return ((string)context.Get("ProcessStatusText")).Length==0; }));Assert.IsNull(context.Model.SelectedProcess);
                context.Model.SelectedProcess=(ProcessRow)context.Model.ProcessesView.GetItemAt(0);Assert.IsTrue((bool)context.Get("CanActOnSelectedProcess"));
            }
        }
        [Test]
        public void FailureBeforeFirstSuccessKeepsUnknownCountAndSuccessfulEmptyIsDistinct()
        {
            using(var context=new Context(new Monitor { Mode="null" }))
            {
                Assert.AreEqual("—",context.Model.ProcessCountText);context.Get("HasProcessStatus");context.Model.SetViewState(true,true);
                Assert.IsTrue(Until(delegate { return ((string)context.Get("ProcessStatusText")).Contains("Could not read"); }));Assert.AreEqual("—",context.Model.ProcessCountText);
                context.Monitor.Mode="empty";context.Model.RefreshNow();
                Assert.IsTrue(Until(delegate { return (string)context.Get("ProcessStatusText")=="No processes available"; }));
                Assert.AreEqual("Matches: 0 / Total: 0",context.Model.ProcessCountText);
            }
        }
        [Test]
        public void SearchSortAndTranslationNeverRequestHardwareSampling()
        {
            using(var context=new Context(new Monitor()))
            {
                context.Get("CanClearSearch");context.Model.SetViewState(true,true);Assert.IsTrue(Until(delegate { return context.Model.ProcessCount==3; }));
                int samples=context.Monitor.Samples;
                for(int i=0;i<20;i++)context.Model.SearchQuery=i==19?"7002":i.ToString(CultureInfo.InvariantCulture);
                Assert.IsTrue(Until(delegate { return context.Model.ProcessesView.Count==1; }));Assert.AreEqual(7002,((ProcessRow)context.Model.ProcessesView.GetItemAt(0)).Id);
                context.Model.Sort("Name");Localizer.Language=new Language(CultureInfo.GetCultureInfo("de"));
                Assert.AreEqual(samples,context.Monitor.Samples);StringAssert.Contains("Matches: 1 / Total: 3",context.Model.ProcessCountText);
                context.ClearSearch();Assert.AreEqual(3,context.Model.ProcessesView.Count);Assert.AreEqual(samples,context.Monitor.Samples);
                context.Model.SearchQuery="game";context.Model.SetViewState(false,true);Assert.AreEqual(0,context.Model.ProcessCount);
                Assert.IsFalse((bool)context.Get("CanActOnSelectedProcess"));Assert.AreEqual("—",context.Model.ProcessCountText);
            }
        }
        [Test]
        public void OldSamplingGenerationCannotOverwriteTheReenteredProcessSession()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))using(var returned=new ManualResetEvent(false))
            using(var currentEntered=new ManualResetEvent(false))using(var currentRelease=new ManualResetEvent(false))
            using(var context=new Context(new Monitor { Mode="old",Entered=entered,Release=release,Returned=returned,CurrentEntered=currentEntered,CurrentRelease=currentRelease }))
            {
                context.Get("ProcessStatusText");context.Model.SetViewState(true,true);Assert.IsTrue(entered.WaitOne(2000));
                context.Model.SetViewState(false,true);context.Monitor.Mode="ready";context.Model.SetViewState(true,true);release.Set();
                Assert.IsTrue(Until(delegate { return currentEntered.WaitOne(0); }));Assert.IsTrue(returned.WaitOne(0));
                Assert.AreEqual(0,context.Model.ProcessCount);Assert.AreEqual("Loading processes…",context.Get("ProcessStatusText"));
                currentRelease.Set();Assert.IsTrue(Until(delegate { return context.Model.ProcessCount==3; }));
                foreach(ProcessRow row in context.Model.ProcessesView)StringAssert.StartsWith("game",row.Name);
            }
        }
    }
}
#pragma warning restore 1591
