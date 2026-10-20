using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Sensors.Test
{
    /// <summary>Device-level failure isolation, driven by stand-ins for the pinned LHM hardware interfaces. No hardware is opened.</summary>
    [TestFixture]
    public sealed class BackendDeviceIsolationTests
    {
        private static SensorDriverState Ready() { return new SensorDriverState(true,true,new Version(2,1,0),"Ready"); }
        private static FakeSensor Temperature(string name,float? value) { return new FakeSensor(SensorType.Temperature,name,value); }
        private static FakeHardware Gpu(string name,HardwareType type,FakeSensor core,IHardware[] children)
        { return new FakeHardware(type,name,new ISensor[] { core },children); }
        private static LhmTemperatureSource Source(IHardware[] devices)
        {
            LhmTemperatureSource[] holder=new LhmTemperatureSource[1];
            holder[0]=new LhmTemperatureSource(Ready,delegate
            {
                var collected=new List<SensorReading>();
                foreach(IHardware device in devices)holder[0].ReadDevice(device,collected,0);
                return collected;
            },delegate { });
            return holder[0];
        }
        private static SensorReading Find(IList<SensorReading> readings,string device)
        {
            foreach(SensorReading reading in readings)if(String.Equals(reading.DeviceName,device,StringComparison.Ordinal))return reading;
            return null;
        }
        [Test]
        public void FailedDeviceContributesNothingWhileHealthyGpuKeepsItsCore()
        {
            FakeHardware healthy=Gpu("GPU One",HardwareType.GpuNvidia,Temperature("GPU Core",46),null);
            FakeHardware failing=Gpu("GPU Two",HardwareType.GpuAmd,Temperature("GPU Core",58),null);
            failing.UpdateError=new InvalidOperationException("device removed");
            using(LhmTemperatureSource source=Source(new IHardware[] { healthy,failing }))
            {
                SensorMessage message=source.Sample(true);
                Assert.AreEqual("Available",message.GpuStatus);
                Assert.AreEqual(1,message.Readings.Count);
                Assert.AreEqual("GPU One",message.Readings[0].DeviceName);
                Assert.AreEqual(46,message.Readings[0].Celsius);
                Assert.AreEqual(1,healthy.Updates);Assert.AreEqual(1,failing.Updates);
                StringAssert.Contains("GpuAmd update failed",message.Detail);
            }
        }
        [Test]
        public void FailedDeviceNeverReplaysItsPreviousValue()
        {
            FakeSensor healthyCore=Temperature("GPU Core",46);
            FakeSensor flakyCore=Temperature("GPU Core",58);
            FakeHardware healthy=Gpu("GPU One",HardwareType.GpuNvidia,healthyCore,null);
            FakeHardware flaky=Gpu("GPU Two",HardwareType.GpuAmd,flakyCore,null);
            using(LhmTemperatureSource source=Source(new IHardware[] { healthy,flaky }))
            {
                IList<SensorReading> first=source.Sample(true).Readings;
                Assert.AreEqual(2,first.Count);Assert.AreEqual(58,Find(first,"GPU Two").Celsius);
                healthyCore.Value=43;flakyCore.Value=44;flaky.UpdateError=new InvalidOperationException("gone");
                SensorMessage second=source.Sample(true);
                Assert.AreEqual("Available",second.GpuStatus);
                Assert.AreEqual(1,second.Readings.Count);
                Assert.AreEqual("GPU One",second.Readings[0].DeviceName);
                Assert.AreEqual(43,second.Readings[0].Celsius);
                Assert.IsNull(Find(second.Readings,"GPU Two"));
            }
        }
        [Test]
        public void FailingCpuDeviceIsReportedAsErrorWithoutLosingGpuCore()
        {
            FakeHardware cpu=new FakeHardware(HardwareType.Cpu,"CPU",new ISensor[] { Temperature("CPU Package",61) },null);
            cpu.UpdateError=new InvalidOperationException("no msr access");
            FakeHardware gpu=Gpu("GPU One",HardwareType.GpuNvidia,Temperature("GPU Core",44),null);
            using(LhmTemperatureSource source=Source(new IHardware[] { cpu,gpu }))
            {
                SensorMessage message=source.Sample(true);
                Assert.AreEqual("Error",message.CpuStatus);
                Assert.AreEqual("Available",message.GpuStatus);
                Assert.AreEqual(1,message.Readings.Count);
                Assert.AreEqual("GPU One",message.Readings[0].DeviceName);
                StringAssert.Contains("Cpu update failed",message.Detail);
            }
        }
        [Test]
        public void NonTemperatureSensorsAndUnrelatedHardwareAreIgnored()
        {
            FakeHardware gpu=new FakeHardware(HardwareType.GpuNvidia,"GPU One",new ISensor[] { new FakeSensor(SensorType.Load,"GPU Core Load",97),Temperature("GPU Core",45) },null);
            FakeHardware board=new FakeHardware(HardwareType.Motherboard,"Board",new ISensor[] { Temperature("Motherboard",39) },null);
            using(LhmTemperatureSource source=Source(new IHardware[] { gpu,board }))
            {
                SensorMessage message=source.Sample(true);
                Assert.AreEqual(1,message.Readings.Count);
                Assert.AreEqual("GPU One",message.Readings[0].DeviceName);
                Assert.AreEqual(45,message.Readings[0].Celsius);
                Assert.AreEqual("Temperature",message.Readings[0].SensorType);
                Assert.AreEqual(0,board.Updates);
            }
        }
        [Test]
        public void SubHardwareIsTraversedButNotBeyondSeventeenLevels()
        {
            FakeHardware[] chain=new FakeHardware[18];
            for(int depth=17;depth>=0;depth--)
            {
                IHardware[] children=depth==17?null:new IHardware[] { chain[depth+1] };
                chain[depth]=Gpu("Depth "+depth,HardwareType.GpuIntel,Temperature("GPU Core",40+depth),children);
            }
            using(LhmTemperatureSource source=Source(new IHardware[] { chain[0] }))
            {
                IList<SensorReading> readings=source.Sample(true).Readings;
                Assert.AreEqual(17,readings.Count);
                Assert.IsNotNull(Find(readings,"Depth 16"));
                Assert.IsNull(Find(readings,"Depth 17"));
            }
        }
        [Test]
        public void NullSensorsAndNullChildrenAreSkipped()
        {
            FakeHardware gpu=new FakeHardware(HardwareType.GpuNvidia,"GPU One",new ISensor[] { Temperature("GPU Core",45),null },new IHardware[] { null });
            using(LhmTemperatureSource source=Source(new IHardware[] { gpu,null }))
            {
                IList<SensorReading> readings=source.Sample(true).Readings;
                Assert.AreEqual(1,readings.Count);
                Assert.AreEqual(45,readings[0].Celsius);
            }
        }
        private sealed class FakeSensor : ISensor
        {
            private readonly Identifier _identifier;
            private readonly SensorType _type;
            private string _name;
            public FakeSensor(SensorType type,string name,float? value) { _type=type;_name=name;Value=value;_identifier=new Identifier("sensor",type.ToString(),name); }
            public float? Value { get; set; }
            public SensorType SensorType { get { return _type; } }
            public string Name { get { return _name; } set { _name=value; } }
            public Identifier Identifier { get { return _identifier; } }
            public IControl Control { get { return null; } }
            public IHardware Hardware { get { return null; } }
            public int Index { get { return 0; } }
            public bool IsDefaultHidden { get { return false; } }
            public float? Max { get { return null; } }
            public float? Min { get { return null; } }
            public IReadOnlyList<IParameter> Parameters { get { return Array.Empty<IParameter>(); } }
            public IEnumerable<SensorValue> Values { get { return Array.Empty<SensorValue>(); } }
            public TimeSpan ValuesTimeWindow { get; set; }
            public void Accept(IVisitor visitor) { }
            public void Traverse(IVisitor visitor) { }
            public void ResetMin() { }
            public void ResetMax() { }
            public void ClearValues() { }
        }
        private sealed class FakeHardware : IHardware
        {
            private readonly Identifier _identifier;
            private readonly HardwareType _type;
            private readonly ISensor[] _sensors;
            private readonly IHardware[] _children;
            private string _name;
            private int _updates;
            public FakeHardware(HardwareType type,string name,ISensor[] sensors,IHardware[] children)
            { _type=type;_name=name;_sensors=sensors??Array.Empty<ISensor>();_children=children??Array.Empty<IHardware>();_identifier=new Identifier("device",type.ToString(),name); }
            public Exception UpdateError { get; set; }
            public int Updates { get { return _updates; } }
            public HardwareType HardwareType { get { return _type; } }
            public Identifier Identifier { get { return _identifier; } }
            public string Name { get { return _name; } set { _name=value; } }
            public IHardware Parent { get { return null; } }
            public ISensor[] Sensors { get { return _sensors; } }
            public IHardware[] SubHardware { get { return _children; } }
            public IDictionary<string,string> Properties { get { return new Dictionary<string,string>(); } }
            public void Update() { _updates++;if(UpdateError!=null)throw UpdateError; }
            public string GetReport() { return String.Empty; }
            public void Accept(IVisitor visitor) { }
            public void Traverse(IVisitor visitor) { }
            public event SensorEventHandler SensorAdded { add { } remove { } }
            public event SensorEventHandler SensorRemoved { add { } remove { } }
        }
    }
}
#pragma warning restore 1591
