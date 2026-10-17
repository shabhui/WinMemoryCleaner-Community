using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class NativeSamplingTests
    {
        private static IDisposable Monitor()
        {
            var type=Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.ResourceMonitor");
            Assert.IsNotNull(type,"The real, demand-driven Windows sampler must exist.");
            return (IDisposable)Activator.CreateInstance(type);
        }
        private static ResourceSnapshot Sample(IDisposable monitor,bool processes,bool temperatures)
        {
            return (ResourceSnapshot)monitor.GetType().GetMethod("Sample").Invoke(monitor,new object[]{processes,temperatures});
        }
        private static MetricValue LibreCpuSample(TemperatureSampler sampler)
        {
            var readings = new object[] { null, null };
            typeof(TemperatureSampler).GetMethod("LibreTemperatures", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sampler, readings);
            return (MetricValue)readings[0];
        }
        [Test]
        public void NativeSampler_SupportedGpuResumesWithoutFailureBackoff()
        {
            using(var monitor=Monitor())
            {
                Sample(monitor,false,false); Thread.Sleep(120);
                var supported=Sample(monitor,false,false).GpuUsage;
                if(supported.Status!=MetricStatus.Available)
                {
                    Assert.IsNull(supported.Value); Assert.IsNotEmpty(supported.Detail); return;
                }
                monitor.GetType().GetMethod("Suspend").Invoke(monitor,null);
                Assert.AreEqual(MetricStatus.WarmingUp,Sample(monitor,false,false).GpuUsage.Status,"A successful provider must reopen immediately after demand resumes.");
                Thread.Sleep(120);
                Assert.AreEqual(MetricStatus.Available,Sample(monitor,false,false).GpuUsage.Status);
            }
        }

        [Test]
        public void NativeSampler_FirstCpuFrameIsUnavailableThenReadsMachineUsage()
        {
            using(var monitor=Monitor())
            {
                var first=Sample(monitor,false,false);
                Assert.AreEqual(MetricStatus.WarmingUp,first.CpuUsage.Status);
                Assert.IsNull(first.CpuUsage.Value);
                Thread.Sleep(120);
                var second=Sample(monitor,false,false);
                Assert.AreEqual(MetricStatus.Available,second.CpuUsage.Status);
                Assert.That(second.CpuUsage.Value.Value,Is.InRange(0.0,100.0));
                Assert.AreEqual(0,second.Processes.Count);
                Assert.AreEqual(MetricStatus.Disabled,second.CpuTemperature.Status);
            }
        }
        [Test]
        public void NativeSampler_EnumeratesAllPidsAndKeepsCreationIdentityAndCpuDeltas()
        {
            using(var monitor=Monitor())
            using(var self=Process.GetCurrentProcess())
            {
                var first=Sample(monitor,true,false);
                Assert.Greater(first.Processes.Count,20);
                var ids=new System.Collections.Generic.HashSet<int>();
                ProcessSnapshot own=null;
                foreach(var row in first.Processes)
                {
                    Assert.IsTrue(ids.Add(row.Id),"One row per PID, including duplicate names.");
                    if(row.Id==self.Id)own=row;
                }
                Assert.IsNotNull(own);
                Assert.AreEqual(self.StartTime.ToUniversalTime().ToFileTimeUtc(),own.CreationTime);
                Assert.Greater(own.WorkingSetBytes.Value,0);
                Assert.IsNull(own.CpuUsage);
                Thread.Sleep(120);
                var second=Sample(monitor,true,false);
                foreach(var row in second.Processes)if(row.Id==self.Id)Assert.IsNotNull(row.CpuUsage);
            }
        }
        [Test]
        public void NativeSampler_SuspendDropsDeltaHistoryAndDisposedSamplerCannotRestart()
        {
            var monitor=Monitor();
            try
            {
                Sample(monitor,true,false);
                monitor.GetType().GetMethod("Suspend").Invoke(monitor,null);
                var next=Sample(monitor,true,false);
                Assert.AreEqual(MetricStatus.WarmingUp,next.CpuUsage.Status);
                foreach(var row in next.Processes)Assert.IsNull(row.CpuUsage);
                monitor.Dispose();
                var error=Assert.Throws<TargetInvocationException>(delegate{Sample(monitor,false,false);});
                Assert.IsInstanceOf<ObjectDisposedException>(error.InnerException);
            }
            finally{monitor.Dispose();}
        }
        [Test]
        public void NativeSampler_TemperatureSourceIsIdentifiedOrExplicitlyUnavailable()
        {
            using(var monitor=Monitor())
            {
                var result=Sample(monitor,false,true);
                foreach(var reading in new[]{result.CpuTemperature,result.GpuTemperature})
                {
                    Assert.IsNotNull(reading);
                    Assert.IsNotEmpty(reading.Detail);
                    if(reading.Status==MetricStatus.Available)Assert.That(reading.Value.Value,Is.InRange(1.0,150.0));
                    else Assert.IsNull(reading.Value);
                    Assert.IsFalse(reading.Detail.IndexOf("ACPI",StringComparison.OrdinalIgnoreCase)>=0&&reading.Status==MetricStatus.Available);
                }
            }
        }
        [Test]
        public void NativeSampler_SkipsLibreHardwareMonitorWmiWhenNoProviderProcessIsRunning()
        {
            var sampler=new WinMemoryCleaner.TemperatureSampler();
            try
            {
                sampler.ProviderRunning=delegate{ return false; };
                var cpu=LibreCpuSample(sampler);
                Assert.AreEqual(MetricStatus.Unsupported,cpu.Status);
                Assert.IsNull(cpu.Value);
                Assert.IsNotEmpty(cpu.Detail);
                Assert.AreEqual(0,sampler.WmiAttempts,"Without a running LibreHardwareMonitor the WMI namespace cannot exist, so the managed WMI stack must stay unloaded.");
            }
            finally{ sampler.Dispose(); }
        }
        [Test]
        public void NativeSampler_AttemptsLibreHardwareMonitorWmiWhenProviderProcessIsRunning()
        {
            var sampler=new WinMemoryCleaner.TemperatureSampler();
            try
            {
                sampler.ProviderRunning=delegate{ return true; };
                var cpu=LibreCpuSample(sampler);
                Assert.AreEqual(1,sampler.WmiAttempts);
                Assert.IsNotNull(cpu);
                Assert.IsNotEmpty(cpu.Detail);
                if(cpu.Status==MetricStatus.Available)Assert.That(cpu.Value.Value,Is.InRange(1.0,150.0));
            }
            finally{ sampler.Dispose(); }
        }
        [Test]
        public void NativeSampler_CachesPositiveProviderDetectionForTheGateWindow()
        {
            var sampler=new WinMemoryCleaner.TemperatureSampler();
            try
            {
                int calls=0;
                sampler.ProviderRunning=delegate{ calls++;return true; };
                Assert.IsTrue(sampler.ProviderExists());
                Assert.IsTrue(sampler.ProviderExists());
                Assert.AreEqual(1,calls,"A positive detection must be trusted for the gate window so repeat temperature ticks do not rescan the process list.");
            }
            finally{ sampler.Dispose(); }
        }
        [Test]
        public void NativeSampler_DoesNotCacheNegativeOrFailedProviderDetection()
        {
            var sampler=new WinMemoryCleaner.TemperatureSampler();
            try
            {
                int calls=0;
                sampler.ProviderRunning=delegate{ calls++;return false; };
                Assert.IsFalse(sampler.ProviderExists());
                Assert.IsFalse(sampler.ProviderExists());
                Assert.AreEqual(2,calls,"A negative detection must be rechecked so a newly started provider is picked up on the next tick.");
                sampler.ProviderRunning=delegate{ calls++;throw new InvalidOperationException("broken check"); };
                Assert.IsTrue(sampler.ProviderExists(),"A broken check must fail open rather than hide real sensors.");
                Assert.IsTrue(sampler.ProviderExists());
                Assert.AreEqual(4,calls,"Fail-open must not be cached as a trusted detection.");
            }
            finally{ sampler.Dispose(); }
        }
        [Test]
        public void NativeSampler_ReportsMissingProviderAsNotDetectedRatherThanDefinitelyNotRunning()
        {
            var sampler=new WinMemoryCleaner.TemperatureSampler();
            try
            {
                sampler.ProviderRunning=delegate{ return false; };
                var cpu=LibreCpuSample(sampler);
                Assert.AreEqual(MetricStatus.Unsupported,cpu.Status);
                Assert.IsTrue(cpu.Detail.IndexOf("not detected",StringComparison.Ordinal)>=0,"A renamed LibreHardwareMonitor cannot be detected, so the status must not claim the provider is definitely not running.");
            }
            finally{ sampler.Dispose(); }
        }
        [Test]
        public void NativeSampler_ExcludesVirtualGpuAdaptersAndNeverInventsUnsupportedMetrics()
        {
            using(var monitor=Monitor())
            {
                var result=Sample(monitor,true,false);
                Assert.IsNotNull(result.GpuUsage);
                Assert.IsNotNull(result.GpuName);
                Assert.IsFalse(result.GpuName.IndexOf("Virtual Display",StringComparison.OrdinalIgnoreCase)>=0);
                if(result.GpuUsage.Status!=MetricStatus.Available)Assert.IsNull(result.GpuUsage.Value);
            }
        }
    }
}
#pragma warning restore 1591
