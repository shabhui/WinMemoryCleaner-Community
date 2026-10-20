using NUnit.Framework;
using System;
using System.Reflection;
using System.Threading;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class MonitoringSchedulerTests
    {
        private static object Pump(Action<MonitoringDemand, int> sample, Action suspend)
        {
            var type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.MonitoringPump");
            Assert.IsNotNull(type, "The monitor must have a serialized demand-aware lifecycle");
            return Activator.CreateInstance(type, sample, suspend);
        }
        private static void Demand(object pump, bool enabled)
        {
            pump.GetType().GetMethod("SetDemand").Invoke(pump, new object[] { new MonitoringDemand(enabled, enabled, enabled), 1 });
        }
        [Test]
        public void HiddenDuringInFlightSample_RejectsOldResultAndSuspends()
        {
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            using (var suspended = new ManualResetEvent(false))
            {
                int generation=-1; int samples=0;
                var pump = Pump(delegate(MonitoringDemand d, int g) { Interlocked.Increment(ref samples); generation=g; entered.Set(); release.WaitOne(3000); }, delegate { suspended.Set(); });
                try
                {
                    Demand(pump, true); Assert.IsTrue(entered.WaitOne(2000));
                    Demand(pump, false);
                    Assert.AreEqual(false, pump.GetType().GetMethod("IsCurrent").Invoke(pump, new object[] {generation}));
                    release.Set(); Assert.IsTrue(suspended.WaitOne(2000));
                    Thread.Sleep(1100); Assert.AreEqual(1, samples, "No detailed sampling without a consumer");
                }
                finally { release.Set(); ((IDisposable)pump).Dispose(); }
            }
        }
        [Test]
        public void SlowSample_DoesNotOverlapOrQueueUnboundedWork()
        {
            using (var entered = new ManualResetEvent(false))
            using (var release = new ManualResetEvent(false))
            {
                int count=0;
                var pump = Pump(delegate(MonitoringDemand d, int g) { Interlocked.Increment(ref count); entered.Set(); release.WaitOne(4000); }, delegate { });
                try
                {
                    Demand(pump,true); Assert.IsTrue(entered.WaitOne(2000));
                    Thread.Sleep(2200); Assert.AreEqual(1,count);
                }
                finally { Demand(pump,false); release.Set(); ((IDisposable)pump).Dispose(); }
            }
        }
        [Test]
        public void ProviderException_DoesNotKillSchedulerOrPreventNextDemand()
        {
            using (var second = new ManualResetEvent(false))
            {
                int count=0;
                var pump=Pump(delegate(MonitoringDemand d,int g) { if(Interlocked.Increment(ref count)==1) throw new InvalidOperationException("sensor failed"); second.Set(); }, delegate { });
                try { Demand(pump,true); Assert.IsTrue(second.WaitOne(3000)); Assert.GreaterOrEqual(count,2); }
                finally { ((IDisposable)pump).Dispose(); }
            }
        }
        [Test]
        public void SlowSuspend_DoesNotBlockUiDemandChangesOrDispose()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))using(var finished=new ManualResetEvent(false))using(var uiDone=new ManualResetEvent(false))
            {
                int calls=0;
                var pump=(MonitoringPump)Pump(delegate(MonitoringDemand demand,int generation) { },delegate { if(Interlocked.Increment(ref calls)==1){entered.Set();release.WaitOne(4000);finished.Set();} });
                try
                {
                    Demand(pump,false);Assert.IsTrue(entered.WaitOne(2000));
                    ThreadPool.QueueUserWorkItem(delegate { Demand(pump,true);pump.Dispose();uiDone.Set(); });
                    Assert.IsTrue(uiDone.WaitOne(200),"Neither UI demand changes nor Dispose may wait for a slow background suspend.");
                }
                finally { release.Set();Assert.IsTrue(finished.WaitOne(2000));uiDone.WaitOne(2000);pump.Dispose(); }
            }
        }
    }
}
#pragma warning restore 1591
