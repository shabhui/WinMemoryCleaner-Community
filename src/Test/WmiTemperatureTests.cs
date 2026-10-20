using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class WmiTemperatureTests
    {
        private static SensorReading Reading(string type,string sensor,double value,string id)
        { return new SensorReading { HardwareType=type,HardwareId=id,DeviceName=id,SensorId=id+"/"+sensor,SensorName=sensor,SensorType="Temperature",Celsius=value }; }
        [Test]
        public void AbsentProvidersDoNotInvokeQueriesAndGatesAreIndependent()
        {
            int libre=0,open=0;
            var lhm=new WmiTemperatureSource("LibreHardwareMonitor",delegate { return false; },delegate { libre++;return new List<SensorReading>(); },Stopwatch.GetTimestamp);
            var ohm=new WmiTemperatureSource("OpenHardwareMonitor",delegate { return true; },delegate { open++;return new List<SensorReading> { Reading("GpuAmd","GPU Core",46,"GPU") }; },Stopwatch.GetTimestamp);
            using(lhm)using(ohm)
            {
                MetricValue cpu,gpu;lhm.Sample(out cpu,out gpu);Assert.AreEqual(MetricStatus.Unsupported,cpu.Status);StringAssert.Contains("not detected",cpu.Detail);Assert.AreEqual(0,libre);
                ohm.Sample(out cpu,out gpu);Assert.AreEqual(1,open);Assert.AreEqual(46,gpu.Value);StringAssert.Contains("OpenHardwareMonitor WMI",gpu.Detail);
            }
        }
        [Test]
        public void SharedSelectionRejectsHotspotAndDistanceAndPrefersPackage()
        {
            using(var source=new WmiTemperatureSource("LibreHardwareMonitor",delegate { return true; },delegate { return new List<SensorReading> { Reading("Cpu","CPU Package",54,"CPU"),Reading("Cpu","Core #1",80,"CPU"),Reading("Cpu","Distance to TjMax",99,"CPU"),Reading("GpuNvidia","GPU Hot Spot",110,"GPU One"),Reading("GpuAmd","GPU Core",46,"GPU Two") }; },Stopwatch.GetTimestamp))
            { MetricValue cpu,gpu;source.Sample(out cpu,out gpu);Assert.AreEqual(54,cpu.Value);StringAssert.Contains("CPU Package",cpu.Detail);Assert.AreEqual(46,gpu.Value);StringAssert.Contains("GPU Two",gpu.Detail); }
        }
        [Test]
        public void FailureBackoffDoesNotKeepLastValidValueAndResumesAtThirtySeconds()
        {
            long ticks=Stopwatch.Frequency;int calls=0;bool fail=false;
            using(var source=new WmiTemperatureSource("OpenHardwareMonitor",delegate { return true; },delegate { calls++;if(fail)throw new UnauthorizedAccessException("provider denied");return new List<SensorReading> { Reading("Cpu","CPU Package",54,"CPU") }; },delegate { return ticks; }))
            {
                MetricValue cpu,gpu;source.Sample(out cpu,out gpu);Assert.AreEqual(54,cpu.Value);fail=true;source.Sample(out cpu,out gpu);Assert.IsNull(cpu.Value);Assert.AreEqual(MetricStatus.AccessDenied,cpu.Status);
                ticks+=29*Stopwatch.Frequency;source.Sample(out cpu,out gpu);Assert.AreEqual(2,calls);Assert.IsNull(cpu.Value);
                fail=false;ticks+=Stopwatch.Frequency;source.Sample(out cpu,out gpu);Assert.AreEqual(3,calls);Assert.AreEqual(54,cpu.Value);
            }
        }
        [Test]
        public void PositiveDetectionIsCachedButNegativeAndBrokenChecksAreNotTrusted()
        {
            long ticks=Stopwatch.Frequency;int calls=0;bool present=true;bool broken=false;
            using(var source=new WmiTemperatureSource("LibreHardwareMonitor",delegate { calls++;if(broken)throw new InvalidOperationException("check failed");return present; },delegate { return new List<SensorReading>(); },delegate { return ticks; }))
            {
                Assert.IsTrue(source.ProviderExists());Assert.IsTrue(source.ProviderExists());Assert.AreEqual(1,calls);
                ticks+=30*Stopwatch.Frequency;present=false;Assert.IsFalse(source.ProviderExists());Assert.IsFalse(source.ProviderExists());Assert.AreEqual(3,calls);
                broken=true;Assert.IsTrue(source.ProviderExists());Assert.IsTrue(source.ProviderExists());Assert.AreEqual(5,calls);
            }
        }
        [Test]
        public void SuspendClearsRetryWithoutQueryingAndDisposedSourceStaysDisabled()
        {
            int calls=0;bool present=false;
            var source=new WmiTemperatureSource("OpenHardwareMonitor",delegate { return present; },delegate { calls++;return new List<SensorReading> { Reading("GpuNvidia","GPU Core",46,"GPU") }; },Stopwatch.GetTimestamp);
            try
            {
                MetricValue cpu,gpu;source.Sample(out cpu,out gpu);present=true;source.Sample(out cpu,out gpu);Assert.AreEqual(0,calls);
                source.Suspend();Assert.AreEqual(0,calls);source.Sample(out cpu,out gpu);Assert.AreEqual(1,calls);Assert.AreEqual(46,gpu.Value);
                source.Dispose();source.Sample(out cpu,out gpu);Assert.AreEqual(MetricStatus.Disabled,cpu.Status);Assert.IsNull(cpu.Value);Assert.AreEqual(1,calls);
            }
            finally { source.Dispose(); }
        }
    }
}
#pragma warning restore 1591
