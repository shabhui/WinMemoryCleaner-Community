using System;
using System.Collections.Generic;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class TemperatureCoordinationTests
    {
        private static MetricValue Value(double value,string source) { return MetricValue.Available(value,source); }
        private static MetricValue Missing(string detail) { return MetricValue.Missing(MetricStatus.Unsupported,detail); }
        private sealed class Source : ITemperatureSource
        {
            internal MetricValue Cpu=Missing("no CPU"),Gpu=Missing("no GPU");
            internal int Samples,Suspends,Disposals;
            internal Exception Error;
            public void Sample(out MetricValue cpu,out MetricValue gpu) { Samples++;if(Error!=null)throw Error;cpu=Cpu;gpu=Gpu; }
            public void Suspend() { Suspends++; }
            public void Dispose() { Disposals++; }
        }
        private sealed class Backend : ISensorBackend
        {
            internal MetricValue Cpu=Missing("universal CPU unavailable"),Gpu=Missing("universal GPU unavailable");
            internal int Samples,Refresh,Suspends,Resets,Disposals;
            public string BackendStatus { get { return "Running"; } }
            public string DriverStatus { get { return "KeepExistingDriver"; } }
            public int ProcessId { get { return 123; } }
            public string Diagnostic { get { return "backend diagnostic"; } }
            public void GetSnapshot(out MetricValue cpu,out MetricValue gpu) { Samples++;cpu=Cpu;gpu=Gpu; }
            public void Configure(int seconds) { Refresh=seconds; }
            public void Suspend() { Suspends++; }
            public void ResetDriverState() { Resets++; }
            public void Dispose() { Disposals++; }
        }
        [Test]
        public void UniversalWinsAndAvoidsUnneededNativeAndWmiWork()
        {
            var backend=new Backend { Cpu=Value(54,"Universal CPU"),Gpu=Value(46,"Universal GPU") };
            var native=new Source { Cpu=Value(90,"ASUS CPU"),Gpu=Value(80,"NVAPI GPU") };var lhm=new Source();var ohm=new Source();
            using(var sampler=new TemperatureSampler(backend,native,lhm,ohm))
            {
                MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);
                Assert.AreEqual(54,cpu.Value);Assert.AreEqual("Universal CPU",cpu.Detail);Assert.AreEqual(46,gpu.Value);
                Assert.AreEqual(1,backend.Samples);Assert.AreEqual(0,native.Samples);Assert.AreEqual(0,lhm.Samples);Assert.AreEqual(0,ohm.Samples);
            }
        }
        [Test]
        public void CpuFallsBackIndependentlyWhileUniversalGpuStaysSelected()
        {
            var backend=new Backend { Cpu=MetricValue.Missing(MetricStatus.AccessDenied,"official CPU driver not trusted"),Gpu=Value(46,"Universal GPU") };
            var native=new Source { Cpu=Value(58,"ASUS ATKACPI"),Gpu=Value(99,"NVAPI GPU") };var lhm=new Source();var ohm=new Source();
            using(var sampler=new TemperatureSampler(backend,native,lhm,ohm))
            {
                MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);
                Assert.AreEqual(58,cpu.Value);StringAssert.Contains("ASUS ATKACPI",cpu.Detail);StringAssert.Contains("official CPU driver not trusted",cpu.Detail);
                Assert.AreEqual(46,gpu.Value);Assert.AreEqual("Universal GPU",gpu.Detail);Assert.AreEqual(0,lhm.Samples);Assert.AreEqual(0,ohm.Samples);
            }
        }
        [Test]
        public void HealthyNativeStillProbesUniversalOnEveryRequestedSample()
        {
            var backend=new Backend();var native=new Source { Cpu=Value(58,"ASUS"),Gpu=Value(48,"NVAPI") };
            using(var sampler=new TemperatureSampler(backend,native,new Source(),new Source()))
            {
                MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);Assert.AreEqual(58,cpu.Value);Assert.AreEqual(48,gpu.Value);
                backend.Cpu=Value(54,"Universal CPU");backend.Gpu=Value(46,"Universal GPU");sampler.Sample(out cpu,out gpu);
                Assert.AreEqual(2,backend.Samples);Assert.AreEqual(54,cpu.Value);Assert.AreEqual(46,gpu.Value);Assert.AreEqual(1,native.Samples);
            }
        }
        [Test]
        public void ExternalWmiSourcesHaveIndependentPriorityPerMetric()
        {
            var backend=new Backend();var native=new Source();var lhm=new Source { Cpu=Value(54,"LibreHardwareMonitor WMI") };var ohm=new Source { Cpu=Value(90,"OpenHardwareMonitor CPU"),Gpu=Value(46,"OpenHardwareMonitor WMI") };
            using(var sampler=new TemperatureSampler(backend,native,lhm,ohm))
            {
                MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);
                Assert.AreEqual(54,cpu.Value);Assert.AreEqual(46,gpu.Value);StringAssert.Contains("OpenHardwareMonitor WMI",gpu.Detail);
                Assert.AreEqual(1,lhm.Samples);Assert.AreEqual(1,ohm.Samples);
            }
        }
        [Test]
        public void FailedOrExpiredSourcesNeverReplayEarlierReadings()
        {
            var backend=new Backend { Cpu=Value(54,"Universal CPU"),Gpu=Value(46,"Universal GPU") };var native=new Source();var lhm=new Source();var ohm=new Source();
            using(var sampler=new TemperatureSampler(backend,native,lhm,ohm))
            {
                MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);
                backend.Cpu=MetricValue.Missing(MetricStatus.Error,"status frames stopped arriving");backend.Gpu=backend.Cpu;native.Error=new InvalidOperationException("native failure");
                sampler.Sample(out cpu,out gpu);Assert.IsNull(cpu.Value);Assert.IsNull(gpu.Value);Assert.AreEqual(MetricStatus.Error,cpu.Status);
                StringAssert.Contains("status frames stopped arriving",cpu.Detail);StringAssert.Contains("native failure",cpu.Detail);StringAssert.Contains("no CPU",cpu.Detail);
            }
        }
        [TestCase(0.0)]
        [TestCase(150.01)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidAvailableSourceCannotBecomeTemperature(double invalid)
        {
            var backend=new Backend { Cpu=Value(invalid,"invalid universal"),Gpu=Value(46,"Universal GPU") };var native=new Source { Cpu=Value(58,"ASUS") };
            using(var sampler=new TemperatureSampler(backend,native,new Source(),new Source()))
            { MetricValue cpu,gpu;sampler.Sample(out cpu,out gpu);Assert.AreEqual(58,cpu.Value);StringAssert.Contains("INVALID",cpu.Detail.ToUpperInvariant());Assert.AreEqual(46,gpu.Value); }
        }
        [Test]
        public void ConfigurationStatusAndOwnedLifecycleAreForwardedWithoutSampling()
        {
            var backend=new Backend();var native=new Source();var lhm=new Source();var ohm=new Source();var sampler=new TemperatureSampler(backend,native,lhm,ohm);
            try
            {
                sampler.Configure(99);Assert.AreEqual(2,backend.Refresh);sampler.Configure(5);Assert.AreEqual(5,backend.Refresh);
                Assert.AreEqual("Running",sampler.BackendStatus);Assert.AreEqual("KeepExistingDriver",sampler.DriverStatus);Assert.AreEqual(123,sampler.ProcessId);
                sampler.ResetDriverState();Assert.AreEqual(1,backend.Resets);sampler.Suspend();Assert.AreEqual(1,backend.Suspends);Assert.AreEqual(1,native.Suspends);Assert.AreEqual(1,lhm.Suspends);Assert.AreEqual(1,ohm.Suspends);
                Assert.AreEqual(0,backend.Samples);Assert.AreEqual(0,native.Samples);
            }
            finally { sampler.Dispose();sampler.Dispose(); }
            Assert.AreEqual(1,backend.Disposals);Assert.AreEqual(1,native.Disposals);Assert.AreEqual(1,lhm.Disposals);Assert.AreEqual(1,ohm.Disposals);
        }
    }
}
#pragma warning restore 1591
