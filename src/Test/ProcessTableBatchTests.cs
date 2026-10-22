using System;
using System.Globalization;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    public sealed class ProcessTableBatchTests
    {
        private static ProcessSnapshot Snapshot(int id,long time,long? memory)
        { return new ProcessSnapshot { Id=id,CreationTime=time,Name="same.exe",WorkingSetBytes=memory }; }
        private static object Batch(ProcessTable table,params ProcessSnapshot[] snapshots)
        {
            var method=typeof(ProcessTable).GetMethod("ReconcileBatch",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.IsNotNull(method,"ReconcileBatch must return change flags without repeated collection rebuilds");
            return method.Invoke(table,new object[]{snapshots});
        }
        private static bool Refresh(int changes,string field,bool hasQuery)
        {
            var assembly=Assembly.GetExecutingAssembly();var type=assembly.GetType("WinMemoryCleaner.ProcessTableChanges");
            var flags=assembly.GetType("WinMemoryCleaner.ProcessTableChange");
            Assert.IsNotNull(type,"ProcessTableChanges must distinguish relevant sort/filter changes");Assert.IsNotNull(flags);
            var method=type.GetMethod("RequiresRefresh",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            Assert.IsNotNull(method);return (bool)method.Invoke(null,new[]{Enum.ToObject(flags,changes),(object)field,hasQuery});
        }
        private static int Bits(object changes) { return Convert.ToInt32(changes,CultureInfo.InvariantCulture); }
        [Test]
        public void BatchMembershipAndSortChangesProduceOneViewReset()
        {
            var table=new ProcessTable();table.Reconcile(new[]{Snapshot(10,100,10),Snapshot(20,200,20),Snapshot(30,300,30)});
            var view=new ListCollectionView(table.Rows) { CustomSort=new ProcessRowComparer("WorkingSetBytes",true) };
            int resets=0,incremental=0,counts=0,indexers=0;
            ((INotifyCollectionChanged)view).CollectionChanged+=delegate(object sender,NotifyCollectionChangedEventArgs args)
            { if(args.Action==NotifyCollectionChangedAction.Reset)resets++;else incremental++; };
            ((INotifyPropertyChanged)table.Rows).PropertyChanged+=delegate(object sender,PropertyChangedEventArgs args)
            { if(args.PropertyName=="Count")counts++;if(args.PropertyName=="Item[]")indexers++; };
            table.Reconcile(new[]{Snapshot(20,200,50),Snapshot(40,400,40),Snapshot(50,500,5)});
            Assert.AreEqual(1,resets,"The actual sorted view must rebuild at most once per membership batch");Assert.AreEqual(0,incremental);
            Assert.AreEqual(1,counts);Assert.AreEqual(1,indexers);Assert.AreEqual(3,view.Count);
            Assert.AreEqual(20,((ProcessRow)view.GetItemAt(0)).Id);Assert.AreEqual(40,((ProcessRow)view.GetItemAt(1)).Id);
        }
        [Test]
        public void IdenticalSnapshotDoesNotNotifyAndPidReuseReplacesIdentity()
        {
            var table=new ProcessTable();table.Reconcile(new[]{Snapshot(10,100,10)});var old=table.Rows[0];
            int rowNotifications=0,collectionNotifications=0;
            old.PropertyChanged+=delegate { rowNotifications++; };table.Rows.CollectionChanged+=delegate { collectionNotifications++; };
            table.Reconcile(new[]{Snapshot(10,100,10)});Assert.AreEqual(0,rowNotifications);Assert.AreEqual(0,collectionNotifications);
            table.Reconcile(new[]{Snapshot(10,101,10)});Assert.AreNotSame(old,table.Rows[0]);Assert.AreEqual(1,collectionNotifications);
        }
        [Test]
        public void BatchReturnsExactChangedFieldsAndRetainsCellNotifications()
        {
            var table=new ProcessTable();Assert.AreEqual(1,Bits(Batch(table,Snapshot(10,100,10))));var row=table.Rows[0];
            int notifications=0;row.PropertyChanged+=delegate { notifications++; };
            Assert.AreEqual(0,Bits(Batch(table,Snapshot(10,100,10))));Assert.AreEqual(0,notifications);
            var changed=Snapshot(10,100,11);changed.Name="renamed.exe";changed.PrivateBytes=12;changed.CpuUsage=1;changed.GpuUsage=2;changed.GpuMemoryBytes=13;
            Assert.AreEqual(126,Bits(Batch(table,changed)));Assert.AreSame(row,table.Rows[0]);Assert.AreEqual(11,notifications);
            Assert.AreEqual("1.0%",row.CpuText);Assert.AreEqual(1,Bits(Batch(table,Snapshot(10,101,null))));
        }
        [Test]
        public void NullAndDuplicateItemsKeepExistingIgnoreSemanticsAndNullListClears()
        {
            var table=new ProcessTable();var first=Snapshot(10,100,10);var duplicate=Snapshot(10,999,999);
            Assert.AreEqual(1,Bits(Batch(table,null,first,duplicate)));Assert.AreEqual(1,table.Rows.Count);
            Assert.AreSame(first,table.Rows[0].Snapshot);Assert.AreEqual(0,Bits(Batch(table,null,Snapshot(10,100,10),duplicate)));
            var find=typeof(ProcessTable).GetMethod("Find",BindingFlags.Instance|BindingFlags.NonPublic);Assert.IsNotNull(find);
            Assert.AreSame(table.Rows[0],find.Invoke(table,new object[]{10}));Assert.IsNull(find.Invoke(table,new object[]{20}));
            Assert.AreEqual(1,Bits(Batch(table,(ProcessSnapshot[])null)));Assert.AreEqual(0,table.Rows.Count);
        }
        [TestCase(0,"WorkingSetBytes",false)]
        [TestCase(1,"WorkingSetBytes",true)]
        [TestCase(2,"WorkingSetBytes",false)]
        [TestCase(2,"Name",true)]
        [TestCase(4,"WorkingSetBytes",true)]
        [TestCase(8,"WorkingSetBytes",false)]
        [TestCase(8,"PrivateBytes",true)]
        [TestCase(16,"CpuUsage",true)]
        [TestCase(32,"GpuUsage",true)]
        [TestCase(64,"GpuMemoryBytes",true)]
        [TestCase(126,"Id",false)]
        public void RefreshOnlyForMembershipFilterOrCurrentSort(int changes,string field,bool expected)
        { Assert.AreEqual(expected,Refresh(changes,field,false)); }
        [Test]
        public void NameChangeRefreshesAnActiveFilterWithoutNameSort()
        { Assert.IsTrue(Refresh(2,"WorkingSetBytes",true)); }
        [TestCase(false)]
        [TestCase(true)]
        public void MissingMetricsRemainAfterRealZeroInEitherSortDirection(bool descending)
        {
            var table=new ProcessTable();Batch(table,Snapshot(10,100,null),Snapshot(20,200,0),Snapshot(30,300,30));
            var view=new ListCollectionView(table.Rows) { CustomSort=new ProcessRowComparer("WorkingSetBytes",descending) };
            Assert.AreEqual(10,((ProcessRow)view.GetItemAt(2)).Id);Assert.AreEqual(descending?30:20,((ProcessRow)view.GetItemAt(0)).Id);
        }
    }
}
#pragma warning restore 1591
