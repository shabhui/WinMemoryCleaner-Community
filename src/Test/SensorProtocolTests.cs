using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorProtocolTests
    {
        private static readonly DateTime Now=new DateTime(2026,10,18,6,0,0,DateTimeKind.Utc);
        private static SensorMessage Snapshot()
        {
            var message=new SensorMessage { Kind="snapshot",Session="123456789012345678901234567890ab",Sequence=1,SampleUtc=Now,CpuStatus="Available",GpuStatus="Unsupported",Detail="sensor status" };
            message.Readings.Add(new SensorReading { HardwareType="Cpu",HardwareId="/cpu/0",DeviceName="测试 CPU",SensorId="/cpu/0/temperature/0",SensorName="CPU Package",SensorType="Temperature",Celsius=54.5,Measurement="Package" });
            return message;
        }
        [Test]
        public void JsonRoundtripRetainsUnicodeAndInvariantNumericValue()
        {
            var old=Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
                string line=SensorProtocol.Encode(Snapshot());Assert.IsTrue(line.IndexOf("54.5",StringComparison.Ordinal)>=0);
                var parsed=SensorProtocol.Decode(line);Assert.IsNotNull(parsed);
                Assert.AreEqual(Snapshot().Session,parsed.Session);Assert.AreEqual(Now,parsed.SampleUtc);
                Assert.AreEqual(1,parsed.Readings.Count);Assert.AreEqual("测试 CPU",parsed.Readings[0].DeviceName);Assert.AreEqual(54.5,parsed.Readings[0].Celsius);
            }
            finally{Thread.CurrentThread.CurrentCulture=old;}
        }
        [Test]
        public void ConfigurationAndStopHaveOnlyFixedCommands()
        {
            var config=new SensorMessage { Kind="configure",Session=Snapshot().Session,RefreshSeconds=5,CpuEnabled=false };
            var parsed=SensorProtocol.Decode(SensorProtocol.Encode(config));Assert.IsNotNull(parsed);Assert.AreEqual(5,parsed.RefreshSeconds);Assert.IsFalse(parsed.CpuEnabled);
            config.Kind="stop";Assert.AreEqual("stop",SensorProtocol.Decode(SensorProtocol.Encode(config)).Kind);
            config.Kind="install";Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Encode(config); });
        }
        [TestCase("\"protocol\":1","\"protocol\":2")]
        [TestCase("\"protocol\":1","\"protocol\":\"1\"")]
        [TestCase("lhm-0.9.6-p1","unknown")]
        [TestCase("123456789012345678901234567890ab","other-session")]
        [TestCase("\"sequence\":1","\"sequence\":1.5")]
        public void IncompatibleOrMistypedEnvelopeTerminatesFrame(string before,string after)
        {
            string line=SensorProtocol.Encode(Snapshot()).Replace(before,after);
            Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Decode(line); });
        }
        [TestCase("{")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{\"__type\":\"System.Diagnostics.Process\"}")]
        public void MalformedOrNonObjectJsonIsNotDeserializedAsTypes(string line)
        { Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Decode(line); }); }
        [Test]
        public void OverlongAndOverdeepFramesAreRejectedBeforeDeserialization()
        {
            Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Decode(new string(' ',65537)); });
            Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Decode(new string('[',17)+"0"+new string(']',17)); });
            var message=Snapshot();for(int i=1;i<65;i++)message.Readings.Add(message.Readings[0]);
            Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Encode(message); });
        }
        [Test]
        public void BadIndividualValueDoesNotDiscardGoodGpuInSameFrame()
        {
            var message=Snapshot();message.Readings[0].Celsius=0;
            message.Readings.Add(new SensorReading { HardwareId="/gpu/0",HardwareType="GpuIntel",DeviceName="GPU",SensorId="/gpu/0/temperature/0",SensorName="GPU Core",SensorType="Temperature",Celsius=46 });
            var parsed=SensorProtocol.Decode(SensorProtocol.Encode(message));Assert.IsNotNull(parsed);Assert.AreEqual(1,parsed.Readings.Count);Assert.AreEqual(46,parsed.Readings[0].Celsius);
        }
        [Test]
        public void OverlongSensorMetadataDropsOnlyThatDevice()
        {
            var message=Snapshot();message.Readings[0].DeviceName=new string('x',257);
            var parsed=SensorProtocol.Decode(SensorProtocol.Encode(message));Assert.IsNotNull(parsed);Assert.AreEqual(0,parsed.Readings.Count);
        }
        [Test]
        public void ReaderRejectsOversizeBeforeNewlineAndDoesNotOwnStream()
        {
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(new string('a',65537))))
            {
                var reader=new BoundedLineReader(stream);
                Assert.Throws<InvalidDataException>(delegate { reader.ReadLine(); });
                Assert.IsTrue(stream.CanRead);Assert.LessOrEqual(stream.Position,69632);
            }
        }
        [Test]
        public void ReaderHandlesUnicodeCrlfMultipleFramesAndEmptyEof()
        {
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("测试\r\nsecond\n")))
            {
                var reader=new BoundedLineReader(stream);Assert.AreEqual("测试",reader.ReadLine());Assert.AreEqual("second",reader.ReadLine());Assert.IsNull(reader.ReadLine());
            }
        }
        [Test]
        public void ReaderRejectsInvalidUtf8AndTruncatedFrame()
        {
            using(var stream=new MemoryStream(new byte[]{255,10}))Assert.Throws<InvalidDataException>(delegate { new BoundedLineReader(stream).ReadLine(); });
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("partial")))Assert.Throws<InvalidDataException>(delegate { new BoundedLineReader(stream).ReadLine(); });
        }
        [Test]
        public void ReaderAcceptsUtf8BomOnlyAtStreamStart()
        {
            string frame=SensorProtocol.Encode(Snapshot());
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("\uFEFF"+frame+"\n\uFEFF"+frame+"\n")))
            {
                var reader=new BoundedLineReader(stream);Assert.AreEqual(frame,reader.ReadLine());
                Assert.Throws<InvalidDataException>(delegate { SensorProtocol.Decode(reader.ReadLine()); });
            }
        }
        [Test]
        public void ReaderCountsUtf8BomTowardRawFrameLimit()
        {
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("\uFEFF"+new string('a',SensorProtocol.MaximumFrameBytes-3)+"\n")))
                Assert.AreEqual(SensorProtocol.MaximumFrameBytes-3,new BoundedLineReader(stream).ReadLine().Length);
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("\uFEFF"+new string('a',SensorProtocol.MaximumFrameBytes-2)+"\n")))
                Assert.Throws<InvalidDataException>(delegate { new BoundedLineReader(stream).ReadLine(); });
        }
        [Test]
        public void ReaderRejectsInvalidUtf8AfterLeadingBom()
        {
            using(var stream=new MemoryStream(new byte[] { 0xEF,0xBB,0xBF,0xC3,0x28,10 }))
                Assert.Throws<InvalidDataException>(delegate { new BoundedLineReader(stream).ReadLine(); });
        }
        [Test]
        public void DuplicateAndOlderSequenceCannotRenewFreshness()
        {
            var fresh=new SensorFreshness(1000);var message=Snapshot();
            Assert.IsTrue(fresh.Accept(message,Now,1000,2));Assert.IsFalse(fresh.Accept(message,Now.AddSeconds(9),10000,2));
            message.Sequence=0;Assert.IsFalse(fresh.Accept(message,Now.AddSeconds(9),10000,2));
            Assert.IsFalse(fresh.IsFresh(Now.AddSeconds(11),12000,2));
        }
        [Test]
        public void FutureAndAlreadyExpiredFramesDoNotBecomeFresh()
        {
            var fresh=new SensorFreshness(1000);var message=Snapshot();
            message.SampleUtc=Now.AddSeconds(5.001);Assert.IsFalse(fresh.Accept(message,Now,1000,2));
            message.SampleUtc=Now.AddSeconds(-10.001);Assert.IsFalse(fresh.Accept(message,Now,1000,2));
            message.SampleUtc=Now.AddSeconds(-10);Assert.IsTrue(fresh.Accept(message,Now,1000,2));
        }
        [Test]
        public void FiveSecondIntervalAllowsFifteenSecondsNotMore()
        {
            var fresh=new SensorFreshness(1000);Assert.IsTrue(fresh.Accept(Snapshot(),Now,1000,5));
            Assert.IsTrue(fresh.IsFresh(Now.AddSeconds(15),16000,5));Assert.IsFalse(fresh.IsFresh(Now.AddSeconds(15.001),16001,5));
        }
        [Test]
        public void MonotonicClockPreventsWallClockRollbackFromRenewingOldSample()
        {
            var fresh=new SensorFreshness(1000);var message=Snapshot();message.SampleUtc=Now.AddSeconds(-9);
            Assert.IsTrue(fresh.Accept(message,Now,1000,2));Assert.IsFalse(fresh.IsFresh(Now.AddSeconds(-20),3000,2));
            fresh.Reset();Assert.IsFalse(fresh.IsFresh(Now,1000,2));
        }
    }
}
#pragma warning restore 1591
