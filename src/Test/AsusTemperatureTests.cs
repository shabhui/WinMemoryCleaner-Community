using System;
using System.Reflection;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class AsusTemperatureTests
    {
        private static Type SourceType()
        {
            var type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.AsusCpuTemperatureSource");
            Assert.IsNotNull(type, "Existing ASUS CPU temperature support must not depend on a second sensor application.");
            return type;
        }
        private static object Call(string name, params object[] arguments)
        {
            var method = SourceType().GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, name);
            return method.Invoke(null, arguments);
        }
        [Test]
        public void Request_OnlyGetsTheIdentifiedCpuTemperature()
        {
            var request = (byte[])Call("CreateRequest");
            // DSTS (get status), eight argument bytes, CPU temperature 0x00120094, zero control argument.
            CollectionAssert.AreEqual(new byte[] { 0x44, 0x53, 0x54, 0x53, 8, 0, 0, 0, 0x94, 0, 0x12, 0, 0, 0, 0, 0 }, request);
        }
        [TestCase(0x00010001u, 1.0)]
        [TestCase(0x0001003Fu, 63.0)]
        [TestCase(0x00010096u, 150.0)]
        public void Response_RequiresPresentCpuSensorAndReportsCelsius(uint raw, double expected)
        {
            var result = (MetricValue)Call("DecodeResponse", BitConverter.GetBytes(raw), 4);
            Assert.AreEqual(MetricStatus.Available, result.Status);
            Assert.AreEqual(expected, result.Value);
            StringAssert.Contains("ASUS", result.Detail);
            StringAssert.Contains("CPU", result.Detail);
            StringAssert.Contains("0x00120094", result.Detail);
        }
        [TestCase(0x0000003Fu)]
        [TestCase(0x00010000u)]
        [TestCase(0x00010097u)]
        [TestCase(0x0003003Fu)]
        [TestCase(0xFFFFFFFFu)]
        public void Response_RejectsMissingFlagZeroOutOfRangeOrUnexpectedStatus(uint raw)
        {
            var result = (MetricValue)Call("DecodeResponse", BitConverter.GetBytes(raw), 4);
            Assert.AreNotEqual(MetricStatus.Available, result.Status);
            Assert.IsNull(result.Value);
            Assert.IsNotEmpty(result.Detail);
        }
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void Response_RejectsShortOrInconsistentReturnedLength(int returned)
        {
            var result = (MetricValue)Call("DecodeResponse", new byte[] { 0x3F, 0, 1, 0 }, returned);
            Assert.AreNotEqual(MetricStatus.Available, result.Status);
            Assert.IsNull(result.Value);
        }
        [Test]
        public void Response_RejectsNullBuffer()
        {
            var result = (MetricValue)Call("DecodeResponse", null, 4);
            Assert.AreNotEqual(MetricStatus.Available, result.Status);
            Assert.IsNull(result.Value);
        }
        [Test]
        public void NativeSampler_UsesExistingAsusSensorWhenItIsAvailable()
        {
            using (var source = (IDisposable)Activator.CreateInstance(SourceType()))
            {
                var reading = (MetricValue)source.GetType().GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);
                if (reading.Status != MetricStatus.Available)
                {
                    Assert.IsNull(reading.Value); Assert.IsNotEmpty(reading.Detail); return;
                }
                using (var sampler = new TemperatureSampler())
                {
                    sampler.ProviderRunning = delegate { return false; };
                    MetricValue cpu, gpu; sampler.Sample(out cpu, out gpu);
                    Assert.AreEqual(MetricStatus.Available, cpu.Status);
                    Assert.That(cpu.Value.Value, Is.InRange(1.0, 150.0));
                    StringAssert.Contains("ASUS", cpu.Detail);
                    Assert.AreEqual(0, sampler.WmiAttempts, "An available built-in CPU sensor must not load a missing external WMI provider.");
                    sampler.Suspend(); sampler.Sample(out cpu, out gpu);
                    Assert.AreEqual(MetricStatus.Available, cpu.Status, "Resuming must reopen the successful built-in sensor immediately.");
                }
            }
        }
    }
}
#pragma warning restore 1591
