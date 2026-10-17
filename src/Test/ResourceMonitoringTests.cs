using System;
using NUnit.Framework;
#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class ResourceMonitoringTests
    {
        [Test]
        public void PhysicalGpuIdentity_RejectsIndirectCopiesAndUncertainAdapters()
        {
            var identify = typeof(ResourceCalculation).GetMethod("IsPhysicalGpu");
            Assert.IsNotNull(identify, "A familiar GPU name is not proof of physical hardware.");
            Assert.AreEqual(true, identify.Invoke(null, new object[] { 0x10DEu, 0u, (uint?)0x231B, "NVIDIA GeForce RTX 4080 Laptop GPU" }));
            Assert.AreEqual(true, identify.Invoke(null, new object[] { 0x8086u, 0u, (uint?)0x2323, "Intel(R) UHD Graphics" }));
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 0u, (uint?)0x342, "NVIDIA GeForce RTX 4080 Laptop GPU" }), "Indirect displays borrow a physical GPU's name and vendor.");
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 1u, (uint?)0x231B, "NVIDIA GPU" }), "Remote DXGI adapter.");
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 2u, (uint?)0x231B, "NVIDIA GPU" }), "Software DXGI adapter.");
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 0u, (uint?)0x83, "NVIDIA GPU" }), "Paravirtualized render adapter.");
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 0u, null, "NVIDIA GPU" }), "Unknown native device type must not claim physical hardware.");
            Assert.AreEqual(false, identify.Invoke(null, new object[] { 0x10DEu, 0u, (uint?)0x231B, "Virtual GPU" }));
        }

        [Test]
        public void ProcessCpu_FirstFrameAndInvalidDeltasAreUnavailable()
        {
            Assert.IsNull(ResourceCalculation.ProcessCpu(null,100,2,4));
            Assert.IsNull(ResourceCalculation.ProcessCpu(200,100,2,4));
            Assert.IsNull(ResourceCalculation.ProcessCpu(100,200,0,4));
            Assert.IsNull(ResourceCalculation.ProcessCpu(100,200,2,0));
        }
        [Test]
        public void ProcessCpu_UsesWholeMachineNormalizationAndKeepsRealIdleZero()
        {
            Assert.AreEqual(25,ResourceCalculation.ProcessCpu(0,20000000,2,4));
            Assert.AreEqual(0,ResourceCalculation.ProcessCpu(100,100,2,4));
            Assert.AreEqual(100,ResourceCalculation.ProcessCpu(0,900000000,1,4));
        }
        [Test]
        public void SystemCpu_AccountsForKernelIncludingIdleAndRejectsReset()
        {
            Assert.IsNull(ResourceCalculation.SystemCpu(null,null,null,10,20,30));
            Assert.AreEqual(60,ResourceCalculation.SystemCpu(100,200,300,140,280,320));
            Assert.IsNull(ResourceCalculation.SystemCpu(100,200,300,90,280,320));
            Assert.IsNull(ResourceCalculation.SystemCpu(100,200,300,100,200,300));
        }
        [Test]
        public void MissingMetricHasNoNumberAndInvalidSensorValueIsError()
        {
            Assert.IsNull(MetricValue.Missing(MetricStatus.AccessDenied,"denied").Value);
            Assert.AreEqual(MetricStatus.Error,MetricValue.Available(double.NaN,"driver").Status);
            Assert.AreEqual(0,MetricValue.Available(0,"counter").Value);
            Assert.Throws<ArgumentException>(delegate { MetricValue.Missing(MetricStatus.Available,"invalid"); });
        }
        private static string Engine(int pid,int engine)
        {
            return "pid_"+pid+"_luid_0x00000000_0x0000007b_phys_0_eng_"+engine+"_engtype_3D";
        }
        [Test]
        public void Gpu_SumsProcessesSharingAnEngineButUsesBusiestEngineNotSumOfEngines()
        {
            var gpu=new GpuAggregation(new ulong[]{123});
            gpu.AddEngine(Engine(10,0),40); gpu.AddEngine(Engine(20,0),30); gpu.AddEngine(Engine(10,1),60);
            Assert.AreEqual(70,gpu.Usage);
            Assert.AreEqual(60,gpu.ProcessUsage[10]);
            Assert.AreEqual(30,gpu.ProcessUsage[20]);
        }
        [Test]
        public void Gpu_IgnoresVirtualAdaptersInvalidValuesAndDuplicateInstances()
        {
            var gpu=new GpuAggregation(new ulong[]{123});
            gpu.AddEngine(Engine(10,0),40); gpu.AddEngine(Engine(10,0),40);
            gpu.AddEngine("pid_20_luid_0x00000000_0x000000ff_phys_0_eng_0_engtype_3D",90);
            gpu.AddEngine(Engine(30,0),double.NaN); gpu.AddEngine(Engine(40,0),-1);
            Assert.AreEqual(40,gpu.Usage); Assert.AreEqual(1,gpu.ProcessUsage.Count);
            gpu.AddEngine(Engine(20,0),95); Assert.AreEqual(100,gpu.Usage);
        }
        [Test]
        public void Gpu_DedicatedMemoryUsesIdentifiedPhysicalAdaptersAndDoesNotDoubleCount()
        {
            var gpu=new GpuAggregation(new ulong[]{123,124});
            const string a="pid_10_luid_0x00000000_0x0000007b_phys_0";
            gpu.AddMemory(a,1000); gpu.AddMemory(a,1000);
            gpu.AddMemory("pid_10_luid_0x00000000_0x0000007c_phys_0",2000);
            gpu.AddMemory("pid_10_luid_0x00000000_0x000000ff_phys_0",9000);
            Assert.AreEqual(3000,gpu.ProcessMemory[10]);
        }
    }
}
#pragma warning restore 1591
