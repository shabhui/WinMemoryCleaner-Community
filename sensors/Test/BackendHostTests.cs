using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Sensors.Test
{
    [TestFixture]
    public sealed class BackendHostTests
    {
        private const string Session="123456789012345678901234567890ab";
        [Test]
        public void BackendEntryRejectsInvalidOrSelfParent()
        {
            Assert.AreEqual(2,Program.Main(null));
            using(Process process=Process.GetCurrentProcess())
                Assert.AreEqual(2,Program.Main(new[] { "--parent-pid",process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),"--parent-start-ticks",process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
        [Test]
        public void ParentParserAcceptsOnlyFixedPositiveIdentityArguments()
        {
            int pid;long ticks;
            Assert.IsTrue(SensorParentIdentity.TryParse(new[] { "--parent-pid","123","--parent-start-ticks","456" },out pid,out ticks));
            Assert.AreEqual(123,pid);Assert.AreEqual(456,ticks);
            Assert.IsFalse(SensorParentIdentity.TryParse(new[] { "--parent-pid","0","--parent-start-ticks","456" },out pid,out ticks));
            Assert.IsFalse(SensorParentIdentity.TryParse(new[] { "--parent-pid","123","--parent-start-ticks","-1" },out pid,out ticks));
            Assert.IsFalse(SensorParentIdentity.TryParse(new[] { "--parent-pid","123","--parent-pid","456" },out pid,out ticks));
            Assert.IsFalse(SensorParentIdentity.TryParse(new[] { "--parent-pid","123","--parent-start-ticks","456","extra" },out pid,out ticks));
        }
        [Test]
        public void ParentMatchRequiresCreationTimeNotJustPid()
        {
            using(Process process=Process.GetCurrentProcess())
            {
                long ticks=process.StartTime.ToUniversalTime().Ticks;
                Assert.IsTrue(SensorParentIdentity.Matches(process,process.Id,ticks));
                Assert.IsFalse(SensorParentIdentity.Matches(process,process.Id,ticks+1));
                Assert.IsFalse(SensorParentIdentity.Matches(process,process.Id+1,ticks));
            }
        }
        [Test]
        public void HostWaitsForConfigurationThenSendsOneHelloAndStatusFrame()
        {
            using(var harness=new HostHarness())
            {
                Assert.IsFalse(SpinWait.SpinUntil(delegate { return harness.Calls>0; },50));
                harness.Send(new SensorMessage { Kind="configure",Session=Session,RefreshSeconds=2,CpuEnabled=false });
                Assert.IsTrue(SpinWait.SpinUntil(delegate { return harness.Frames().Count>=2; },2000));
                IList<SensorMessage> frames=harness.Frames();
                Assert.AreEqual("hello",frames[0].Kind);Assert.AreEqual(Session,frames[0].Session);
                Assert.AreEqual("snapshot",frames[1].Kind);Assert.AreEqual("Unsupported",frames[1].CpuStatus);Assert.AreEqual(0,frames[1].Readings.Count);
                Assert.IsFalse(harness.LastCpu);
                harness.Send(new SensorMessage { Kind="stop",Session=Session });
                Assert.IsTrue(harness.Join());Assert.AreEqual(0,harness.ExitCode);
            }
        }
        [Test]
        public void ConfigureUpdatesCpuWithoutExtraHandshake()
        {
            using(var harness=new HostHarness())
            {
                harness.Send(new SensorMessage { Kind="configure",Session=Session,RefreshSeconds=5,CpuEnabled=false });
                Assert.IsTrue(SpinWait.SpinUntil(delegate { return harness.Calls>0; },2000));
                harness.Send(new SensorMessage { Kind="configure",Session=Session,RefreshSeconds=1,CpuEnabled=true });
                Assert.IsTrue(SpinWait.SpinUntil(delegate { return harness.LastCpu; },2000));
                int hellos=0;foreach(SensorMessage frame in harness.Frames())if(frame.Kind=="hello")hellos++;
                Assert.AreEqual(1,hellos);Assert.GreaterOrEqual(harness.Calls,2);
            }
        }
        [Test]
        public void EndOfInputStopsWithoutHardwareCall()
        {
            using(var harness=new HostHarness()) { harness.Input.Complete();Assert.IsTrue(harness.Join());Assert.AreEqual(0,harness.Calls); }
        }
        [Test]
        public void InvalidCommandStopsRatherThanRunningHardware()
        {
            using(var harness=new HostHarness())
            { harness.Send(new SensorMessage { Kind="hello",Session=Session });Assert.IsTrue(harness.Join());Assert.AreEqual(2,harness.ExitCode);Assert.AreEqual(0,harness.Calls); }
        }
        [Test]
        public void ChangedSessionStopsCurrentHost()
        {
            using(var harness=new HostHarness())
            {
                harness.Send(new SensorMessage { Kind="configure",Session=Session,RefreshSeconds=2 });
                Assert.IsTrue(SpinWait.SpinUntil(delegate { return harness.Calls>0; },2000));
                harness.Send(new SensorMessage { Kind="configure",Session="fedcba9876543210fedcba9876543210",RefreshSeconds=2 });
                Assert.IsTrue(harness.Join());Assert.AreEqual(2,harness.ExitCode);
            }
        }
        [Test]
        public void FrameWithoutNewlineIsRejectedAtByteLimit()
        {
            using(var harness=new HostHarness())
            { byte[] bytes=new byte[SensorProtocol.MaximumFrameBytes+1];for(int i=0;i<bytes.Length;i++)bytes[i]=65;harness.Input.Write(bytes,0,bytes.Length);Assert.IsTrue(harness.Join());Assert.AreEqual(2,harness.ExitCode); }
        }
        [Test]
        public void ParentIdentityLossStopsHost()
        { using(var harness=new HostHarness()) { harness.ParentAlive=false;Assert.IsTrue(harness.Join());Assert.AreEqual(0,harness.Calls); } }

        private sealed class HostHarness : IDisposable
        {
            internal readonly BlockingInput Input=new BlockingInput();
            private readonly CaptureOutput _output=new CaptureOutput();
            private readonly SensorHost _host;
            private readonly Thread _thread;
            private int _calls;
            private volatile bool _cpu;
            internal volatile bool ParentAlive=true;
            internal int ExitCode;
            internal int Calls { get { return Interlocked.CompareExchange(ref _calls,0,0); } }
            internal bool LastCpu { get { return _cpu; } }
            internal HostHarness()
            {
                _host=new SensorHost(Input,_output,delegate(bool cpu)
                { _cpu=cpu;Interlocked.Increment(ref _calls);return new SensorMessage { Kind="snapshot",SampleUtc=DateTime.UtcNow,CpuStatus="Unsupported",GpuStatus="Unsupported",Detail="No devices" }; },delegate { return ParentAlive; });
                _thread=new Thread(delegate() { ExitCode=_host.Run(); }) { IsBackground=true };_thread.Start();
            }
            internal void Send(SensorMessage message)
            { byte[] bytes=Encoding.UTF8.GetBytes(SensorProtocol.Encode(message)+"\n");Input.Write(bytes,0,bytes.Length); }
            internal IList<SensorMessage> Frames()
            {
                var frames=new List<SensorMessage>();
                foreach(string line in Encoding.UTF8.GetString(_output.Bytes()).Split('\n'))if(!String.IsNullOrEmpty(line))frames.Add(SensorProtocol.Decode(line));
                return frames;
            }
            internal bool Join() { return _thread.Join(2000); }
            public void Dispose() { Input.Complete();_host.Dispose();_thread.Join(2000);Input.Dispose();_output.Dispose(); }
        }
        private sealed class CaptureOutput : MemoryStream
        {
            private readonly object _gate=new object();
            public override void Write(byte[] buffer,int offset,int count) { lock(_gate)base.Write(buffer,offset,count); }
            internal byte[] Bytes() { lock(_gate)return ToArray(); }
        }
        private sealed class BlockingInput : Stream
        {
            private readonly Queue<byte> _bytes=new Queue<byte>();private bool _complete;
            internal void Complete() { lock(_bytes) { _complete=true;Monitor.PulseAll(_bytes); } }
            public override int Read(byte[] buffer,int offset,int count)
            {
                lock(_bytes)
                {
                    while(_bytes.Count==0&&!_complete)Monitor.Wait(_bytes);
                    int read=0;while(read<count&&_bytes.Count>0)buffer[offset+read++]=_bytes.Dequeue();return read;
                }
            }
            public override void Write(byte[] buffer,int offset,int count) { lock(_bytes) { for(int i=0;i<count;i++)_bytes.Enqueue(buffer[offset+i]);Monitor.PulseAll(_bytes); } }
            protected override void Dispose(bool disposing) { if(disposing)Complete();base.Dispose(disposing); }
            public override bool CanRead { get { return true; } }public override bool CanWrite { get { return true; } }public override bool CanSeek { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }public override long Position { get { throw new NotSupportedException(); }set { throw new NotSupportedException(); } }
            public override void Flush() { }public override long Seek(long offset,SeekOrigin origin) { throw new NotSupportedException(); }public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }
}
#pragma warning restore 1591
