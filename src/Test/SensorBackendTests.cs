using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorBackendTests
    {
        private string _root;
        private byte[] _backend;
        [TestFixtureSetUp]
        public void BuildControlledBackend()
        {
            string scratch=Environment.GetEnvironmentVariable("PI_SCRATCH_DIR")??Environment.GetEnvironmentVariable("RUNNER_TEMP")??Path.GetTempPath();
            _root=Path.Combine(scratch,"wmc-sensor-process-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_root);
            string source=Path.Combine(_root,"child.cs"),exe=Path.Combine(_root,"child.exe");
            using(Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("WinMemoryCleaner.SensorBackendChild.cs"))
            { Assert.IsNotNull(input,"Controlled backend source must be embedded only in the test build.");using(var reader=new StreamReader(input))File.WriteAllText(source,reader.ReadToEnd(),Encoding.UTF8); }
            string runtime=RuntimeEnvironment.GetRuntimeDirectory();
            using(var compiler=new Process { StartInfo=new ProcessStartInfo(Path.Combine(runtime,"csc.exe"),"/nologo /noconfig /langversion:4 /warn:4 /warnaserror+ /platform:x64 /target:exe /define:SENSOR_BACKEND_CHILD /reference:\""+Path.Combine(runtime,"System.dll")+"\" /reference:\""+Path.Combine(runtime,"System.Web.Extensions.dll")+"\" /out:\""+exe+"\" \""+source+"\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true } })
            {
                Assert.IsTrue(compiler.Start());string output=compiler.StandardOutput.ReadToEnd()+compiler.StandardError.ReadToEnd();
                Assert.IsTrue(compiler.WaitForExit(30000));Assert.AreEqual(0,compiler.ExitCode,output);
            }
            _backend=File.ReadAllBytes(exe);
        }
        [TestFixtureTearDown]
        public void RemoveControlledFiles()
        {
            if(String.IsNullOrEmpty(_root)||!Directory.Exists(_root))return;
            // Disposal is asynchronous by contract, so wait briefly for the released payload lease.
            var watch=Stopwatch.StartNew();
            while(true)
            {
                try { Directory.Delete(_root,true);return; }
                catch(IOException) { if(watch.ElapsedMilliseconds>=5000)throw; }
                catch(UnauthorizedAccessException) { if(watch.ElapsedMilliseconds>=5000)throw; }
                Thread.Sleep(100);
            }
        }
        private SensorPayload Payload(string mode)
        {
            var files=new Dictionary<string,byte[]> {
                {"WinMemoryCleaner.Sensors.exe",_backend},{"WinMemoryCleaner.Sensors.exe.config",Encoding.UTF8.GetBytes("<configuration><startup><supportedRuntime version=\"v4.0\" /></startup></configuration>")},
                {"LibreHardwareMonitorLib.dll",Encoding.UTF8.GetBytes("unused controlled-backend file")},{"PawnIO_setup.exe",Encoding.UTF8.GetBytes("NEVER EXECUTED")},{"mode.txt",Encoding.UTF8.GetBytes(mode)} };
            var rows=new List<object>();var resources=new Dictionary<string,byte[]>();
            foreach(KeyValuePair<string,byte[]> item in files)
            {
                string hash;using(var input=new MemoryStream(item.Value))hash=SensorPayload.Hash(input);
                string resource="WinMemoryCleaner.Sensors.Payload."+item.Key+".gz";
                rows.Add(new Dictionary<string,object> { {"path",item.Key},{"length",item.Value.Length},{"sha256",hash},{"resource",resource} });
                using(var output=new MemoryStream()) { using(var gzip=new GZipStream(output,CompressionMode.Compress,true))gzip.Write(item.Value,0,item.Value.Length);resources.Add(resource,output.ToArray()); }
            }
            byte[] manifest=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string,object> { {"version",SensorProtocol.PayloadVersion},{"protocol",1},{"files",rows} }));
            return new SensorPayload(Path.Combine(_root,Guid.NewGuid().ToString("N")),manifest,delegate(string name) { return new MemoryStream(resources[name],false); });
        }
        private SensorBackendController Controller(string mode,Clock clock)
        { SensorPayload payload=Payload(mode);return new SensorBackendController(payload.Prepare,delegate { return new SensorJob(); },clock.Now,Ready); }
        private static SensorDriverState Ready() { return new SensorDriverState(true,true,new Version(2,1,0),"Ready"); }
        private static MetricValue Gpu(SensorBackendController controller)
        { MetricValue cpu,gpu;controller.GetSnapshot(out cpu,out gpu);return gpu; }
        private static bool Wait(Func<bool> condition) { return SpinWait.SpinUntil(condition,5000); }
        private static void AssertAvailable(SensorBackendController controller)
        {
            bool available=Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; });
            MetricValue gpu=Gpu(controller);
            Assert.IsTrue(available,String.Format(CultureInfo.InvariantCulture,"Backend={0}; GPU={1}; Detail={2}; PID={3}; stderr={4}",controller.BackendStatus,gpu.Status,gpu.Detail,controller.ProcessId,controller.Diagnostic));
        }
        private static bool Gone(int pid)
        { if(pid==0)return true;try { using(Process process=Process.GetProcessById(pid))return process.HasExited; }catch(ArgumentException) { return true; } }
        [Test]
        public void ConfigurationDoesNotStartWithoutDemand()
        { using(var controller=Controller("normal",new Clock())) { controller.Configure(5);Assert.AreEqual(0,controller.ProcessId);Assert.AreEqual("Disabled",controller.BackendStatus); } }
        [Test]
        public void OneOwnedProcessReceivesRefreshChangesAndStopsWithoutBlockingCaller()
        {
            using(var controller=Controller("normal",new Clock()))
            {
                var watch=Stopwatch.StartNew();Assert.AreEqual(MetricStatus.WarmingUp,Gpu(controller).Status);Assert.Less(watch.ElapsedMilliseconds,200);
                AssertAvailable(controller);int pid=controller.ProcessId;Assert.Greater(pid,0);Assert.AreEqual(42,Gpu(controller).Value);
                controller.Configure(5);Assert.IsTrue(Wait(delegate { return Gpu(controller).Value==45; }));Assert.AreEqual(pid,controller.ProcessId);
                controller.Configure(1);Assert.IsTrue(Wait(delegate { return Gpu(controller).Value==41; }));Assert.AreEqual(pid,controller.ProcessId);
                watch.Restart();controller.Suspend();Assert.Less(watch.ElapsedMilliseconds,200);Assert.AreEqual("Disabled",controller.BackendStatus);
                Assert.IsTrue(Wait(delegate { return Gone(pid)&&controller.ProcessId==0; }));
            }
        }
        [TestCase("broken-json")][TestCase("wrong-version")][TestCase("wrong-session")][TestCase("long-line")]
        [TestCase("exit")][TestCase("no-hello")][TestCase("partial-eof")][TestCase("invalid-utf8")]
        public void MalformedOrDisconnectedBackendInvalidatesSession(string mode)
        {
            using(var controller=Controller(mode,new Clock()))
            { Gpu(controller);Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Error; }));Assert.IsNull(Gpu(controller).Value);Assert.IsTrue(Wait(delegate { return controller.ProcessId==0; })); }
        }
        [Test]
        public void StderrFloodIsDrainedAndOnlyLatest16KiBRemain()
        {
            using(var controller=Controller("stderr",new Clock()))
            {
                AssertAvailable(controller);
                Assert.IsTrue(Wait(delegate { return controller.Diagnostic.EndsWith("END",StringComparison.Ordinal); }),controller.Diagnostic);
                Assert.AreEqual(16384,Encoding.UTF8.GetByteCount(controller.Diagnostic));Assert.AreEqual(42,Gpu(controller).Value);
            }
        }
        [Test]
        public void FirstStatusBudgetAndThirtySecondRetryAreMonotonic()
        {
            var clock=new Clock();int prepares=0;SensorPayload payload=Payload("hang");
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return payload.Prepare(); },delegate { return new SensorJob(); },clock.Now,Ready))
            {
                Gpu(controller);Assert.IsTrue(Wait(delegate { return controller.ProcessId>0; }));clock.Advance(11);
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Error; }));Assert.IsTrue(Wait(delegate { return controller.ProcessId==0; }));
                for(int i=0;i<10;i++)Gpu(controller);Assert.AreEqual(1,prepares);
                clock.Advance(29);Gpu(controller);Assert.AreEqual(1,prepares);
                clock.Advance(1);Assert.IsTrue(Wait(delegate { Gpu(controller);return prepares==2; }));
            }
        }
        [Test]
        public void SlowPreparationDoesNotConsumeFirstStatusBudget()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                var clock=new Clock();SensorPayload payload=Payload("normal");
                var controller=new SensorBackendController(delegate { entered.Set();release.WaitOne();return payload.Prepare(); },delegate { return new SensorJob(); },clock.Now,Ready);
                try
                {
                    Gpu(controller);Assert.IsTrue(entered.WaitOne(2000));clock.Advance(20);
                    Assert.AreEqual(MetricStatus.WarmingUp,Gpu(controller).Status,"The handshake budget starts only after the child starts.");
                    release.Set();AssertAvailable(controller);
                }
                finally { release.Set();controller.Dispose(); }
            }
        }
        [Test]
        public void SuspendSendsSessionStopBeforeTerminatingOwnedChild()
        {
            string marker=Path.Combine(_root,Guid.NewGuid().ToString("N")+".stopped");
            using(var controller=Controller("stop-marker:"+marker,new Clock()))
            {
                AssertAvailable(controller);int pid=controller.ProcessId;
                controller.Suspend();Assert.IsTrue(Wait(delegate { return Gone(pid)&&controller.ProcessId==0; }));
                Assert.IsTrue(File.Exists(marker),"A normal shutdown must let the child receive its session-bound stop.");
                Assert.AreEqual("session-stop",File.ReadAllText(marker));
            }
        }
        [TestCase("duplicate")][TestCase("one-frame")]
        public void StaleOrRepeatedFramesCannotKeepOldTemperatureAlive(string mode)
        {
            var clock=new Clock();using(var controller=Controller(mode,clock))
            {
                AssertAvailable(controller);clock.Advance(11);
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Error; }));Assert.IsNull(Gpu(controller).Value);
            }
        }
        [Test]
        public void FutureFramesDoNotFinishStartup()
        {
            var clock=new Clock();using(var controller=Controller("future",clock))
            { Gpu(controller);Assert.IsTrue(Wait(delegate { return controller.ProcessId>0; }));Assert.AreEqual(MetricStatus.WarmingUp,Gpu(controller).Status);clock.Advance(11);Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Error; })); }
        }
        [Test]
        public void CancelDuringPreparationReleasesLeaseAndNeverStartsLateChild()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                SensorPayload payload=Payload("normal");int completed=0;SensorPayloadLease prepared=null;
                var controller=new SensorBackendController(delegate { entered.Set();release.WaitOne();try { prepared=payload.Prepare();return prepared; }finally { Interlocked.Increment(ref completed); } },delegate { return new SensorJob(); },new Clock().Now,Ready);
                try
                {
                    Gpu(controller);Assert.IsTrue(entered.WaitOne(2000));var watch=Stopwatch.StartNew();controller.Dispose();Assert.Less(watch.ElapsedMilliseconds,200);
                    release.Set();Assert.IsTrue(Wait(delegate { return completed==1&&controller.ProcessId==0; }));Assert.AreEqual(MetricStatus.Disabled,Gpu(controller).Status);
                    Assert.IsTrue(Wait(delegate { return prepared!=null&&CanWrite(prepared.Executable); }),"Canceled preparation must release its owned verified lease.");
                }
                finally { release.Set();controller.Dispose();if(prepared!=null)prepared.Dispose(); }
            }
        }
        [Test]
        public void SuspendDuringPreparationReleasesLeaseAndDemandCanResume()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                SensorPayload payload=Payload("normal");int completed=0;SensorPayloadLease prepared=null;
                var controller=new SensorBackendController(delegate { if(Interlocked.Increment(ref completed)==1) { entered.Set();release.WaitOne();prepared=payload.Prepare();return prepared; }return payload.Prepare(); },delegate { return new SensorJob(); },new Clock().Now,Ready);
                try
                {
                    Gpu(controller);Assert.IsTrue(entered.WaitOne(2000));controller.Suspend();Assert.AreEqual("Disabled",controller.BackendStatus);release.Set();
                    Assert.IsTrue(Wait(delegate { return prepared!=null&&CanWrite(prepared.Executable); }),"Suspended preparation must release its lease before the next session.");
                    AssertAvailable(controller);
                }
                finally { release.Set();controller.Dispose();if(prepared!=null)prepared.Dispose(); }
            }
        }
        private static bool CanWrite(string path)
        {
            try { using(var input=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None))return input.CanWrite; }
            catch(IOException) { return false; }
        }
        [Test]
        public void JobAssignmentFailureKillsOnlyNewlyOwnedChild()
        {
            int pid=0;SensorPayload payload=Payload("normal");
            using(var controller=new SensorBackendController(payload.Prepare,delegate { return new RefusingJob(delegate(int value) { pid=value; }); },new Clock().Now,Ready))
            { Gpu(controller);Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Error; }));Assert.Greater(pid,0);Assert.IsTrue(Wait(delegate { return Gone(pid); })); }
        }
        [Test]
        public void DriverResetRemovesBackoffAndRestartsRatherThanCreatingSecondChild()
        {
            using(var controller=Controller("normal",new Clock()))
            {
                AssertAvailable(controller);int old=controller.ProcessId;
                controller.ResetDriverState();Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available&&controller.ProcessId!=old; }));Assert.IsTrue(Gone(old));
            }
        }
        [Test]
        public void DriverResetAfterSuspendNeverRestoresCancelledDemand()
        {
            using(var controller=Controller("normal",new Clock()))
            {
                AssertAvailable(controller);int pid=controller.ProcessId;
                controller.Suspend();controller.ResetDriverState();Assert.AreEqual("Disabled",controller.BackendStatus);
                Assert.IsTrue(Wait(delegate { return Gone(pid)&&controller.ProcessId==0; }));
                Thread.Sleep(150);Assert.AreEqual(0,controller.ProcessId);Assert.AreEqual("Disabled",controller.BackendStatus);
            }
        }
        [Test]
        public void DisposeOfUnresponsiveBackendIsAsyncAndKillsOwnedIdentity()
        {
            var controller=Controller("one-frame",new Clock());int pid=0;
            try
            { AssertAvailable(controller);pid=controller.ProcessId;var watch=Stopwatch.StartNew();controller.Dispose();Assert.Less(watch.ElapsedMilliseconds,200);Assert.IsTrue(Wait(delegate { return Gone(pid)&&controller.ProcessId==0; }));Assert.AreEqual(MetricStatus.Disabled,Gpu(controller).Status); }
            finally { controller.Dispose(); }
        }
        private static MetricValue Requested(SensorBackendController controller,bool cpuRequired,bool gpuRequired,bool cpuMetric)
        { MetricValue cpu,gpu;controller.GetSnapshot(cpuRequired,gpuRequired,out cpu,out gpu);return cpuMetric?cpu:gpu; }
        private static SensorDriverState Denied() { return new SensorDriverState(false,true,null,"AccessDenied"); }
        private static bool WorkerStopped(SensorBackendController controller)
        {
            var worker=(Thread)typeof(SensorBackendController).GetField("_worker",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);
            return worker==null||!worker.IsAlive;
        }
        [Test]
        public void NoMissingMetricDoesNotInspectPrepareOrStart()
        {
            int inspections=0,prepares=0,jobs=0;
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return null; },delegate { Interlocked.Increment(ref jobs);return null; },new Clock().Now,delegate { Interlocked.Increment(ref inspections);return Ready(); }))
            {
                for(int i=0;i<20;i++)Assert.AreEqual(MetricStatus.Disabled,Requested(controller,false,false,true).Status);
                controller.Configure(5);controller.ResetDriverState();Thread.Sleep(100);
                Assert.AreEqual(0,inspections);Assert.AreEqual(0,prepares);Assert.AreEqual(0,jobs);Assert.AreEqual(0,controller.ProcessId);
            }
        }
        [TestCase("AccessDenied","AccessDenied")][TestCase("NotInstalled","Unsupported")]
        [TestCase("UnverifiedDriver","Unsupported")][TestCase("DriverNotReady","Unsupported")][TestCase("InspectionFailed","Error")]
        public void CpuOnlyBlockedDriverDefersWithoutPayloadProcessOrResidentWorker(string status,string expected)
        {
            var clock=new Clock();int inspections=0,prepares=0,jobs=0;
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return null; },delegate { Interlocked.Increment(ref jobs);return null; },clock.Now,
                delegate { Interlocked.Increment(ref inspections);return new SensorDriverState(false,status!="NotInstalled",null,status); }))
            {
                Requested(controller,true,false,true);
                Assert.IsTrue(Wait(delegate { return controller.BackendStatus=="WaitingForDriver"&&WorkerStopped(controller); }));
                MetricValue cpu=Requested(controller,true,false,true);
                Assert.AreEqual(expected,cpu.Status.ToString());Assert.IsNull(cpu.Value);StringAssert.Contains(status,cpu.Detail);Assert.AreEqual(status,controller.DriverStatus);
                for(int i=0;i<20;i++)Requested(controller,true,false,true);
                controller.Configure(5);clock.Advance(29);Requested(controller,true,false,true);Assert.AreEqual(1,inspections);
                clock.Advance(1);Assert.IsTrue(Wait(delegate { Requested(controller,true,false,true);return inspections==2&&WorkerStopped(controller); }));
                Assert.AreEqual(0,prepares);Assert.AreEqual(0,jobs);Assert.AreEqual(0,controller.ProcessId);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void FailedOrNullDriverInspectionStillCannotStartCpuOnlyBackend(bool throws)
        {
            int prepares=0;
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return null; },delegate { return new SensorJob(); },new Clock().Now,
                delegate { if(throws)throw new InvalidOperationException("inspection failure");return null; }))
            {
                Assert.IsTrue(Wait(delegate { return Requested(controller,true,false,true).Status==MetricStatus.Error; }));
                Assert.AreEqual("InspectionFailed",controller.DriverStatus);Assert.AreEqual("WaitingForDriver",controller.BackendStatus);
                Assert.AreEqual(0,prepares);Assert.AreEqual(0,controller.ProcessId);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void CpuOnlyDriverCanRecoverOnRetryOrExplicitReset(bool reset)
        {
            var clock=new Clock();int allowed=0,prepares=0;SensorPayload payload=Payload("normal");
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return payload.Prepare(); },delegate { return new SensorJob(); },clock.Now,
                delegate { return Interlocked.CompareExchange(ref allowed,0,0)==0?Denied():Ready(); }))
            {
                Requested(controller,true,false,true);Assert.IsTrue(Wait(delegate { return controller.BackendStatus=="WaitingForDriver"; }));
                Interlocked.Exchange(ref allowed,1);if(reset)controller.ResetDriverState();else clock.Advance(30);
                Assert.IsTrue(Wait(delegate { return Requested(controller,true,false,true).Value==54; }));
                Assert.AreEqual("Ready",controller.DriverStatus);Assert.Greater(controller.ProcessId,0);Assert.AreEqual(1,prepares);
                Assert.AreEqual(MetricStatus.Disabled,Requested(controller,true,false,false).Status);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void MissingGpuStillStartsFallbackEvenWhenCpuIsDenied(bool cpuRequired)
        {
            string marker=Path.Combine(_root,Guid.NewGuid().ToString("N")+".config.json");SensorPayload payload=Payload("config-marker:"+marker);
            using(var controller=new SensorBackendController(payload.Prepare,delegate { return new SensorJob(); },new Clock().Now,Denied))
            {
                Assert.IsTrue(Wait(delegate { return Requested(controller,cpuRequired,true,false).Value==42; }));
                Assert.Greater(controller.ProcessId,0);Assert.AreEqual("AccessDenied",controller.DriverStatus);
                Assert.AreEqual(cpuRequired?MetricStatus.AccessDenied:MetricStatus.Disabled,Requested(controller,cpuRequired,true,true).Status);
                var config=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(marker)) as Dictionary<string,object>;
                Assert.IsFalse((bool)config["cpuEnabled"],"The parent trust gate must remain closed.");
            }
        }
        [Test]
        public void GpuOnlyDemandDoesNotEnableCpuEvenWhenDriverIsTrusted()
        {
            string marker=Path.Combine(_root,Guid.NewGuid().ToString("N")+".config.json");
            using(var controller=Controller("config-marker:"+marker,new Clock()))
            {
                Assert.IsTrue(Wait(delegate { return Requested(controller,false,true,false).Value==42; }));
                var config=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(marker)) as Dictionary<string,object>;
                Assert.IsFalse((bool)config["cpuEnabled"]);Assert.AreEqual(MetricStatus.Disabled,Requested(controller,false,true,true).Status);
            }
        }
        [Test]
        public void GpuRecoveryStopsFallbackAndLossBypassesCpuOnlyBackoff()
        {
            int prepares=0;SensorPayload payload=Payload("normal");
            using(var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return payload.Prepare(); },delegate { return new SensorJob(); },new Clock().Now,Denied))
            {
                AssertAvailable(controller);int first=controller.ProcessId;
                Requested(controller,true,false,true);
                Assert.IsTrue(Wait(delegate { return controller.BackendStatus=="WaitingForDriver"&&controller.ProcessId==0&&Gone(first); }));
                Assert.AreEqual(1,prepares);
                AssertAvailable(controller);Assert.AreEqual(2,prepares);Assert.AreNotEqual(first,controller.ProcessId);Assert.IsTrue(Gone(first));
                int second=controller.ProcessId;Requested(controller,false,false,true);
                Assert.IsTrue(Wait(delegate { return controller.ProcessId==0&&Gone(second); }));Assert.AreEqual("Disabled",controller.BackendStatus);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void SuspendOrDisposeDuringTrustInspectionCannotPrepareLatePayload(bool dispose)
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                int inspections=0,prepares=0;SensorPayload payload=Payload("normal");
                var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return payload.Prepare(); },delegate { return new SensorJob(); },new Clock().Now,
                    delegate { if(Interlocked.Increment(ref inspections)==1) { entered.Set();if(!release.WaitOne(5000))throw new TimeoutException(); }return Ready(); });
                try
                {
                    var watch=Stopwatch.StartNew();Requested(controller,true,false,true);Assert.Less(watch.ElapsedMilliseconds,200);Assert.IsTrue(entered.WaitOne(2000));
                    watch.Restart();if(dispose)controller.Dispose();else controller.Suspend();Assert.Less(watch.ElapsedMilliseconds,200);
                    release.Set();Assert.IsTrue(Wait(delegate { return WorkerStopped(controller); }));
                    Assert.AreEqual(0,prepares);Assert.AreEqual(0,controller.ProcessId);Assert.AreEqual("Disabled",controller.BackendStatus);
                    if(!dispose)Assert.IsTrue(Wait(delegate { return Requested(controller,true,false,true).Value==54; }));
                }
                finally { release.Set();controller.Dispose(); }
            }
        }
        [Test]
        public void DemandChangeDuringPreparationNeverLaunchesObsoleteGpuChild()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                int prepares=0,jobs=0;SensorPayload payload=Payload("normal");SensorPayloadLease prepared=null;
                var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);entered.Set();if(!release.WaitOne(5000))throw new TimeoutException();prepared=payload.Prepare();return prepared; },
                    delegate { Interlocked.Increment(ref jobs);return new SensorJob(); },new Clock().Now,Denied);
                try
                {
                    Gpu(controller);Assert.IsTrue(entered.WaitOne(2000));Requested(controller,true,false,true);release.Set();
                    Assert.IsTrue(Wait(delegate { return controller.BackendStatus=="WaitingForDriver"&&WorkerStopped(controller); }));
                    Assert.AreEqual(1,prepares);Assert.AreEqual(0,jobs);Assert.AreEqual(0,controller.ProcessId);Assert.IsTrue(CanWrite(prepared.Executable));
                }
                finally { release.Set();controller.Dispose();if(prepared!=null)prepared.Dispose(); }
            }
        }
        [Test]
        public void ResetDuringInspectionDoesNotPublishObsoleteDriverDenial()
        {
            using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))
            {
                int inspections=0,prepares=0;SensorPayload payload=Payload("normal");
                var controller=new SensorBackendController(delegate { Interlocked.Increment(ref prepares);return payload.Prepare(); },delegate { return new SensorJob(); },new Clock().Now,
                    delegate { if(Interlocked.Increment(ref inspections)==1) { entered.Set();if(!release.WaitOne(5000))throw new TimeoutException();return Denied(); }return Ready(); });
                try
                {
                    Requested(controller,true,false,true);Assert.IsTrue(entered.WaitOne(2000));controller.ResetDriverState();release.Set();
                    Assert.IsTrue(Wait(delegate { return Requested(controller,true,false,true).Value==54; }));
                    Assert.AreEqual("Ready",controller.DriverStatus);Assert.AreEqual(2,inspections);Assert.AreEqual(1,prepares);
                }
                finally { release.Set();controller.Dispose(); }
            }
        }
        private sealed class Clock
        {
            private long _ticks=Stopwatch.GetTimestamp();
            internal long Now() { return Interlocked.Read(ref _ticks); }
            internal void Advance(int seconds) { Interlocked.Add(ref _ticks,seconds*Stopwatch.Frequency); }
        }
        private sealed class RefusingJob : ISensorJob
        {
            private readonly Action<int> _capture;internal RefusingJob(Action<int> capture) { _capture=capture; }
            public bool Assign(Process process) { _capture(process.Id);return false; }
            public void Dispose() { GC.SuppressFinalize(this); }
        }
    }
}
#pragma warning restore 1591
