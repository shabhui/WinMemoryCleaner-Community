using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class UniversalTemperatureTests
    {
        private static SensorReading Reading(string type,string device,string name,double? value,string id)
        {
            return new SensorReading { HardwareType=type,HardwareId=device,DeviceName=device,
                SensorType="Temperature",SensorName=name,SensorId=id,Celsius=value };
        }
        private static SensorReading Cpu(string name,double? value,string id)
        { return Reading("Cpu","/intelcpu/0",name,value,id); }
        private static SensorReading Pick(params SensorReading[] values)
        { return TemperatureSelection.Highest(TemperatureSelection.Select(values),true); }

        [Test]
        public void PackageWinsOverCoreAndDistance()
        {
            var chosen=Pick(Cpu("CPU Package",54,"/temperature/0"),Cpu("Core Max",80,"/temperature/1"),Cpu("Distance to TjMax",99,"/temperature/2"));
            Assert.IsNotNull(chosen);Assert.AreEqual(54,chosen.Celsius);Assert.AreEqual("Package",chosen.Measurement);
        }
        [Test]
        public void AmdTdieWinsWithoutInventingOffset()
        {
            var chosen=Pick(Cpu("Core (Tctl/Tdie)",73,"/combined"),Cpu("CPU Die (average)",72,"/average"),Cpu("Core (Tdie)",61,"/tdie"));
            Assert.IsNotNull(chosen);Assert.AreEqual(61,chosen.Celsius);Assert.AreEqual("Tdie",chosen.Measurement);
        }
        [TestCase("Core (Tctl/Tdie)")]
        [TestCase("CPU (Tctl/Tdie)")]
        public void CombinedTctlTdieRetainsReportedValue(string name)
        {
            var chosen=Pick(Cpu(name,73,"/combined"));
            Assert.IsNotNull(chosen);Assert.AreEqual(73,chosen.Celsius);Assert.AreEqual("Tctl/Tdie",chosen.Measurement);
        }
        [Test]
        public void CcdUsesHighestValidRecognizedDie()
        {
            var chosen=Pick(Cpu("CCD1 (Tdie)",55,"/ccd/1"),Cpu("CCD2 (Tdie)",66,"/ccd/2"),Cpu("CCD3 (Tdie) extra",120,"/bad"));
            Assert.IsNotNull(chosen);Assert.AreEqual(66,chosen.Celsius);Assert.AreEqual("CCD max (Tdie)",chosen.Measurement);
        }
        [TestCase("Core #1")]
        [TestCase("CPU Core #12")]
        [TestCase("Core Max")]
        public void CoreOnlyCpuUsesRecognizedCoreTemperature(string name)
        {
            var chosen=Pick(Cpu(name,52,"/a"),Cpu("Core #2",60,"/b"));
            Assert.IsNotNull(chosen);Assert.AreEqual(60,chosen.Celsius);Assert.AreEqual("Core max",chosen.Measurement);
        }
        [TestCase("Tctl")]
        [TestCase("Core (Tctl)")]
        [TestCase("Distance to TjMax")]
        [TestCase("CPU Package (Distance)")]
        [TestCase("CPU Core #1 extra")]
        [TestCase("CPU")]
        [TestCase("CCD (Tdie)")]
        [TestCase("CPU Package ")]
        public void UnrecognizedOrControlTemperatureCannotBecomeCpuReading(string name)
        { Assert.IsNull(Pick(Cpu(name,80,"/bad"))); }
        [Test]
        public void MultipleCpusSelectEachPackageBeforeGlobalMaximum()
        {
            var values=TemperatureSelection.Select(new[]{Cpu("CPU Package",54,"/a"),Cpu("Core Max",95,"/b"),Reading("Cpu","/amdcpu/1","CPU (Tdie)",68,"/c")});
            Assert.AreEqual(2,values.Count);Assert.AreEqual(68,TemperatureSelection.Highest(values,true).Celsius);
        }
        [Test]
        public void HotspotOnlyDeviceDoesNotWinOverRealGpuCore()
        {
            var values=TemperatureSelection.Select(new[]{Reading("GpuNvidia","/nvidia/0","GPU Core",46,"/a"),Reading("GpuAmd","/amd/1","GPU Hot Spot",110,"/b"),Reading("GpuAmd","/amd/1","Memory Junction",100,"/c")});
            var chosen=TemperatureSelection.Highest(values,false);
            Assert.AreEqual(1,values.Count);Assert.IsNotNull(chosen);Assert.AreEqual(46,chosen.Celsius);Assert.AreEqual("GPU core",chosen.Measurement);
        }
        [TestCase("GpuNvidia","GPU Core")]
        [TestCase("GpuAmd","GPU Core")]
        [TestCase("GpuIntel","GPU Temperature")]
        public void RecognizedGpuBrandsExposeOnlyTheirCore(string type,string name)
        {
            var values=TemperatureSelection.Select(new[]{Reading(type,"/gpu/0",name,47,"/core")});
            var chosen=TemperatureSelection.Highest(values,false);Assert.IsNotNull(chosen);Assert.AreEqual(47,chosen.Celsius);
        }
        [Test]
        public void MissingZeroNonFiniteAndOutOfRangeValuesAreExcluded()
        {
            foreach(var invalid in new double?[]{null,0,0.99,150.01,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                Assert.IsNull(Pick(Cpu("CPU Package",invalid,"/bad")),"Invalid temperature must never become an available zero or maximum.");
            var low=Pick(Cpu("CPU Package",1,"/low"));Assert.IsNotNull(low);Assert.AreEqual(1,low.Celsius);
            var high=Pick(Cpu("CPU Package",150,"/high"));Assert.IsNotNull(high);Assert.AreEqual(150,high.Celsius);
        }
        [Test]
        public void NonTemperatureAndUnidentifiedHardwareAreExcluded()
        {
            var reading=Cpu("CPU Package",65,"/a");reading.SensorType="Load";
            Assert.IsNull(Pick(reading));
            reading.SensorType="Temperature";reading.HardwareType="Motherboard";Assert.IsNull(Pick(reading));
            reading.HardwareType="GpuUnknown";Assert.IsNull(TemperatureSelection.Highest(TemperatureSelection.Select(new[]{reading}),false));
        }
        [Test]
        public void EqualValuesHaveOrdinalStableSourceAcrossInputOrder()
        {
            var a=Cpu("CPU Package",60,"/a");var b=Cpu("CPU Package",60,"/b");
            var first=Pick(b,a);var second=Pick(a,b);Assert.IsNotNull(first);Assert.IsNotNull(second);
            Assert.AreEqual("/a",first.SensorId);Assert.AreEqual("/a",second.SensorId);
            var other=Reading("Cpu","/intelcpu/1","CPU Package",60,"/first");
            Assert.AreEqual("/intelcpu/0",Pick(other,b,a).HardwareId);
        }
        [Test]
        public void SelectionIsOrdinalAndCultureIndependent()
        {
            var previous=Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach(string culture in new[]{"tr-TR","de-DE"})
                {
                    Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo(culture);
                    var chosen=Pick(Cpu("cpu package",54.5,"/a"));Assert.IsNotNull(chosen);Assert.AreEqual(54.5,chosen.Celsius);
                }
            }
            finally{Thread.CurrentThread.CurrentCulture=previous;}
        }
        [Test]
        public void SelectionDoesNotMutateCandidateOrCrossDeviceTypes()
        {
            var cpu=Cpu("CPU Package",56,"/a");
            var gpu=Reading("GpuIntel","/gpu/0","GPU Core",46,"/b");
            var values=TemperatureSelection.Select(new[]{cpu,gpu});
            Assert.AreEqual(2,values.Count);Assert.IsNull(cpu.Measurement);Assert.AreEqual(56,TemperatureSelection.Highest(values,true).Celsius);
        }
    }
}
#pragma warning restore 1591
