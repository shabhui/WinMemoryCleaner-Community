using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LibreHardwareMonitor.Hardware;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Sensors.Test
{
    [TestFixture]
    [CLSCompliant(false)] // NUnit's params object[] test-case attributes are not CLS-compliant.
    public sealed class BackendAdapterTests
    {
        private static SensorDriverState Ready() { return new SensorDriverState(true,true,new Version(2,1,0),"Ready"); }
        private static SensorReading Reading(string type,string name,double value,string id)
        { return new SensorReading { HardwareType=type,HardwareId=id,DeviceName="Device "+id,SensorId=id+"/temperature/0",SensorName=name,SensorType="Temperature",Celsius=value }; }
        [Test]
        public void DefaultSettingsNeverReplayOrPersistSoftwareControl()
        {
            var settings=new DefaultSensorSettings();
            settings.SetValue("gpu/control/mode","Software");
            Assert.IsFalse(settings.Contains("gpu/control/mode"));
            Assert.AreEqual("Undefined",settings.GetValue("gpu/control/mode","Undefined"));
            settings.Remove("gpu/control/mode");
            Assert.AreEqual("default",settings.GetValue("anything","default"));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ComputerEnablesOnlyGpuAndExplicitlyAllowedCpu(bool cpu)
        {
            Computer computer=LhmTemperatureSource.CreateComputer(cpu);
            try
            {
                foreach(PropertyInfo property in typeof(Computer).GetProperties())
                {
                    if(property.PropertyType!=typeof(bool)||!property.Name.StartsWith("Is",StringComparison.Ordinal)||!property.Name.EndsWith("Enabled",StringComparison.Ordinal))continue;
                    bool expected=property.Name=="IsGpuEnabled"||(cpu&&property.Name=="IsCpuEnabled");
                    Assert.AreEqual(expected,(bool)property.GetValue(computer,null),property.Name);
                }
            }
            finally { computer.Close(); }
        }
        [TestCase(9,10,17763,461808,true)]
        [TestCase(9,10,22000,533320,true)]
        [TestCase(12,10,22000,533320,false)]
        [TestCase(0,10,22000,533320,false)]
        [TestCase(9,6,7601,533320,false)]
        [TestCase(9,10,17762,533320,false)]
        [TestCase(9,10,22000,461807,false)]
        [TestCase(9,10,22000,-1,false)]
        [TestCase(-1,-1,-1,-1,false)]
        public void RuntimeFactsFailClosedOutsideNativeAmd64Windows10AndNet472(int architecture,int major,int build,int release,bool expected)
        {
            string detail;
            Assert.AreEqual(expected,SensorRuntimeSupport.Evaluate(architecture,major,build,release,out detail));
            Assert.IsNotEmpty(detail);
            if(!expected)Assert.AreNotEqual("Ready",detail);
        }
        [Test]
        public void MissingDriverDisablesCpuButKeepsRealGpuCandidate()
        {
            bool? requested=null;
            using(var source=new LhmTemperatureSource(delegate { return new SensorDriverState(false,false,null,"NotInstalled"); },
                delegate(bool cpu) { requested=cpu;return new List<SensorReading> { Reading("Cpu","CPU Package",61,"cpu"),Reading("GpuAmd","GPU Core",46,"gpu") }; },delegate { }))
            {
                SensorMessage message=source.Sample(true);
                Assert.AreEqual(false,requested);Assert.AreEqual("Unsupported",message.CpuStatus);Assert.AreEqual("Available",message.GpuStatus);
                Assert.AreEqual(1,message.Readings.Count);Assert.AreEqual(46,message.Readings[0].Celsius);StringAssert.Contains("driver=NotInstalled",message.Detail);
            }
        }
        [Test]
        public void ParentCpuGateRemainsRequiredEvenWhenChildDriverIsReady()
        {
            bool? requested=null;
            using(var source=new LhmTemperatureSource(Ready,delegate(bool cpu) { requested=cpu;return new List<SensorReading> { Reading("Cpu","CPU Package",61,"cpu"),Reading("GpuNvidia","GPU Core",44,"gpu") }; },delegate { }))
            {
                SensorMessage message=source.Sample(false);
                Assert.AreEqual(false,requested);Assert.AreEqual("Disabled",message.CpuStatus);Assert.AreEqual(1,message.Readings.Count);Assert.AreEqual("GpuNvidia",message.Readings[0].HardwareType);
            }
        }
        [Test]
        public void TrustedCpuUsesPackageAndGpuUsesCoreNotHotspot()
        {
            using(var source=new LhmTemperatureSource(Ready,delegate(bool cpu)
                { Assert.IsTrue(cpu);return new List<SensorReading> { Reading("Cpu","CPU Package",54,"cpu"),Reading("Cpu","Core #1",80,"cpu"),Reading("GpuIntel","GPU Core",46,"gpu"),Reading("GpuIntel","GPU Hot Spot",110,"gpu") }; },delegate { }))
            {
                SensorMessage message=source.Sample(true);
                Assert.AreEqual("snapshot",message.Kind);Assert.AreEqual("Available",message.CpuStatus);Assert.AreEqual("Available",message.GpuStatus);
                Assert.AreEqual(2,message.Readings.Count);Assert.AreEqual(54,TemperatureSelection.Highest(message.Readings,true).Celsius);Assert.AreEqual(46,TemperatureSelection.Highest(message.Readings,false).Celsius);
                Assert.AreEqual(DateTimeKind.Utc,message.SampleUtc.Kind);
            }
        }
        [Test]
        public void HotspotOnlyAndZeroCannotProduceAvailableGpu()
        {
            using(var source=new LhmTemperatureSource(Ready,delegate { return new List<SensorReading> { Reading("GpuAmd","GPU Hot Spot",110,"gpu"),Reading("GpuNvidia","GPU Core",0,"gpu2") }; },delegate { }))
            { var message=source.Sample(true);Assert.AreEqual("Unsupported",message.GpuStatus);Assert.AreEqual(0,message.Readings.Count); }
        }
        [Test]
        public void AcquisitionFailureClearsPriorValuesAndReportsAccessDenial()
        {
            int calls=0;
            using(var source=new LhmTemperatureSource(Ready,delegate { if(++calls>1)throw new UnauthorizedAccessException();return new List<SensorReading> { Reading("GpuNvidia","GPU Core",44,"gpu") }; },delegate { }))
            {
                Assert.AreEqual("Available",source.Sample(true).GpuStatus);
                SensorMessage missing=source.Sample(true);Assert.AreEqual("AccessDenied",missing.GpuStatus);Assert.AreEqual(0,missing.Readings.Count);
            }
        }
        [Test]
        public void TooManyDevicesRemainWithinProtocolLimit()
        {
            var candidates=new List<SensorReading>();for(int i=0;i<70;i++)candidates.Add(Reading("GpuAmd","GPU Core",40+i,"gpu"+i));
            using(var source=new LhmTemperatureSource(Ready,delegate { return candidates; },delegate { }))
            { SensorMessage message=source.Sample(true);Assert.AreEqual(64,message.Readings.Count);message.Session="123456789012345678901234567890ab";Assert.DoesNotThrow(delegate { SensorProtocol.Encode(message); }); }
        }
        [Test]
        public void DisposeReleasesHardwareExactlyOnce()
        {
            int closed=0;var source=new LhmTemperatureSource(Ready,delegate { return new List<SensorReading>(); },delegate { closed++; });
            source.Dispose();source.Dispose();Assert.AreEqual(1,closed);Assert.Throws<ObjectDisposedException>(delegate { source.Sample(true); });
        }
    }
}
#pragma warning restore 1591
