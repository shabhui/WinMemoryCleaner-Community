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
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));int pid=controller.ProcessId;Assert.Greater(pid,0);Assert.AreEqual(42,Gpu(controller).Value);
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
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));
                Assert.IsTrue(Wait(delegate { return controller.Diagnostic.EndsWith("END",StringComparison.Ordinal); }));
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
                    release.Set();Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));
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
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));int pid=controller.ProcessId;
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
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));clock.Advance(11);
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
                    Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }),"New demand must restart after canceled preparation.");
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
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));int old=controller.ProcessId;
                controller.ResetDriverState();Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available&&controller.ProcessId!=old; }));Assert.IsTrue(Gone(old));
            }
        }
        [Test]
        public void DriverResetAfterSuspendNeverRestoresCancelledDemand()
        {
            using(var controller=Controller("normal",new Clock()))
            {
                Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));int pid=controller.ProcessId;
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
            { Assert.IsTrue(Wait(delegate { return Gpu(controller).Status==MetricStatus.Available; }));pid=controller.ProcessId;var watch=Stopwatch.StartNew();controller.Dispose();Assert.Less(watch.ElapsedMilliseconds,200);Assert.IsTrue(Wait(delegate { return Gone(pid)&&controller.ProcessId==0; }));Assert.AreEqual(MetricStatus.Disabled,Gpu(controller).Status); }
            finally { controller.Dispose(); }
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
