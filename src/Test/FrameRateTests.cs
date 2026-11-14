using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;

// Reflection deliberately lets the absence build compile: RED must be assertion failures,
// not missing-type compiler errors. Doubles replace only native/identity/foreground boundaries.
#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class FrameRateTests
    {
        private static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type CoreType(string name)
        {
            Type type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner." + name);
            Assert.IsNotNull(type, "Missing production FPS type: " + name);
            return type;
        }

        private static object Construct(string name, params object[] arguments)
        {
            return Activator.CreateInstance(CoreType(name), InstanceMembers, null, arguments, CultureInfo.InvariantCulture);
        }

        private static object Call(object instance, string name, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(name, InstanceMembers);
            Assert.IsNotNull(method, "Missing production member: " + name);
            return method.Invoke(instance, arguments);
        }

        private static T Property<T>(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, InstanceMembers);
            Assert.IsNotNull(property, "Missing reading property: " + name);
            return (T)property.GetValue(instance, null);
        }

        private static object Field(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, InstanceMembers);
            Assert.IsNotNull(field, "Missing bounded storage: " + name);
            return field.GetValue(instance);
        }

        private static object Accumulator()
        {
            return Construct("FramePresentAccumulator", 60000L, 71, 0L);
        }

        private static string[] StartNames(Guid provider)
        {
            return provider == Dxgi
                ? new[] { "pIDXGISwapChain", "Flags", "SyncInterval" }
                : new[] { "pSwapchain", "Flags" };
        }

        private static int[] StartTypes(Guid provider)
        {
            return provider == Dxgi ? new[] { 16, 8, 8 } : new[] { 16, 8 };
        }

        // TDH scalar types: POINTER=16, UINT32=8. These literals are independent
        // fixtures; payload order is described by metadata, never guessed by the core.
        private static byte[] StartPayload(Guid provider, int pointerSize, ulong swapchain, uint flags)
        {
            byte[] result = new byte[pointerSize + (provider == Dxgi ? 8 : 4)];
            byte[] pointer = pointerSize == 8 ? BitConverter.GetBytes(swapchain) : BitConverter.GetBytes((uint)swapchain);
            Array.Copy(pointer, 0, result, 0, pointerSize);
            Array.Copy(BitConverter.GetBytes(flags), 0, result, pointerSize, 4);
            if (provider == Dxgi) Array.Copy(BitConverter.GetBytes(1u), 0, result, pointerSize + 4, 4);
            return result;
        }

        private static void Start(object accumulator, Guid provider, int eventId, int pointerSize, int processId, int threadId, long timestamp, ulong swapchain, uint flags)
        {
            Call(accumulator, "Accept", provider, eventId, 0, processId, threadId, timestamp, pointerSize,
                StartPayload(provider, pointerSize, swapchain, flags), StartNames(provider), StartTypes(provider));
        }

        private static void Stop(object accumulator, Guid provider, int eventId, int pointerSize, int processId, int threadId, long timestamp, int result)
        {
            Call(accumulator, "Accept", provider, eventId, 0, processId, threadId, timestamp, pointerSize,
                BitConverter.GetBytes(result), new[] { "Result" }, new[] { 7 });
        }

        private static void Present(object accumulator, Guid provider, int startId, int stopId, int pointerSize, long timestamp, ulong swapchain, uint flags, int result)
        {
            Start(accumulator, provider, startId, pointerSize, 71, 17, timestamp, swapchain, flags);
            Stop(accumulator, provider, stopId, pointerSize, 71, 17, timestamp + 10, result);
        }

        private static object Reading(object accumulator, long now)
        {
            return Call(accumulator, "Read", now);
        }

        private static void Missing(object reading, string state)
        {
            Assert.AreEqual(state, Property<string>(reading, "StateKey"));
            MetricValue metric = Property<MetricValue>(reading, "Metric");
            Assert.AreNotEqual(MetricStatus.Available, metric.Status);
            Assert.IsNull(metric.Value, "Unavailable FPS must never be 0, 60, refresh rate, or a stale number.");
        }

        private static void Available(object reading, double expected)
        {
            Assert.AreEqual("FpsAvailable", Property<string>(reading, "StateKey"));
            MetricValue metric = Property<MetricValue>(reading, "Metric");
            Assert.AreEqual(MetricStatus.Available, metric.Status);
            Assert.IsTrue(metric.Value.HasValue);
            Assert.AreEqual(expected, metric.Value.Value, 0.000001);
        }

        private static ProcessSnapshot Target(int id, long creation, string name)
        {
            return new ProcessSnapshot { Id = id, CreationTime = creation, Name = name };
        }

        [Test]
        public void ReadingRejectsNullMetricAndExposesReadOnlyIntegrationContract()
        {
            Type reading = CoreType("FrameRateReading");
            ConstructorInfo constructor = reading.GetConstructor(new[] { typeof(MetricValue), typeof(string), typeof(string) });
            Assert.IsNotNull(constructor);
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(delegate
            {
                constructor.Invoke(new object[] { null, "game", "FpsWarmingUp" });
            });
            Assert.IsInstanceOf<ArgumentNullException>(error.InnerException);
            foreach (string name in new[] { "Metric", "TargetName", "StateKey" })
                Assert.IsNull(reading.GetProperty(name).GetSetMethod(), "The integration reading cannot be externally mutated.");
            Type contract = CoreType("IFrameRateMonitor");
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(contract));
            Assert.AreEqual(reading, contract.GetMethod("Sample").ReturnType);
            Assert.IsNotNull(contract.GetMethod("SetActive", new[] { typeof(bool) }));
            Assert.IsNotNull(contract.GetMethod("SetTarget", new[] { typeof(bool), typeof(ProcessSnapshot) }));
        }

        [TestCase(4)]
        [TestCase(8)]
        public void DxgiSuccessfulPairsUseStartQpcAndProduceSixtyFps(int pointerSize)
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++) Present(accumulator, Dxgi, 42, 43, pointerSize, i * 1000L, 0x12345678UL, 0, 0);
            Available(Reading(accumulator, 60010), 60.0);
        }

        [TestCase(4)]
        [TestCase(8)]
        public void D3d9SuccessfulPairsUseTheirVerifiedPointerPayload(int pointerSize)
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++) Present(accumulator, D3d9, 1, 2, pointerSize, i * 1000L, 0xF2345678UL, 0, 0);
            Available(Reading(accumulator, 60010), 60.0);
        }

        [TestCase(4)]
        [TestCase(8)]
        public void DxgiMpoApplicationPresentsArePairedSeparately(int pointerSize)
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++) Present(accumulator, Dxgi, 55, 56, pointerSize, i * 1000L, 123, 0, 0);
            Available(Reading(accumulator, 60010), 60.0);
        }

        [Test]
        public void NoEventsNeverInventsAnyRateAndEventuallyReportsNoFrames()
        {
            object accumulator = Accumulator();
            Missing(Reading(accumulator, 0), "FpsWarmingUp");
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void OneValidatedEventCannotWarmUp()
        {
            object accumulator = Accumulator();
            Present(accumulator, Dxgi, 42, 43, 8, 1000, 1, 0, 0);
            Missing(Reading(accumulator, 1010), "FpsWarmingUp");
        }

        [Test]
        public void OldFramesBecomeUnavailableInsteadOfRetainingTheirNumber()
        {
            object accumulator = Accumulator();
            Present(accumulator, Dxgi, 42, 43, 8, 0, 1, 0, 0);
            Present(accumulator, Dxgi, 42, 43, 8, 1000, 1, 0, 0);
            Available(Reading(accumulator, 1010), 60.0);
            Missing(Reading(accumulator, 62001), "FpsNoFrames");
        }

        [Test]
        public void DominantSwapchainDoesNotSumASecondaryRenderingSurface()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 1, 0, 0);
                if (i % 10 == 0) Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 2, 0, 0);
            }
            Available(Reading(accumulator, 60010), 60.0);
        }

        [Test]
        public void Full64BitSwapchainIdentityCannotCollideOnLow32Bits()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 0x0000000100000001UL, 0, 0);
                if (i % 10 == 0) Present(accumulator, Dxgi, 42, 43, 8, i * 1000L + 100, 0x0000000200000001UL, 0, 0);
            }
            Available(Reading(accumulator, 60110), 60.0);
        }

        [TestCase(-1)]
        [TestCase(-2005270527)]
        [TestCase(142213121)]
        [TestCase(1)]
        public void FailedOccludedOrNonSOkStopsDoNotBecomeFrames(int result)
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++) Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 1, 0, result);
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void DxgiTestAndD3d9DoNotFlipPresentsDoNotBecomeFrames()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 1, 1, 0);
                Present(accumulator, D3d9, 1, 2, 8, i * 1000L, 2, 4, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void StopsRequireMatchingProviderThreadPidAndEventFamily()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Start(accumulator, Dxgi, 42, 8, 71, 17, i * 1000L, 1, 0);
                Stop(accumulator, Dxgi, 43, 8, 72, 17, i * 1000L + 1, 0);
                Stop(accumulator, Dxgi, 43, 8, 71, 18, i * 1000L + 2, 0);
                Stop(accumulator, D3d9, 2, 8, 71, 17, i * 1000L + 3, 0);
                Stop(accumulator, Dxgi, 56, 8, 71, 17, i * 1000L + 4, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void UnpairedStopsAndForeignProvidersAreIgnored()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Stop(accumulator, Dxgi, 43, 8, 71, 17, i * 1000L, 0);
                Present(accumulator, Guid.Empty, 42, 43, 8, i * 1000L, 1, 0, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void UnknownRelevantVersionFailsClosed()
        {
            object accumulator = Accumulator();
            Call(accumulator, "Accept", Dxgi, 42, 1, 71, 17, 0L, 8,
                StartPayload(Dxgi, 8, 1, 0), StartNames(Dxgi), StartTypes(Dxgi));
            Missing(Reading(accumulator, 0), "FpsUnsupported");
        }

        [Test]
        public void UnknownSchemaCannotUseGuessedPayloadOffsets()
        {
            object accumulator = Accumulator();
            Call(accumulator, "Accept", Dxgi, 42, 0, 71, 17, 0L, 8,
                StartPayload(Dxgi, 8, 1, 0), new[] { "UnknownPointer", "Flags", "SyncInterval" }, StartTypes(Dxgi));
            Missing(Reading(accumulator, 0), "FpsUnsupported");
        }

        [Test]
        public void MetadataDefinesScalarOrderRatherThanTheFixtureBuilder()
        {
            object accumulator = Accumulator();
            byte[] payload = { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 };
            for (int i = 0; i < 61; i++)
            {
                Call(accumulator, "Accept", Dxgi, 42, 0, 71, 17, i * 1000L, 8, payload,
                    new[] { "Flags", "pIDXGISwapChain", "SyncInterval" }, new[] { 8, 16, 8 });
                Stop(accumulator, Dxgi, 43, 8, 71, 17, i * 1000L + 10, 0);
            }
            Available(Reading(accumulator, 60010), 60.0);
        }

        [Test]
        public void MalformedNullAndOverlongPayloadsCannotProduceFrames()
        {
            object accumulator = Accumulator();
            foreach (byte[] payload in new[] { null, new byte[3], new byte[17] })
            {
                Call(accumulator, "Accept", Dxgi, 42, 0, 71, 17, 0L, 8, payload, StartNames(Dxgi), StartTypes(Dxgi));
                Stop(accumulator, Dxgi, 43, 8, 71, 17, 10, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void ARejectedStopCannotLeaveAnOlderStartForAnotherStop()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Start(accumulator, Dxgi, 42, 8, 71, 17, i * 1000L, 1, 0);
                Call(accumulator, "Accept", Dxgi, 43, 0, 71, 17, i * 1000L + 5, 8,
                    new byte[3], new[] { "Result" }, new[] { 7 });
                Stop(accumulator, Dxgi, 43, 8, 71, 17, i * 1000L + 10, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void ReversedQpcAndMismatchedPointerHeadersCannotBePaired()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++)
            {
                Start(accumulator, Dxgi, 42, 8, 71, 17, i * 1000L + 10, 1, 0);
                Stop(accumulator, Dxgi, 43, 8, 71, 17, i * 1000L, 0);
                Start(accumulator, Dxgi, 42, 8, 71, 17, i * 1000L, 1, 0);
                Stop(accumulator, Dxgi, 43, 4, 71, 17, i * 1000L + 20, 0);
            }
            Missing(Reading(accumulator, 120001), "FpsNoFrames");
        }

        [Test]
        public void DuplicateQpcCannotInventAnInfiniteOrZeroRate()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 61; i++) Present(accumulator, Dxgi, 42, 43, 8, 1000, 1, 0, 0);
            Missing(Reading(accumulator, 1010), "FpsWarmingUp");
        }

        [Test]
        public void FreshEpochRejectsPreSelectionReplay()
        {
            object accumulator = Construct("FramePresentAccumulator", 60000L, 71, 30000L);
            for (int i = 0; i < 30; i++) Present(accumulator, Dxgi, 42, 43, 8, i * 1000L, 1, 0, 0);
            Missing(Reading(accumulator, 30010), "FpsWarmingUp");
            Present(accumulator, Dxgi, 42, 43, 8, 30000, 1, 0, 0);
            Missing(Reading(accumulator, 30010), "FpsWarmingUp");
            Present(accumulator, Dxgi, 42, 43, 8, 31000, 1, 0, 0);
            Available(Reading(accumulator, 31010), 60.0);
        }

        [Test]
        public void TimestampRetentionRemainsBoundedAtHighPresentRates()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 20000; i++) Present(accumulator, Dxgi, 42, 43, 8, i * 10L, 1, 0, 0);
            Available(Reading(accumulator, 200000), 6000.0);
            IDictionary chains = (IDictionary)Field(accumulator, "_chains");
            int retained = 0;
            foreach (object window in chains.Values) retained += (int)Field(window, "_count");
            Assert.LessOrEqual(retained, 8192);
            Assert.LessOrEqual(chains.Count, 16);
            Assert.LessOrEqual(((IDictionary)Field(accumulator, "_pending")).Count, 256);
            Missing(Reading(accumulator, 400001), "FpsNoFrames");
            foreach (object window in chains.Values) Assert.AreEqual(0, (int)Field(window, "_count"));
        }

        [Test]
        public void PendingOverflowInvalidatesRatherThanSilentlyUndercounting()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 300; i++) Start(accumulator, Dxgi, 42, 8, 71, i + 1, i, 1, 0);
            Missing(Reading(accumulator, 1000), "FpsEventLoss");
            Assert.LessOrEqual(((IDictionary)Field(accumulator, "_pending")).Count, 256);
        }

        [Test]
        public void ExpiredPendingStartsDoNotGrowOrPairAfterTheirBoundedLifetime()
        {
            object accumulator = Accumulator();
            for (int i = 0; i < 256; i++) Start(accumulator, Dxgi, 42, 8, 71, i + 1, 0, 1, 0);
            Start(accumulator, Dxgi, 42, 8, 71, 500, 120001, 1, 0);
            Stop(accumulator, Dxgi, 43, 8, 71, 500, 120011, 0);
            Stop(accumulator, Dxgi, 43, 8, 71, 1, 120020, 0);
            Missing(Reading(accumulator, 120020), "FpsWarmingUp");
            Assert.AreEqual(0, ((IDictionary)Field(accumulator, "_pending")).Count);
        }

        [Test]
        public void ExcessRecentSwapchainsFailClosedWithinTheRetentionBound()
        {
            object accumulator = Accumulator();
            for (ulong i = 1; i <= 17; i++) Present(accumulator, Dxgi, 42, 43, 8, 1000, i, 0, 0);
            Missing(Reading(accumulator, 1010), "FpsEventLoss");
            Assert.LessOrEqual(((IDictionary)Field(accumulator, "_chains")).Count, 16);
        }

        [Test]
        public void DefaultInactiveMonitorDoesNotQueueOrOpenNativeCapture()
        {
            using (IDisposable monitor = (IDisposable)Construct("FrameRateMonitor"))
            {
                Missing(Call(monitor, "Sample"), "FpsDisabled");
                Assert.IsFalse((bool)Field(monitor, "_workerScheduled"));
                Assert.IsNull(Field(monitor, "_capture"));
            }
        }

        [Test]
        public void InactiveSetTargetAndSamplingDoNotCallAnyExternalBoundary()
        {
            using (Rig rig = new Rig())
            {
                rig.Select(Target(71, 222, "Game.exe"));
                Missing(rig.Sample(), "FpsDisabled");
                Assert.AreEqual(0, rig.CaptureCount);
                Assert.AreEqual(0, rig.IdentityCalls);
                Assert.AreEqual(0, rig.ForegroundCalls);
            }
        }

        [Test]
        public void ActiveWithoutAnIdentityAsksForASelection()
        {
            using (Rig rig = new Rig())
            {
                rig.Active(true);
                Missing(rig.Sample(), "FpsChooseTarget");
                Assert.AreEqual(0, rig.CaptureCount);
            }
        }

        [Test]
        public void ManualSelectionIsAnIdentityAndDisplayNameCopy()
        {
            using (Rig rig = new Rig())
            {
                ProcessSnapshot source = Target(71, 222, "C:\\private\\Game.exe");
                rig.Select(source);
                source.Id = 72; source.CreationTime = 333; source.Name = "C:\\credentials\\Other.exe";
                rig.Active(true);
                Capture capture = rig.WaitCapture(0);
                Assert.AreEqual(71, capture.Target.Id);
                Assert.AreEqual(222, capture.Target.CreationTime);
                capture.SixtyFrames();
                rig.Now = 60010;
                object reading = rig.Sample();
                Available(reading, 60.0);
                Assert.AreEqual("Game", Property<string>(reading, "TargetName"));
                Assert.AreEqual(71, rig.LastVerified.Id);
                Assert.AreEqual(222, rig.LastVerified.CreationTime);
            }
        }

        [Test]
        public void SelectionChangeImmediatelyDropsOldRateAndRejectsLateCallbacks()
        {
            using (Rig rig = new Rig())
            {
                Capture old = rig.StartManual();
                old.SixtyFrames();
                Available(rig.Sample(), 60.0);
                rig.Select(Target(72, 333, "Second.exe"));
                object pending = rig.Sample();
                Assert.AreNotEqual(MetricStatus.Available, Property<MetricValue>(pending, "Metric").Status);
                Assert.AreEqual("Second", Property<string>(pending, "TargetName"));
                old.SixtyFrames();
                Capture current = rig.WaitCapture(1);
                Missing(rig.Sample(), "FpsWarmingUp");
                current.Present(60010);
                Missing(rig.Sample(), "FpsWarmingUp");
                rig.Now = 61020;
                current.Present(61010);
                Available(rig.Sample(), 60.0);
            }
        }

        [Test]
        public void DisabledImmediatelyInvalidatesAndStopsCaptureOffCallerThread()
        {
            using (Rig rig = new Rig())
            {
                Capture capture = rig.StartManual();
                capture.SixtyFrames();
                Available(rig.Sample(), 60.0);
                capture.BlockStop = true;
                rig.Active(false);
                Assert.IsTrue(capture.StopEntered.WaitOne(3000));
                Missing(rig.Sample(), "FpsDisabled");
                capture.SixtyFrames();
                Missing(rig.Sample(), "FpsDisabled");
                Assert.IsFalse(capture.StopFinished.WaitOne(0), "Native cleanup is still blocked while UI demand already returned.");
                rig.ReleaseStops.Set();
                Assert.IsTrue(capture.StopFinished.WaitOne(3000));
            }
        }

        [Test]
        public void TwoRapidSelectionsCoalesceAndNeverOverlapConsumers()
        {
            using (Rig rig = new Rig())
            {
                Capture first = rig.StartManual();
                first.BlockStop = true;
                rig.Select(Target(72, 333, "Second.exe"));
                Assert.IsTrue(first.StopEntered.WaitOne(3000));
                rig.Select(Target(73, 444, "Third.exe"));
                Assert.AreEqual(1, rig.CaptureCount, "No new native start is allowed before old Stop completes.");
                Missing(rig.Sample(), "FpsWarmingUp");
                Assert.AreEqual("Third", Property<string>(rig.Sample(), "TargetName"));
                rig.ReleaseStops.Set();
                Capture latest = rig.WaitCapture(1);
                Assert.AreEqual(73, latest.Target.Id);
                Assert.AreEqual(444, latest.Target.CreationTime);
                Assert.AreEqual(1, rig.MaximumAlive);
                Assert.AreEqual(2, rig.CaptureCount, "Intermediate selection must not be replayed from a pending queue.");
            }
        }

        [Test]
        public void DisposeReturnsBeforeBlockedNativeCleanupAndCannotRestart()
        {
            using (Rig rig = new Rig())
            using (ManualResetEvent returned = new ManualResetEvent(false))
            {
                Capture capture = rig.StartManual();
                capture.BlockStop = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    ((IDisposable)rig.Monitor).Dispose();
                    returned.Set();
                });
                Assert.IsTrue(returned.WaitOne(3000), "UI Dispose must not join a native consumer.");
                Assert.IsTrue(capture.StopEntered.WaitOne(3000));
                rig.Active(true);
                rig.Select(Target(72, 333, "Second.exe"));
                Missing(rig.Sample(), "FpsDisabled");
                Assert.AreEqual(1, rig.CaptureCount);
                rig.ReleaseStops.Set();
                Assert.IsTrue(capture.StopFinished.WaitOne(3000));
            }
        }

        [Test]
        public void DisablingDuringCaptureStartupDisposesLateFactoryResult()
        {
            using (Rig rig = new Rig())
            {
                rig.BlockStart = true;
                rig.Select(Target(71, 222, "Game.exe"));
                rig.Active(true);
                Assert.IsTrue(rig.StartEntered.WaitOne(3000));
                rig.Active(false);
                Missing(rig.Sample(), "FpsDisabled");
                rig.ReleaseStart.Set();
                Capture late = rig.WaitCapture(0);
                Assert.IsTrue(late.StopFinished.WaitOne(3000));
                late.SixtyFrames();
                Missing(rig.Sample(), "FpsDisabled");
                Assert.AreEqual(1, rig.MaximumAlive);
            }
        }

        [TestCase("FpsIdentityChanged")]
        [TestCase("FpsTargetExited")]
        [TestCase("FpsAccessDenied")]
        public void InvalidCreationIdentityRefusesAnyCapture(string state)
        {
            using (Rig rig = new Rig())
            {
                rig.Verification = state;
                rig.Select(Target(71, 222, "Game.exe"));
                rig.Active(true);
                Missing(rig.Sample(), state);
                Assert.AreEqual(0, rig.CaptureCount);
            }
        }

        [TestCase("FpsIdentityChanged")]
        [TestCase("FpsTargetExited")]
        public void IdentityFailureDuringCaptureDropsHistoryAndStopsOwnedCapture(string state)
        {
            using (Rig rig = new Rig())
            {
                Capture capture = rig.StartManual();
                capture.SixtyFrames();
                Available(rig.Sample(), 60.0);
                rig.Verification = state;
                Missing(rig.Sample(), state);
                Assert.IsTrue(capture.StopFinished.WaitOne(3000));
                capture.SixtyFrames();
                Missing(rig.Sample(), state);
            }
        }

        [Test]
        public void ForegroundFollowingChangesSafelyAndExcludesThisTool()
        {
            using (Rig rig = new Rig())
            {
                rig.Foreground = Target(71, 222, "Foreground.exe");
                rig.Follow();
                rig.Active(true);
                Missing(rig.Sample(), "FpsWarmingUp");
                Capture old = rig.WaitCapture(0);
                old.SixtyFrames();
                rig.Now = 60010;
                Available(rig.Sample(), 60.0);
                rig.Foreground = Target(72, 333, "Another.exe");
                Missing(rig.Sample(), "FpsWarmingUp");
                Capture current = rig.WaitCapture(1);
                Assert.AreEqual(72, current.Target.Id);
                Assert.AreEqual(1, rig.MaximumAlive);
                old.SixtyFrames();
                Missing(rig.Sample(), "FpsWarmingUp");
                rig.Foreground = Target(900, 555, "WinMemoryCleaner.Community.exe");
                Missing(rig.Sample(), "FpsChooseTarget");
                Assert.IsTrue(current.StopFinished.WaitOne(3000));
                Assert.AreEqual(2, rig.CaptureCount);
            }
        }

        [Test]
        public void NativeLossIsStickyUntilFreshDemandAndNeverKeepsUndercountedFps()
        {
            using (Rig rig = new Rig())
            {
                Capture capture = rig.StartManual();
                capture.SixtyFrames();
                Available(rig.Sample(), 60.0);
                capture.Fault("FpsEventLoss", 0);
                Missing(rig.Sample(), "FpsEventLoss");
                capture.SixtyFrames();
                Missing(rig.Sample(), "FpsEventLoss");
                Assert.IsTrue(capture.StopFinished.WaitOne(3000));
                rig.Active(false);
                rig.Active(true);
                rig.WaitCapture(1);
                Missing(rig.Sample(), "FpsWarmingUp");
            }
        }

        [Test]
        public void NativeErrorsExposeOnlyFixedPublicSourceAndNumericCode()
        {
            using (Rig rig = new Rig())
            {
                Capture capture = rig.StartManual();
                capture.Fault("C:\\secret\\token", 5);
                object reading = rig.Sample();
                Missing(reading, "FpsError");
                string detail = Property<MetricValue>(reading, "Metric").Detail;
                Assert.IsFalse(detail.Contains("secret"));
                Assert.IsFalse(detail.Contains("token"));
                Assert.IsTrue(detail.Contains("5"));
            }
        }

        [Test]
        public void NativeFactoryExceptionsDoNotLeakLocalPathsOrCrashSampling()
        {
            using (Rig rig = new Rig())
            {
                rig.ThrowOnStart = true;
                rig.Select(Target(71, 222, "Game.exe"));
                rig.Active(true);
                Assert.IsTrue(rig.FactoryReturned.WaitOne(3000));
                object reading = rig.Sample();
                Missing(reading, "FpsError");
                Assert.IsFalse(Property<MetricValue>(reading, "Metric").Detail.Contains("private"));
                Assert.AreEqual(0, rig.CaptureCount);
            }
        }

        [Test]
        public void ForeignPidCallbackCannotProduceSelectedTargetRate()
        {
            using (Rig rig = new Rig())
            {
                Capture capture = rig.StartManual();
                for (int i = 0; i < 61; i++) capture.Emit(72, i * 1000L);
                Missing(rig.Sample(), "FpsWarmingUp");
                capture.SixtyFrames();
                Available(rig.Sample(), 60.0);
            }
        }

        [Test]
        public void NativeInteropLayoutsMatchSdkPointerAlignmentWithoutStartingEtw()
        {
            Type native = CoreType("FramePresentNativeMethods");
            Type header = native.GetNestedType("EventHeader", BindingFlags.NonPublic);
            Type record = native.GetNestedType("EventRecord", BindingFlags.NonPublic);
            Type logfile = native.GetNestedType("EventTraceLogfile", BindingFlags.NonPublic);
            Type parameters = native.GetNestedType("EnableTraceParameters", BindingFlags.NonPublic);
            Type descriptor = native.GetNestedType("EventFilterDescriptor", BindingFlags.NonPublic);
            Assert.IsNotNull(header); Assert.IsNotNull(record); Assert.IsNotNull(logfile);
            Assert.IsNotNull(parameters); Assert.IsNotNull(descriptor);
            Assert.AreEqual(80, Marshal.SizeOf(header));
            Assert.AreEqual(16, Marshal.SizeOf(descriptor));
            Assert.AreEqual(IntPtr.Size == 8 ? 112 : 104, Marshal.SizeOf(record));
            Assert.AreEqual(88 + IntPtr.Size, Marshal.OffsetOf(record, "UserData").ToInt32());
            Assert.AreEqual(IntPtr.Size == 8 ? 448 : 416, Marshal.SizeOf(logfile));
            Assert.AreEqual(IntPtr.Size == 8 ? 48 : 36, Marshal.SizeOf(parameters));
            Assert.AreEqual(IntPtr.Size == 8 ? 424 : 400, Marshal.OffsetOf(logfile, "EventRecordCallback").ToInt32());
        }

        [TestCase(333L, 0L, "FpsIdentityChanged")]
        [TestCase(222L, 1L, "FpsTargetExited")]
        [TestCase(222L, 0L, null)]
        public void RealIdentityVerifierComparesCreationFileTimeAndAlwaysClosesReadHandle(long actualCreation, long exitTime, string expectedState)
        {
            int closes = 0;
            object process = Construct("FramePresentProcess",
                new Func<int, IntPtr>(delegate(int id) { Assert.AreEqual(71, id); return new IntPtr(17); }),
                new Func<IntPtr, long[]>(delegate { return new[] { actualCreation, exitTime }; }),
                new Func<IntPtr, string>(delegate { return "C:\\private\\Game.exe"; }),
                new Action<IntPtr>(delegate(IntPtr handle) { Assert.AreEqual(new IntPtr(17), handle); closes++; }),
                new Func<int>(delegate { return 71; }), 900);
            Assert.AreEqual(expectedState, Call(process, "Verify", Target(71, 222, "Game")));
            Assert.AreEqual(1, closes, "Identity verification owns only a read/query handle and releases it on every outcome.");
        }

        [Test]
        public void RealForegroundResolverExcludesSelfWithoutOpeningOrEnumeratingProcesses()
        {
            int opens = 0;
            object process = Construct("FramePresentProcess",
                new Func<int, IntPtr>(delegate { opens++; return new IntPtr(17); }),
                new Func<IntPtr, long[]>(delegate { return new[] { 222L, 0L }; }),
                new Func<IntPtr, string>(delegate { return "C:\\private\\Game.exe"; }),
                new Action<IntPtr>(delegate { }), new Func<int>(delegate { return 900; }), 900);
            Assert.IsNull(Call(process, "ResolveForeground"));
            Assert.AreEqual(0, opens);
        }

        [Test]
        public void RealForegroundResolverReturnsCopiedCreationIdentityAndOnlyBasename()
        {
            int closes = 0;
            object process = Construct("FramePresentProcess",
                new Func<int, IntPtr>(delegate(int id) { Assert.AreEqual(71, id); return new IntPtr(17); }),
                new Func<IntPtr, long[]>(delegate { return new[] { 222L, 0L }; }),
                new Func<IntPtr, string>(delegate { return "C:\\private\\Game.exe"; }),
                new Action<IntPtr>(delegate { closes++; }), new Func<int>(delegate { return 71; }), 900);
            ProcessSnapshot identity = (ProcessSnapshot)Call(process, "ResolveForeground");
            Assert.AreEqual(71, identity.Id);
            Assert.AreEqual(222, identity.CreationTime);
            Assert.AreEqual("Game", identity.Name);
            Assert.AreEqual(1, closes);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NativeFilterContainsBothExactPidAndIncludeOnlyPresentIds(bool dxgi)
        {
            ushort[] ids = dxgi ? new ushort[] { 42, 43, 55, 56 } : new ushort[] { 1, 2 };
            using (IDisposable filter = (IDisposable)Construct("FramePresentFilter", 71, ids))
            {
                object parameters = Property<object>(filter, "Parameters");
                Assert.AreEqual(2u, Field(parameters, "Version"));
                Assert.AreEqual(2u, Field(parameters, "FilterDescCount"));
                IntPtr descriptors = (IntPtr)Field(parameters, "EnableFilterDesc");
                long pidPointer = Marshal.ReadInt64(descriptors, 0);
                long idPointer = Marshal.ReadInt64(descriptors, 16);
                IntPtr pidData = IntPtr.Size == 8 ? new IntPtr(pidPointer) : new IntPtr(unchecked((int)pidPointer));
                IntPtr idData = IntPtr.Size == 8 ? new IntPtr(idPointer) : new IntPtr(unchecked((int)idPointer));
                Assert.AreEqual(4, Marshal.ReadInt32(descriptors, 8));
                Assert.AreEqual(unchecked((int)0x80000004u), Marshal.ReadInt32(descriptors, 12));
                Assert.AreEqual(71, Marshal.ReadInt32(pidData));
                Assert.AreEqual(4 + ids.Length * 2, Marshal.ReadInt32(descriptors, 24));
                Assert.AreEqual(unchecked((int)0x80000200u), Marshal.ReadInt32(descriptors, 28));
                Assert.AreEqual(1, Marshal.ReadByte(idData, 0));
                Assert.AreEqual(0, Marshal.ReadByte(idData, 1));
                Assert.AreEqual(ids.Length, Marshal.ReadInt16(idData, 2));
                for (int i = 0; i < ids.Length; i++) Assert.AreEqual(ids[i], (ushort)Marshal.ReadInt16(idData, 4 + 2 * i));
            }
        }

        private static byte[] Metadata(uint propertyFlags)
        {
            byte[] blob = new byte[320];
            Array.Copy(Dxgi.ToByteArray(), blob, 16);
            Array.Copy(BitConverter.GetBytes((ushort)42), 0, blob, 32, 2);
            Array.Copy(BitConverter.GetBytes(3), 0, blob, 100, 4);
            Array.Copy(BitConverter.GetBytes(3), 0, blob, 104, 4);
            Array.Copy(BitConverter.GetBytes(1), 0, blob, 108, 4);
            int position = 184;
            string[] names = { "pIDXGISwapChain", "Flags", "SyncInterval" };
            int[] types = { 16, 8, 7 };
            for (int i = 0; i < 3; i++)
            {
                int entry = 112 + i * 24;
                Array.Copy(BitConverter.GetBytes(propertyFlags), 0, blob, entry, 4);
                Array.Copy(BitConverter.GetBytes(position), 0, blob, entry + 4, 4);
                Array.Copy(BitConverter.GetBytes((ushort)types[i]), 0, blob, entry + 8, 2);
                Array.Copy(BitConverter.GetBytes((ushort)1), 0, blob, entry + 16, 2);
                byte[] name = System.Text.Encoding.Unicode.GetBytes(names[i] + "\0");
                Array.Copy(name, 0, blob, position, name.Length);
                position += name.Length;
            }
            return blob;
        }

        private static object ReadSchema(byte[] blob)
        {
            MethodInfo method = CoreType("FramePresentSchema").GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return method.Invoke(null, new object[] { blob, Dxgi, 42, 0, 8 });
        }

        [Test]
        public void NativeMetadataDecoderValidatesTypedScalarFieldsFromBoundedTdhBlob()
        {
            object schema = ReadSchema(Metadata(0));
            Assert.IsNotNull(schema);
            CollectionAssert.AreEqual(new[] { "pIDXGISwapChain", "Flags", "SyncInterval" }, Property<string[]>(schema, "Names"));
            CollectionAssert.AreEqual(new[] { 16, 8, 7 }, Property<int[]>(schema, "Types"));
        }

        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(4u)]
        [TestCase(128u)]
        public void NativeMetadataDecoderRejectsStructVariableArrayAndCustomSchemas(uint propertyFlags)
        {
            Assert.IsNull(ReadSchema(Metadata(propertyFlags)));
        }

        [Test]
        public void NativeMetadataDecoderRejectsOutOfBoundsOffsetsCountsAndProviderMismatch()
        {
            byte[] offset = Metadata(0);
            Array.Copy(BitConverter.GetBytes(400), 0, offset, 116, 4);
            Assert.IsNull(ReadSchema(offset));
            byte[] count = Metadata(0);
            Array.Copy(BitConverter.GetBytes(1000000), 0, count, 100, 4);
            Assert.IsNull(ReadSchema(count));
            byte[] provider = Metadata(0);
            provider[0] ^= 255;
            Assert.IsNull(ReadSchema(provider));
            Assert.IsNull(ReadSchema(new byte[111]));
        }

        [Test]
        public void UnsupportedNativePlatformCannotStartAnUnfilteredFallback()
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.Supported = false;
                rig.Start();
                Missing(rig.Read(0), "FpsUnsupported");
                Assert.AreEqual(0, rig.StartCalls);
                Assert.AreEqual(0, rig.EnableCalls);
                Assert.AreEqual(0, rig.StopCalls);
            }
        }

        [Test]
        public void SessionNameCollisionNeverStopsOrOpensTheExistingSession()
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.StartCode = 183;
                rig.Start();
                Missing(rig.Read(0), "FpsError");
                Assert.AreEqual(183, rig.FaultCode);
                Assert.AreEqual(0, rig.EnableCalls);
                Assert.AreEqual(0, rig.OpenCalls);
                Assert.AreEqual(0, rig.StopCalls);
                Assert.AreEqual(0, rig.CloseCalls);
            }
        }

        [TestCase(5u, "FpsAccessDenied")]
        [TestCase(87u, "FpsUnsupported")]
        public void FilterFailureStopsOnlyItsOwnSessionWithoutAnyBroadRetry(uint code, string state)
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.EnableCode = code;
                rig.Start();
                Missing(rig.Read(0), state);
                Assert.AreEqual(1, rig.EnableCalls);
                Assert.AreEqual(1, rig.StopCalls);
                Assert.AreEqual(0, rig.OpenCalls);
                Assert.AreEqual(0, rig.CloseCalls);
                Assert.AreEqual((int)code, rig.FaultCode);
            }
        }

        [Test]
        public void NativeCaptureRequestsUniqueQpcRealtimeSessionAndBothNarrowProviderFilters()
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.Start();
                rig.WaitProcess();
                Assert.IsTrue(rig.SessionName.StartsWith("WinMemoryCleaner.FramePresent.", StringComparison.Ordinal));
                Guid suffix;
                Assert.IsTrue(Guid.TryParse(rig.SessionName.Substring("WinMemoryCleaner.FramePresent.".Length), out suffix));
                Assert.AreNotEqual(Guid.Empty, suffix);
                Assert.AreEqual(1, Marshal.ReadInt32(rig.Properties, 40));
                Assert.AreEqual(0x20000, Marshal.ReadInt32(rig.Properties, 44));
                Assert.AreEqual(64, Marshal.ReadInt32(rig.Properties, 48));
                Assert.AreEqual(2, Marshal.ReadInt32(rig.Properties, 52));
                Assert.AreEqual(8, Marshal.ReadInt32(rig.Properties, 56));
                Assert.AreEqual(0x10000100, Marshal.ReadInt32(rig.Properties, 64));
                Assert.AreEqual(1, Marshal.ReadInt32(rig.Properties, 68));
                Assert.AreEqual(0x10001100, Marshal.ReadInt32(rig.Logfile, IntPtr.Size == 8 ? 28 : 20));
                Assert.AreEqual(2, rig.EnableCalls);
                Assert.AreEqual(1, rig.OpenCalls);
            }
        }

        [TestCase(4)]
        [TestCase(8)]
        public void RealNativeCallbackUsesProviderPointerHeaderAndTdhSchemaNotCollectorBitness(int pointerSize)
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.Start();
                rig.WaitProcess();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                for (int i = 0; i < 61; i++) rig.Present(i * 1000L, pointerSize);
                Available(rig.Read(60010), 60.0);
                Assert.AreEqual(2, rig.MetadataCalls, "Validated Start/Stop schemas are cached, not queried per frame.");
            }
        }

        [TestCase(1, 8, false)]
        [TestCase(0, 0, false)]
        [TestCase(0, 8, true)]
        public void NativeCallbackRejectsUnknownVersionAmbiguousHeaderOrInvalidTdhMetadata(int version, int pointerSize, bool badMetadata)
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.BadMetadata = badMetadata;
                rig.Start();
                rig.WaitProcess();
                rig.Emit(42, version, 71, 0, pointerSize, StartPayload(Dxgi, 8, 1, 0));
                Missing(rig.Read(0), "FpsUnsupported");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TraceBufferOrSessionLossInvalidatesPreviouslyAvailableNativeRate(bool sessionLoss)
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.Start();
                rig.WaitProcess();
                rig.Present(0, 8);
                rig.Present(1000, 8);
                Available(rig.Read(1010), 60.0);
                rig.QueryLoss = sessionLoss;
                if (!sessionLoss) Marshal.WriteInt32(rig.Logfile, IntPtr.Size == 8 ? 416 : 396, 1);
                Assert.AreEqual(0u, rig.Buffer(), "Loss must stop callback processing rather than continue silently undercounting.");
                Missing(rig.Read(1010), "FpsEventLoss");
                rig.Present(2000, 8);
                Missing(rig.Read(2010), "FpsEventLoss");
            }
        }

        [Test]
        public void NativeStopRetainsCallbackRootsAndMemoryUntilProcessTraceHasActuallyExited()
        {
            using (NativeRig rig = new NativeRig())
            using (ManualResetEvent returned = new ManualResetEvent(false))
            {
                rig.Start();
                rig.WaitProcess();
                IntPtr properties = (IntPtr)Field(rig.Capture, "_properties");
                IntPtr logfile = (IntPtr)Field(rig.Capture, "_logfile");
                ThreadPool.QueueUserWorkItem(delegate { ((IDisposable)rig.Capture).Dispose(); returned.Set(); });
                Assert.IsTrue(rig.StopEntered.WaitOne(3000));
                Assert.IsFalse(returned.WaitOne(0));
                Assert.AreEqual(properties, Field(rig.Capture, "_properties"));
                Assert.AreEqual(logfile, Field(rig.Capture, "_logfile"));
                Assert.IsNotNull(Field(rig.Capture, "_eventCallback"));
                Assert.IsNotNull(Field(rig.Capture, "_bufferCallback"));
                rig.ReleaseProcess.Set();
                Assert.IsTrue(returned.WaitOne(3000));
                Assert.AreEqual(IntPtr.Zero, Field(rig.Capture, "_properties"));
                Assert.AreEqual(IntPtr.Zero, Field(rig.Capture, "_logfile"));
                Assert.AreEqual(1, rig.StopCalls);
                Assert.AreEqual(1, rig.CloseCalls);
            }
        }

        [Test]
        public void ManagedSinkAndFaultObserverExceptionsCannotEscapeNativeCallbacks()
        {
            using (NativeRig rig = new NativeRig())
            {
                rig.ThrowSink = true;
                rig.ThrowFault = true;
                rig.Start();
                rig.WaitProcess();
                Assert.DoesNotThrow(delegate { rig.Present(0, 8); });
                Missing(rig.Read(0), "FpsError");
                Assert.AreEqual(0u, rig.Buffer());
            }
        }

        [Test]
        public void AFailedOwnedCleanupCannotMasqueradeAsWarmingUpOrPermitAnotherCapture()
        {
            using (Rig rig = new Rig())
            {
                Capture first = rig.StartManual();
                first.ThrowStop = true;
                rig.Select(Target(72, 333, "Second"));
                Assert.IsTrue(first.StopFinished.WaitOne(3000));
                for (int i = 0; i < 3000 && !(bool)Field(rig.Monitor, "_cleanupFailed"); i++) Thread.Sleep(1);
                Assert.IsTrue((bool)Field(rig.Monitor, "_cleanupFailed"));
                Missing(rig.Sample(), "FpsError");
                rig.Active(false);
                Missing(rig.Sample(), "FpsDisabled");
                rig.Active(true);
                rig.Select(Target(73, 444, "Third"));
                Missing(rig.Sample(), "FpsError");
                Assert.AreEqual(1, rig.CaptureCount);
            }
        }

        [Test]
        public void AnExitedPidOpenErrorIsTargetExitedNotUnsupportedOrAnEscapingException()
        {
            object process = Construct("FramePresentProcess",
                new Func<int, IntPtr>(delegate { throw new System.ComponentModel.Win32Exception(87); }),
                new Func<IntPtr, long[]>(delegate { return new[] { 222L, 0L }; }),
                new Func<IntPtr, string>(delegate { return "Game"; }), new Action<IntPtr>(delegate { }),
                new Func<int>(delegate { return 71; }), 900);
            object state = null;
            Assert.DoesNotThrow(delegate { state = Call(process, "Verify", Target(71, 222, "Game")); });
            Assert.AreEqual("FpsTargetExited", state);
        }

        private sealed class NativeRig : IDisposable
        {
            internal readonly ManualResetEvent ProcessEntered = new ManualResetEvent(false);
            internal readonly ManualResetEvent ReleaseProcess = new ManualResetEvent(false);
            internal readonly ManualResetEvent StopEntered = new ManualResetEvent(false);
            private readonly object _accumulator = Accumulator();
            private int _stopCalls;
            private int _closeCalls;
            private bool _disposed;
            internal object Capture { get; private set; }
            internal bool Supported { get; set; }
            internal uint StartCode { get; set; }
            internal uint EnableCode { get; set; }
            internal bool BadMetadata { get; set; }
            internal bool QueryLoss { get; set; }
            internal bool ThrowSink { get; set; }
            internal bool ThrowFault { get; set; }
            internal int FaultCode { get; private set; }
            internal int StartCalls { get; private set; }
            internal int EnableCalls { get; private set; }
            internal int OpenCalls { get; private set; }
            internal int MetadataCalls { get; private set; }
            internal int StopCalls { get { return _stopCalls; } }
            internal int CloseCalls { get { return _closeCalls; } }
            internal string SessionName { get; private set; }
            internal IntPtr Properties { get; private set; }
            internal IntPtr Logfile { get; private set; }

            internal NativeRig() { Supported = true; }

            internal void Start()
            {
                Capture = Construct("FramePresentCapture", Target(71, 222, "Game"),
                    new Action<Guid, int, int, int, int, long, int, byte[], string[], int[]>(Sink),
                    new Action<string, int>(Fault), new Func<bool>(delegate { return Supported; }),
                    new Func<string, IntPtr, Tuple<uint, ulong>>(StartTrace),
                    new Func<ulong, Guid, IntPtr, uint>(Enable), new Func<IntPtr, Tuple<uint, ulong>>(Open),
                    new Func<ulong, uint>(Process), new Func<ulong, uint>(Close),
                    new Func<ulong, IntPtr, uint, uint>(Control), new Func<IntPtr, byte[]>(Schema));
            }

            private Tuple<uint, ulong> StartTrace(string name, IntPtr properties)
            {
                StartCalls++;
                SessionName = name; Properties = properties;
                return Tuple.Create(StartCode, StartCode == 0 ? 123UL : 0UL);
            }

            private uint Enable(ulong session, Guid provider, IntPtr parameters)
            {
                EnableCalls++;
                Assert.AreEqual(123UL, session);
                Assert.AreEqual(2, Marshal.ReadInt32(parameters));
                Assert.AreEqual(2, Marshal.ReadInt32(parameters, IntPtr.Size == 8 ? 40 : 32));
                IntPtr descriptors = Marshal.ReadIntPtr(parameters, IntPtr.Size == 8 ? 32 : 28);
                Assert.AreEqual(4, Marshal.ReadInt32(descriptors, 8));
                Assert.AreEqual(unchecked((int)0x80000004u), Marshal.ReadInt32(descriptors, 12));
                IntPtr pid = Pointer(Marshal.ReadInt64(descriptors));
                Assert.AreEqual(71, Marshal.ReadInt32(pid));
                Assert.AreEqual(unchecked((int)0x80000200u), Marshal.ReadInt32(descriptors, 28));
                IntPtr ids = Pointer(Marshal.ReadInt64(descriptors, 16));
                Assert.AreEqual(1, Marshal.ReadByte(ids));
                ushort[] expected = provider == Dxgi ? new ushort[] { 42, 43, 55, 56 } : new ushort[] { 1, 2 };
                Assert.IsTrue(provider == Dxgi || provider == D3d9);
                Assert.AreEqual(expected.Length, Marshal.ReadInt16(ids, 2));
                for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], (ushort)Marshal.ReadInt16(ids, 4 + 2 * i));
                return EnableCode;
            }

            private Tuple<uint, ulong> Open(IntPtr logfile) { OpenCalls++; Logfile = logfile; return Tuple.Create(0u, 456UL); }
            private uint Process(ulong trace)
            {
                Assert.AreEqual(456UL, trace);
                ProcessEntered.Set();
                ReleaseProcess.WaitOne(10000);
                return 0;
            }
            private uint Close(ulong trace) { Assert.AreEqual(456UL, trace); Interlocked.Increment(ref _closeCalls); return 7007; }
            private uint Control(ulong session, IntPtr properties, uint action)
            {
                Assert.AreEqual(123UL, session, "Only the session handle actually returned by successful StartTrace is owned.");
                if (action == 1) { Interlocked.Increment(ref _stopCalls); StopEntered.Set(); }
                else { Assert.AreEqual(0u, action); if (QueryLoss) Marshal.WriteInt32(properties, 100, 1); }
                return 0;
            }
            private byte[] Schema(IntPtr record)
            {
                MetadataCalls++;
                if (BadMetadata) return new byte[65537];
                int id = Marshal.ReadInt16(record, 40);
                if (id == 42) return Metadata(0);
                byte[] blob = new byte[160];
                Array.Copy(Dxgi.ToByteArray(), blob, 16);
                Array.Copy(BitConverter.GetBytes((ushort)43), 0, blob, 32, 2);
                Array.Copy(BitConverter.GetBytes(1), 0, blob, 100, 4);
                Array.Copy(BitConverter.GetBytes(1), 0, blob, 104, 4);
                Array.Copy(BitConverter.GetBytes(136), 0, blob, 116, 4);
                Array.Copy(BitConverter.GetBytes((ushort)7), 0, blob, 120, 2);
                Array.Copy(BitConverter.GetBytes((ushort)1), 0, blob, 128, 2);
                byte[] name = System.Text.Encoding.Unicode.GetBytes("Result\0");
                Array.Copy(name, 0, blob, 136, name.Length);
                return blob;
            }
            private void Sink(Guid provider, int id, int version, int pid, int tid, long timestamp, int pointerSize, byte[] payload, string[] names, int[] types)
            {
                if (ThrowSink) throw new InvalidOperationException("C:\\private\\credentials");
                Call(_accumulator, "Accept", provider, id, version, pid, tid, timestamp, pointerSize, payload, names, types);
            }
            private void Fault(string state, int code)
            {
                FaultCode = code;
                Call(_accumulator, "Fail", state);
                if (ThrowFault) throw new InvalidOperationException("C:\\private\\credentials");
            }
            internal object Read(long now) { return Reading(_accumulator, now); }
            internal void WaitProcess() { Assert.IsTrue(ProcessEntered.WaitOne(3000)); }
            private static IntPtr Pointer(long value) { return IntPtr.Size == 8 ? new IntPtr(value) : new IntPtr(unchecked((int)value)); }
            private Delegate Callback(string name, int offset)
            {
                Type callback = CoreType("FramePresentNativeMethods").GetNestedType(name, BindingFlags.NonPublic);
                Assert.IsNotNull(callback);
                return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Logfile, offset), callback);
            }
            internal void Present(long timestamp, int pointerSize)
            {
                Emit(42, 0, 71, timestamp, pointerSize, StartPayload(Dxgi, pointerSize, 1, 0));
                Emit(43, 0, 71, timestamp + 10, pointerSize, BitConverter.GetBytes(0));
            }
            internal void Emit(int id, int version, int processId, long timestamp, int pointerSize, byte[] payload)
            {
                byte[] record = new byte[IntPtr.Size == 8 ? 112 : 104];
                record[0] = 80;
                record[4] = (byte)(pointerSize == 4 ? 0x20 : pointerSize == 8 ? 0x40 : 0);
                Array.Copy(BitConverter.GetBytes(17), 0, record, 8, 4);
                Array.Copy(BitConverter.GetBytes(processId), 0, record, 12, 4);
                Array.Copy(BitConverter.GetBytes(timestamp), 0, record, 16, 8);
                Array.Copy(Dxgi.ToByteArray(), 0, record, 24, 16);
                Array.Copy(BitConverter.GetBytes((ushort)id), 0, record, 40, 2);
                record[42] = (byte)version;
                Array.Copy(BitConverter.GetBytes((ushort)payload.Length), 0, record, 86, 2);
                IntPtr data = Marshal.AllocHGlobal(payload.Length);
                IntPtr nativeRecord = Marshal.AllocHGlobal(record.Length);
                try
                {
                    Marshal.Copy(payload, 0, data, payload.Length);
                    Marshal.Copy(record, 0, nativeRecord, record.Length);
                    Marshal.WriteIntPtr(nativeRecord, 88 + IntPtr.Size, data);
                    Callback("EventRecordCallbackDelegate", IntPtr.Size == 8 ? 424 : 400).DynamicInvoke(nativeRecord);
                }
                finally { Marshal.FreeHGlobal(nativeRecord); Marshal.FreeHGlobal(data); }
            }
            internal uint Buffer() { return (uint)Callback("BufferCallbackDelegate", IntPtr.Size == 8 ? 400 : 384).DynamicInvoke(Logfile); }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                ReleaseProcess.Set();
                if (Capture != null) ((IDisposable)Capture).Dispose();
                ProcessEntered.Dispose(); ReleaseProcess.Dispose(); StopEntered.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private sealed class Rig : IDisposable
        {
            private readonly object _gate = new object();
            private readonly List<Capture> _captures = new List<Capture>();
            private readonly AutoResetEvent _opened = new AutoResetEvent(false);
            private int _alive;
            private int _maximumAlive;
            private int _identityCalls;
            private int _foregroundCalls;
            private long _now;
            private string _verification;
            private ProcessSnapshot _foreground;
            private ProcessSnapshot _lastVerified;
            private bool _disposed;
            internal readonly ManualResetEvent StartEntered = new ManualResetEvent(false);
            internal readonly ManualResetEvent ReleaseStart = new ManualResetEvent(false);
            internal readonly ManualResetEvent ReleaseStops = new ManualResetEvent(false);
            internal readonly ManualResetEvent FactoryReturned = new ManualResetEvent(false);
            internal object Monitor { get; private set; }
            internal bool BlockStart { get; set; }
            internal bool ThrowOnStart { get; set; }
            internal long Now { get { return Interlocked.Read(ref _now); } set { Interlocked.Exchange(ref _now, value); } }
            internal string Verification { get { lock (_gate) return _verification; } set { lock (_gate) _verification = value; } }
            internal ProcessSnapshot Foreground { get { lock (_gate) return _foreground; } set { lock (_gate) _foreground = value; } }
            internal ProcessSnapshot LastVerified { get { lock (_gate) return _lastVerified; } }
            internal int CaptureCount { get { lock (_gate) return _captures.Count; } }
            internal int MaximumAlive { get { lock (_gate) return _maximumAlive; } }
            internal int IdentityCalls { get { return _identityCalls; } }
            internal int ForegroundCalls { get { return _foregroundCalls; } }

            internal Rig()
            {
                Func<ProcessSnapshot, Action<Guid, int, int, int, int, long, int, byte[], string[], int[]>, Action<string, int>, IDisposable> factory = Open;
                Monitor = Construct("FrameRateMonitor", new Func<long>(Clock), 60000L,
                    new Func<ProcessSnapshot>(ResolveForeground), new Func<ProcessSnapshot, string>(Verify), factory, 900);
            }

            private long Clock() { return Now; }
            private ProcessSnapshot ResolveForeground() { Interlocked.Increment(ref _foregroundCalls); return Foreground; }
            private string Verify(ProcessSnapshot target)
            {
                Interlocked.Increment(ref _identityCalls);
                lock (_gate) { _lastVerified = target; return _verification; }
            }

            private IDisposable Open(ProcessSnapshot target, Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> sink, Action<string, int> fault)
            {
                StartEntered.Set();
                if (BlockStart && !ReleaseStart.WaitOne(3000)) throw new InvalidOperationException("Native start test gate timed out");
                if (ThrowOnStart)
                {
                    // Signal after the monitor has handled the exception, not before the catch.
                    ThreadPool.QueueUserWorkItem(delegate { WaitForError(); FactoryReturned.Set(); });
                    throw new InvalidOperationException("C:\\private\\credentials");
                }
                Capture capture = new Capture(this, target, sink, fault);
                lock (_gate)
                {
                    _alive++;
                    _maximumAlive = Math.Max(_maximumAlive, _alive);
                    _captures.Add(capture);
                }
                _opened.Set();
                return capture;
            }

            private void WaitForError()
            {
                // Monitor.Pulse at native-close is unnecessary: this bounded assertion
                // polls only managed state and never creates native traffic.
                for (int i = 0; i < 3000; i++)
                {
                    if (Property<string>(Sample(), "StateKey") == "FpsError") return;
                    Thread.Yield();
                }
            }

            internal void Closed() { lock (_gate) _alive--; }
            internal void Select(ProcessSnapshot target) { Call(Monitor, "SetTarget", false, target); }
            internal void Follow() { Call(Monitor, "SetTarget", true, null); }
            internal void Active(bool active) { Call(Monitor, "SetActive", active); }
            internal object Sample() { return Call(Monitor, "Sample"); }
            internal Capture StartManual()
            {
                Now = 0;
                Select(Target(71, 222, "Game.exe"));
                Active(true);
                Capture capture = WaitCapture(0);
                Now = 60010;
                return capture;
            }
            internal Capture WaitCapture(int index)
            {
                for (int i = 0; i < 4; i++)
                {
                    lock (_gate) if (_captures.Count > index) return _captures[index];
                    Assert.IsTrue(_opened.WaitOne(3000), "Serial control worker did not open the requested capture.");
                }
                Assert.Fail("Capture count did not advance");
                return null;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                ReleaseStart.Set();
                ReleaseStops.Set();
                if (Monitor != null) ((IDisposable)Monitor).Dispose();
                Capture[] captures;
                lock (_gate) captures = _captures.ToArray();
                foreach (Capture capture in captures)
                {
                    Assert.IsTrue(capture.StopFinished.WaitOne(3000), "Owned deterministic capture cleanup did not finish.");
                    capture.CloseEvents();
                }
                _opened.Dispose();
                StartEntered.Dispose(); ReleaseStart.Dispose(); ReleaseStops.Dispose(); FactoryReturned.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private sealed class Capture : IDisposable
        {
            private readonly Rig _rig;
            private readonly Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> _sink;
            private readonly Action<string, int> _fault;
            private int _disposed;
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Rig disposes observer gates after native-stop assertions in CloseEvents.")]
            internal readonly ManualResetEvent StopEntered = new ManualResetEvent(false);
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Rig disposes observer gates after native-stop assertions in CloseEvents.")]
            internal readonly ManualResetEvent StopFinished = new ManualResetEvent(false);
            internal ProcessSnapshot Target { get; private set; }
            internal bool BlockStop { get; set; }
            internal bool ThrowStop { get; set; }

            internal Capture(Rig rig, ProcessSnapshot target, Action<Guid, int, int, int, int, long, int, byte[], string[], int[]> sink, Action<string, int> fault)
            {
                _rig = rig; Target = target; _sink = sink; _fault = fault;
            }
            internal void SixtyFrames() { for (int i = 0; i < 61; i++) Present(i * 1000L); }
            internal void Present(long timestamp) { Emit(Target.Id, timestamp); }
            internal void Emit(int processId, long timestamp)
            {
                _sink(Dxgi, 42, 0, processId, 17, timestamp, 8, StartPayload(Dxgi, 8, 1, 0), StartNames(Dxgi), StartTypes(Dxgi));
                _sink(Dxgi, 43, 0, processId, 17, timestamp + 10, 8, BitConverter.GetBytes(0), new[] { "Result" }, new[] { 7 });
            }
            internal void Fault(string state, int code) { _fault(state, code); }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                StopEntered.Set();
                if (BlockStop) _rig.ReleaseStops.WaitOne(10000);
                _rig.Closed();
                StopFinished.Set();
                GC.SuppressFinalize(this);
                if (ThrowStop) throw new System.ComponentModel.Win32Exception(5);
            }
            internal void CloseEvents() { StopEntered.Dispose(); StopFinished.Dispose(); }
        }
    }
}
#pragma warning restore 1591
