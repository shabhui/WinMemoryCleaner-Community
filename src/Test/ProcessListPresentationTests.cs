using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Data;
using System.Windows.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    public sealed class ProcessListPresentationTests
    {
        private sealed class Context : IDisposable
        {
            internal readonly object Model;
            internal readonly ListCollectionView View;
            internal int Resets;
            private bool _disposed;
            internal Context()
            {
                var type=Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.ProcessListPresentation");
                Assert.IsNotNull(type,"ProcessListPresentation must coalesce queries and retain only current visible identities");
                Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));
                Model=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    new object[]{Dispatcher.CurrentDispatcher,new MonitorText()},CultureInfo.InvariantCulture);
                View=(ListCollectionView)Get("View");
                ((INotifyCollectionChanged)View).CollectionChanged+=OnViewChanged;
                Call("SetActive",true);
            }
            private void OnViewChanged(object sender,NotifyCollectionChangedEventArgs args)
            { if(args.Action==NotifyCollectionChangedAction.Reset)Resets++; }
            internal object Get(string name) { return Property(name).GetValue(Model,null); }
            internal void Set(string name,object value) { Property(name).SetValue(Model,value,null); }
            private PropertyInfo Property(string name)
            {
                var property=Model.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                Assert.IsNotNull(property,name+" must be presented");return property;
            }
            internal object Call(string name,params object[] arguments)
            {
                var method=Model.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                Assert.IsNotNull(method,name+" must implement the approved coordinator behavior");return method.Invoke(Model,arguments);
            }
            internal object Field(string name)
            {
                var field=Model.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.IsNotNull(field,name+" must guard pending callbacks");return field.GetValue(Model);
            }
            internal void Apply(params ProcessSnapshot[] rows)
            {
                var snapshot=new ResourceSnapshot();foreach(var row in rows)snapshot.Processes.Add(row);
                Call("Apply",snapshot,null);
            }
            internal void Query(string query) { Set("SearchQuery",query); }
            internal ProcessRow Selected
            { get { return (ProcessRow)Get("SelectedProcess"); } set { Set("SelectedProcess",value); } }
            internal bool CanAct { get { return (bool)Get("CanActOnSelectedProcess"); } }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                ((INotifyCollectionChanged)View).CollectionChanged-=OnViewChanged;
                ((IDisposable)Model).Dispose();GC.SuppressFinalize(this);
            }
        }
        private static ProcessSnapshot Row(int id,string name,long time=100,long? memory=10)
        { return new ProcessSnapshot { Id=id,Name=name,CreationTime=time,WorkingSetBytes=memory }; }
        private static bool Until(Func<bool> ready,int milliseconds=2000)
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
        private static void DrainFor(int milliseconds)
        { var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds) { Drain();Thread.Sleep(3); } }
        private static void WaitQuery(Context context)
        { Assert.IsTrue(Until(delegate { return !((bool)context.Field("_state").GetType().GetProperty("IsFiltering",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.Field("_state"),null)); }),"The one-shot query must finish"); }
        [Test]
        public void EquivalentQueryAndFastInputDoNotRepeatRefresh()
        {
            using(var context=new Context())
            {
                var rows=new ProcessSnapshot[30];for(int i=0;i<rows.Length;i++)rows[i]=Row(1000+i,"game.exe");context.Apply(rows);context.Resets=0;
                var watch=Stopwatch.StartNew();for(int i=0;i<20;i++)context.Query((1000+i).ToString(CultureInfo.InvariantCulture));
                Assert.AreEqual("1019",context.Get("SearchQuery"));Assert.AreEqual(30,context.View.Count);Assert.AreEqual(0,context.Resets);
                DrainFor(60);Assert.AreEqual(0,context.Resets,"The 150ms quiet period must not submit early");
                Assert.IsTrue(Until(delegate { return context.View.Count==1; }));Assert.GreaterOrEqual(watch.ElapsedMilliseconds,140);
                Assert.AreEqual(1,context.Resets);Assert.AreEqual(1019,((ProcessRow)context.View.GetItemAt(0)).Id);
                context.Resets=0;context.Query(" 1019 ");context.Query("1019");DrainFor(180);Assert.AreEqual(0,context.Resets);
                context.Query("game");WaitQuery(context);context.Resets=0;context.Query(" GAME ");DrainFor(180);Assert.AreEqual(0,context.Resets);
            }
        }
        [Test]
        public void EquivalentPendingInputDoesNotRestartWaitAndReturningCommittedCancels()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Query("game");
                var revision=context.Field("_revision");var timer=context.Field("_timer");context.Query(" GAME ");
                Assert.AreEqual(revision,context.Field("_revision"));Assert.AreSame(timer,context.Field("_timer"));WaitQuery(context);
                context.Selected=(ProcessRow)context.View.GetItemAt(0);Assert.IsTrue(context.CanAct);context.Resets=0;
                context.Query("other");Assert.IsFalse(context.CanAct);context.Query(" Game ");Assert.IsTrue(context.CanAct);
                DrainFor(180);Assert.AreEqual(0,context.Resets);Assert.AreEqual(10,((ProcessRow)context.View.GetItemAt(0)).Id);
                Assert.IsNull(context.Field("_timer"));
            }
        }
        [Test]
        public void EmptyQueryAndClearApplyImmediatelyWhileWhitespaceRemainsClearable()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Query("game");WaitQuery(context);context.Resets=0;
                context.Query("   ");Assert.AreEqual(2,context.View.Count);Assert.AreEqual(1,context.Resets);Assert.IsTrue((bool)context.Get("CanClearSearch"));
                context.Call("ClearSearch");Assert.AreEqual(string.Empty,context.Get("SearchQuery"));Assert.IsFalse((bool)context.Get("CanClearSearch"));
                Assert.AreEqual(1,context.Resets);context.Query("other");context.Call("ClearSearch");DrainFor(180);Assert.AreEqual(1,context.Resets);
            }
        }
        [Test]
        public void PendingQueryKeepsCountsAndArrivingSnapshotUsesCommittedQuery()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Query("game");WaitQuery(context);
                context.Selected=(ProcessRow)context.View.GetItemAt(0);context.Query("other");Assert.IsFalse(context.CanAct);
                Assert.AreEqual(1,context.Get("MatchedCount"));Assert.AreEqual("Filtering processes…",context.Get("StatusText"));
                context.Resets=0;context.Apply(Row(10,"game.exe",100,30),Row(20,"other.exe"),Row(30,"second-game.exe"));
                Assert.AreEqual(1,context.Resets);Assert.AreEqual(2,context.View.Count);Assert.AreEqual(3,context.Get("TotalCount"));
                Assert.IsFalse(context.CanAct);WaitQuery(context);Assert.AreEqual(1,context.View.Count);Assert.AreEqual(20,((ProcessRow)context.View.GetItemAt(0)).Id);
                Assert.IsNull(context.Selected);
            }
        }
        [Test]
        public void FailureRetainsStaleRowsAndRecoveryClearsInspectionSelection()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Selected=(ProcessRow)context.View.GetItemAt(0);
                Assert.IsTrue(context.CanAct);context.Call("Apply",null,"fixture failure");
                Assert.IsNull(context.Selected);Assert.IsFalse(context.CanAct);Assert.AreEqual(2,context.View.Count);
                Assert.AreEqual("Showing last successful data; actions paused",context.Get("StatusText"));Assert.AreEqual("fixture failure",context.Get("StatusDetail"));
                context.Selected=(ProcessRow)context.View.GetItemAt(0);Assert.IsNotNull(context.Selected);Assert.IsFalse(context.CanAct);
                context.Query("missing");WaitQuery(context);Assert.AreEqual(0,context.View.Count);
                Assert.AreEqual("Showing last successful data; actions paused",context.Get("StatusText"));
                context.Call("ClearSearch");context.Selected=(ProcessRow)context.View.GetItemAt(0);
                context.Apply(Row(10,"game.exe",100,null),Row(20,"other.exe",100,null));
                Assert.IsNull(context.Selected);Assert.AreEqual(string.Empty,context.Get("StatusText"));Assert.AreEqual(string.Empty,context.Get("StatusDetail"));
                context.Selected=(ProcessRow)context.View.GetItemAt(0);Assert.IsTrue(context.CanAct,"Unavailable metric cells must not invalidate a successful list");
            }
        }
        [Test]
        public void BeforeFirstSuccessCountsAreUnknownAndEmptyDiffersFromNoMatches()
        {
            using(var context=new Context())
            {
                Assert.AreEqual("—",context.Get("CountText"));Assert.AreEqual("Loading processes…",context.Get("StatusText"));
                context.Call("Apply",null,null);Assert.AreEqual("—",context.Get("CountText"));Assert.AreEqual("Could not read the process list; actions paused",context.Get("StatusText"));
                context.Apply();Assert.AreEqual("Matches: 0 / Total: 0",context.Get("CountText"));Assert.AreEqual("No processes available",context.Get("StatusText"));
                context.Apply(Row(10,"game.exe"));context.Query("missing");WaitQuery(context);
                Assert.AreEqual("Matches: 0 / Total: 1",context.Get("CountText"));Assert.AreEqual("No matching processes",context.Get("StatusText"));
            }
        }
        [Test]
        public void BatchResetPreservesOnlyTheSameVisibleIdentityIncludingTransientSelectionLoss()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));var selected=(ProcessRow)context.View.GetItemAt(0);context.Selected=selected;
                ((INotifyCollectionChanged)context.View).CollectionChanged+=delegate(object sender,NotifyCollectionChangedEventArgs args)
                { if(args.Action==NotifyCollectionChangedAction.Reset)context.Selected=null; };
                context.Resets=0;context.Apply(Row(10,"game.exe",100,30),Row(30,"new.exe"));
                Assert.AreEqual(1,context.Resets);Assert.AreSame(selected,context.Selected);
                context.Apply(Row(10,"game.exe",101,30),Row(30,"new.exe"));Assert.IsNull(context.Selected);
            }
        }
        [TestCase("exit")]
        [TestCase("reuse")]
        [TestCase("filter")]
        public void CapturedTargetsAndSelectionsBecomeInvalidWhenIdentityOrVisibilityChanges(string change)
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Query("game");WaitQuery(context);
                context.Selected=(ProcessRow)context.View.GetItemAt(0);var target=context.Selected.Snapshot;
                Assert.IsTrue((bool)context.Call("IsCurrentTarget",target));
                if(change=="filter") { context.Query("other");WaitQuery(context); }
                else if(change=="reuse")context.Apply(Row(10,"game.exe",101),Row(20,"other.exe"));
                else context.Apply(Row(20,"other.exe"));
                Assert.IsNull(context.Selected);Assert.IsFalse((bool)context.Call("IsCurrentTarget",target));
            }
        }
        [TestCase(0,100)]
        [TestCase(10,0)]
        public void InvalidIdentityCannotActEvenIfVisible(int id,long creationTime)
        {
            using(var context=new Context())
            {
                context.Apply(Row(id,"game.exe",creationTime));context.Selected=(ProcessRow)context.View.GetItemAt(0);
                Assert.IsFalse(context.CanAct);Assert.IsFalse((bool)context.Call("IsCurrentTarget",context.Selected.Snapshot));
            }
        }
        [Test]
        public void ForeignRowsCannotBeSelectedByPidAlone()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"));context.Selected=new ProcessRow(Row(10,"game.exe"));Assert.IsNull(context.Selected);
            }
        }
        [Test]
        public void SnapshotChangesRefreshOnlyMembershipFilterAndCurrentSort()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Resets=0;
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));Assert.AreEqual(0,context.Resets);
                var cpu=Row(10,"game.exe");cpu.CpuUsage=3;context.Apply(cpu,Row(20,"other.exe"));Assert.AreEqual(0,context.Resets);
                context.Apply(Row(10,"game.exe",100,30),Row(20,"other.exe"));Assert.AreEqual(1,context.Resets);
                context.Resets=0;context.Apply(Row(10,"renamed.exe",100,30),Row(20,"other.exe"));Assert.AreEqual(0,context.Resets);
                context.Query("renamed");WaitQuery(context);context.Resets=0;context.Apply(Row(10,"excluded.exe",100,30),Row(20,"other.exe"));
                Assert.AreEqual(1,context.Resets);Assert.AreEqual(0,context.View.Count);
                context.Call("ClearSearch");context.Resets=0;context.Call("Sort","CpuUsage");Assert.AreEqual(1,context.Resets);
            }
        }
        [Test]
        public void LeavingReentryAndDisposalRejectQueuedOldRevisionWithoutAResidentTimer()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"));context.Query("game");int old=(int)context.Field("_revision");
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate { context.Call("CommitQuery",old); }));
                context.Call("SetActive",false);Assert.AreEqual(0,context.View.Count);Assert.IsNull(context.Field("_timer"));
                context.Call("SetActive",true);context.Apply(Row(20,"other.exe"));context.Query("other");context.Resets=0;
                Drain();Assert.AreEqual(0,context.Resets);Assert.AreEqual(0,context.View.Count);WaitQuery(context);Assert.AreEqual(1,context.View.Count);
                context.Query("never");int disposedRevision=(int)context.Field("_revision");((IDisposable)context.Model).Dispose();
                context.Call("CommitQuery",disposedRevision);DrainFor(180);Assert.AreEqual(0,context.View.Count);Assert.IsNull(context.Field("_timer"));
                Assert.IsFalse((bool)context.Get("HasStatus"));Assert.IsFalse(context.CanAct);
            }
        }
        [Test]
        public void TranslationRefreshChangesTextOnlyAndKeepsRawQuerySortAndSelection()
        {
            using(var context=new Context())
            {
                context.Apply(Row(10,"game.exe"),Row(20,"other.exe"));context.Query(" game ");WaitQuery(context);
                context.Selected=(ProcessRow)context.View.GetItemAt(0);var selected=context.Selected;context.Resets=0;
                foreach(string culture in new[]{"zh-Hans","en","de"})
                {
                    Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));context.Call("RefreshText");
                    StringAssert.Contains(culture=="zh-Hans"?"匹配：1 / 总数：2":"Matches: 1 / Total: 2",(string)context.Get("CountText"));
                    Assert.AreEqual(" game ",context.Get("SearchQuery"));Assert.AreSame(selected,context.Selected);Assert.AreEqual(0,context.Resets);
                }
            }
        }
    }
}
#pragma warning restore 1591
