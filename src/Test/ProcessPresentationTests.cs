using NUnit.Framework;
using System;
using System.Collections;
using System.ComponentModel;
using System.Reflection;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class ProcessPresentationTests
    {
        private static Type TypeOf(string name)
        {
            var type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner." + name);
            Assert.IsNotNull(type, name + " must retain selected process identity and unavailable readings");
            return type;
        }
        private static object Snapshot(int id, long time, double? cpu)
        {
            var type = TypeOf("ProcessSnapshot"); var item = Activator.CreateInstance(type);
            type.GetProperty("Id").SetValue(item, id, null);
            type.GetProperty("CreationTime").SetValue(item, time, null);
            type.GetProperty("Name").SetValue(item, "same.exe", null);
            type.GetProperty("CpuUsage").SetValue(item, cpu, null);
            return item;
        }
        private static void Apply(object table, params object[] items)
        {
            var array = Array.CreateInstance(TypeOf("ProcessSnapshot"), items.Length);
            for (int i = 0; i < items.Length; i++) array.SetValue(items[i], i);
            table.GetType().GetMethod("Reconcile").Invoke(table, new object[] { array });
        }
        private static IList Rows(object table) { return (IList)table.GetType().GetProperty("Rows").GetValue(table, null); }
        private static object Get(object row, string name) { return row.GetType().GetProperty(name).GetValue(row, null); }
        [Test]
        public void SameNames_RemainSeparatePidRows()
        {
            var table = Activator.CreateInstance(TypeOf("ProcessTable"));
            Apply(table, Snapshot(10, 100, null), Snapshot(20, 200, null));
            Assert.AreEqual(2, Rows(table).Count);
            Assert.AreNotEqual(Get(Rows(table)[0], "Id"), Get(Rows(table)[1], "Id"));
        }
        [Test]
        public void Refresh_UpdatesRowsInPlaceButReplacesReusedPidIdentity()
        {
            var table = Activator.CreateInstance(TypeOf("ProcessTable"));
            Apply(table, Snapshot(10, 100, null)); var first = Rows(table)[0];
            Apply(table, Snapshot(10, 100, 1.5)); Assert.AreSame(first, Rows(table)[0]);
            Assert.AreEqual("1.5%", Get(first, "CpuText"));
            Apply(table, Snapshot(10, 101, 2.0)); Assert.AreNotSame(first, Rows(table)[0]);
        }
        [Test]
        public void UnavailableReading_IsNotDisplayedOrSortedAsRealZero()
        {
            var table = Activator.CreateInstance(TypeOf("ProcessTable"));
            Apply(table, Snapshot(10, 100, null)); var row = Rows(table)[0];
            Assert.AreEqual("—", Get(row, "CpuText")); Assert.AreEqual(-1.0, Get(row, "CpuSortKey"));
            Apply(table, Snapshot(10, 100, 0.0)); Assert.AreEqual("0.0%", Get(row, "CpuText"));
            Assert.AreEqual(0.0, Get(row, "CpuSortKey"));
        }
        [Test]
        public void IdenticalRefresh_DoesNotInvalidateAllRowBindings()
        {
            var table = Activator.CreateInstance(TypeOf("ProcessTable"));
            Apply(table, Snapshot(10, 100, 3.0)); int notifications=0;
            ((INotifyPropertyChanged)Rows(table)[0]).PropertyChanged += delegate { notifications++; };
            Apply(table, Snapshot(10, 100, 3.0)); Assert.AreEqual(0, notifications);
        }
        [Test]
        public void ExitedRows_AreRemovedWithoutKeepingHistory()
        {
            var table = Activator.CreateInstance(TypeOf("ProcessTable"));
            Apply(table, Snapshot(10, 100, null), Snapshot(20, 200, null));
            Apply(table, Snapshot(20, 200, null)); Assert.AreEqual(1, Rows(table).Count);
            Apply(table); Assert.AreEqual(0, Rows(table).Count);
        }
    }
}
#pragma warning restore 1591
