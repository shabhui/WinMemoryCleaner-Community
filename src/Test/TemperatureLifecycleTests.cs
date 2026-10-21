using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class TemperatureLifecycleTests
    {
        private sealed class Monitor : IResourceMonitor,ITemperatureConfiguration
        {
            internal readonly List<int> Refreshes=new List<int>();
            internal Action DuringSample,OnDispose;
            internal int Samples,Suspends,Disposals,Resets,SampleThread,ConfigThread;
            internal bool Requested;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                SampleThread=Thread.CurrentThread.ManagedThreadId;Interlocked.Increment(ref Samples);
                var action=DuringSample;if(action!=null)action();
                return new ResourceSnapshot { CpuTemperature=temperatures&&Requested?MetricValue.Available(54,"Fixture CPU"):MetricValue.Missing(MetricStatus.Disabled,"not requested"),SensorBackendStatus="Running",SensorDriverStatus="KeepExistingDriver" };
            }
            public void ConfigureTemperatures(int seconds) { ConfigThread=Thread.CurrentThread.ManagedThreadId;Refreshes.Add(seconds); }
            public void SetTemperatureDemand(bool requested) { Requested=requested; }
            public void ResetTemperatureDriver() { Interlocked.Increment(ref Resets); }
            public void Suspend() { Interlocked.Increment(ref Suspends); }
            public void Dispose() { Interlocked.Increment(ref Disposals);var action=OnDispose;if(action!=null)action(); }
        }
        private static MonitoringDemand Demand(bool temperatures)
        { return new MonitoringDemand(true,false,temperatures); }
        [Test]
        public void SessionIsLazyAndForwardsNormalizedRefreshAndStatus()
        {
            Monitor monitor=null;int creations=0;
            using(var session=new MonitoringSession(delegate { creations++;return monitor=new Monitor(); }))
            {
                session.SetDemand(Demand(true));session.RequestDriverReset();Assert.AreEqual(0,creations);
                ResourceSnapshot snapshot=null;
                foreach(int seconds in new[]{1,2,5,99})snapshot=session.Sample(Demand(true),seconds);
                Assert.AreEqual(1,creations);CollectionAssert.AreEqual(new[]{1,2,5,2},monitor.Refreshes);
                Assert.AreEqual(1,monitor.Resets);Assert.AreEqual("Running",snapshot.SensorBackendStatus);Assert.AreEqual("KeepExistingDriver",snapshot.SensorDriverStatus);
            }
            Assert.IsTrue(SpinWait.SpinUntil(delegate { return monitor.Disposals==1; },2000));
        }
        [Test]
        public void CancelledTemperatureDemandCannotBeResurrectedByAnOldSample()
        {
            Monitor monitor=null;
            using(var session=new MonitoringSession(delegate { return monitor=new Monitor(); }))
            {
                session.SetDemand(Demand(true));session.Sample(Demand(true),2);Assert.IsTrue(monitor.Requested);
                session.SetDemand(Demand(false));Assert.IsFalse(monitor.Requested);
                var obsolete=session.Sample(Demand(true),2);Assert.IsNull(obsolete.CpuTemperature.Value);Assert.IsFalse(monitor.Requested);
                session.SetDemand(Demand(true));Assert.IsTrue(monitor.Requested);Assert.AreEqual(54,session.Sample(Demand(true),5).CpuTemperature.Value);
            }
            Assert.IsTrue(SpinWait.SpinUntil(delegate { return monitor.Disposals==1; },2000));
        }
        [Test]
        public void DisposeRevokesDemandImmediatelyButSerializesOwnedReleaseAfterSample()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))using(var finished=new ManualResetEvent(false))using(var disposed=new ManualResetEvent(false))
            {
                Monitor monitor=null;
                var session=new MonitoringSession(delegate { return monitor=new Monitor { DuringSample=delegate { entered.Set();release.WaitOne(4000); },OnDispose=delegate { disposed.Set(); } }; });session.SetDemand(Demand(true));
                ThreadPool.QueueUserWorkItem(delegate { try { session.Sample(Demand(true),2); } finally { finished.Set(); } });
                try
                {
                    Assert.IsTrue(entered.WaitOne(2000));var watch=Stopwatch.StartNew();session.Dispose();session.Dispose();
                    Assert.Less(watch.ElapsedMilliseconds,200);Assert.IsFalse(monitor.Requested);Assert.AreEqual(0,monitor.Disposals);
                    release.Set();Assert.IsTrue(finished.WaitOne(2000));Assert.IsTrue(disposed.WaitOne(2000));Assert.AreEqual(1,monitor.Disposals);
                    session.Sample(Demand(true),2);Assert.AreEqual(1,monitor.Samples);Assert.IsFalse(monitor.Requested);
                }
                finally { release.Set();session.Dispose();finished.WaitOne(2000);disposed.WaitOne(2000); }
            }
        }
        [Test]
        public void HiddenWindowWithTemperatureStripKeepsDemandAndBackgroundConfiguration()
        {
            using(var sampled=new ManualResetEvent(false))
            {
                Monitor monitor=null;int ui=Thread.CurrentThread.ManagedThreadId;
                using(var session=new MonitoringSession(delegate { return monitor=new Monitor(); }))
                using(var pump=new MonitoringPump(delegate(MonitoringDemand demand,int generation) { session.Sample(demand,5);sampled.Set(); },session.Suspend))
                {
                    var demand=MonitoringPolicy.GetDemand(false,false,true,true);session.SetDemand(demand);pump.SetDemand(demand,5);
                    Assert.IsTrue(sampled.WaitOne(2000));Assert.IsTrue(monitor.Requested);Assert.AreNotEqual(ui,monitor.ConfigThread);Assert.AreEqual(monitor.SampleThread,monitor.ConfigThread);
                    session.SetDemand(new MonitoringDemand(false,false,false));pump.SetDemand(new MonitoringDemand(false,false,false),5);
                    Assert.IsFalse(monitor.Requested);Assert.IsTrue(SpinWait.SpinUntil(delegate { return monitor.Suspends>0; },2000));
                }
                Assert.IsTrue(SpinWait.SpinUntil(delegate { return monitor.Disposals==1; },2000));
            }
        }
        [Test]
        public void ClosingBeforeFirstSampleDoesNotCreateAnyMonitor()
        {
            int creations=0;var session=new MonitoringSession(delegate { creations++;return new Monitor(); });
            session.Dispose();session.SetDemand(Demand(true));session.RequestDriverReset();session.Sample(Demand(true),2);session.Suspend();
            Assert.AreEqual(0,creations);
        }
    }
}
#pragma warning restore 1591
