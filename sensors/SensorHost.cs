using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace WinMemoryCleaner.Sensors
{
    /// <summary>Consumes only configuration/stop; never exposes hardware control commands.</summary>
    internal sealed class SensorHost : IDisposable
    {
        private readonly object _gate=new object();
        // Borrowed streams are closed by Program/the harness after Run returns.
        private readonly Stream _input,_output;
        private readonly Func<bool,SensorMessage> _sample;
        private readonly Func<bool> _parentAlive;
        private bool _stopped,_disposed,_configured,_cpu;
        private int _seconds=2,_revision,_exitCode;
        private string _session,_diagnostic=string.Empty;
        internal string Diagnostic { get { lock(_gate)return _diagnostic; } }
        internal SensorHost(Stream input,Stream output,Func<bool,SensorMessage> sample,Func<bool> parentAlive)
        {
            if(input==null)throw new ArgumentNullException("input");
            if(output==null)throw new ArgumentNullException("output");
            if(sample==null)throw new ArgumentNullException("sample");
            if(parentAlive==null)throw new ArgumentNullException("parentAlive");
            _input=input;_output=output;_sample=sample;_parentAlive=parentAlive;
        }
        private static double Now() { return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency; }
        internal int Run()
        {
            lock(_gate)if(_disposed)throw new ObjectDisposedException("SensorHost");
            var reader=new Thread(ReadCommands) { IsBackground=true,Name="Sensor configuration" };reader.Start();
            double started=Now(),next=0;int observed=-1;long sequence=0;bool hello=false;
            try
            {
                while(true)
                {
                    if(!_parentAlive())return 0;
                    bool configured,cpu;int seconds,revision;string session;
                    lock(_gate)
                    {
                        if(_stopped)return _exitCode;
                        configured=_configured;cpu=_cpu;seconds=_seconds;revision=_revision;session=_session;
                    }
                    double now=Now();
                    if(!configured&&now-started>=10)throw new IOException("Initial sensor configuration timed out.");
                    if(configured&&(revision!=observed||now>=next))
                    {
                        if(!hello) { Send(new SensorMessage { Kind="hello",Session=session });hello=true; }
                        SensorMessage message=_sample(cpu);
                        if(message==null)throw new InvalidDataException("Sensor source returned no status frame.");
                        message.Kind="snapshot";message.Session=session;message.Sequence=sequence++;
                        Send(message);observed=revision;next=Now()+seconds;
                    }
                    lock(_gate)if(!_stopped)Monitor.Wait(_gate,100);
                }
            }
            catch(Exception error) { Fail(error);return 2; }
            finally { lock(_gate) { _stopped=true;Monitor.PulseAll(_gate); } }
        }
        private void Send(SensorMessage message)
        {
            byte[] bytes=new UTF8Encoding(false,true).GetBytes(SensorProtocol.Encode(message)+"\n");
            _output.Write(bytes,0,bytes.Length);_output.Flush();
        }
        private void ReadCommands()
        {
            try
            {
                var reader=new BoundedLineReader(_input);string line;
                while((line=reader.ReadLine())!=null)
                {
                    SensorMessage message=SensorProtocol.Decode(line);
                    lock(_gate)
                    {
                        if(_stopped)return;
                        if(message.Kind!="configure"&&message.Kind!="stop")throw new InvalidDataException("Unexpected parent command.");
                        if(_configured&&!String.Equals(message.Session,_session,StringComparison.Ordinal))throw new InvalidDataException("Sensor session changed.");
                        if(message.Kind=="stop") { _stopped=true;Monitor.PulseAll(_gate);return; }
                        _configured=true;_session=message.Session;_cpu=message.CpuEnabled;_seconds=message.RefreshSeconds;_revision++;
                        Monitor.PulseAll(_gate);
                    }
                }
                lock(_gate) { _stopped=true;Monitor.PulseAll(_gate); }
            }
            catch(Exception error) { Fail(error); }
        }
        private void Fail(Exception error)
        {
            lock(_gate)
            {
                if(_stopped)return;
                _exitCode=2;_diagnostic=error.GetType().Name+": "+error.Message;
                if(_diagnostic.Length>1024)_diagnostic=_diagnostic.Substring(0,1024);
                _stopped=true;Monitor.PulseAll(_gate);
            }
        }
        public void Dispose() { Dispose(true);GC.SuppressFinalize(this); }
        private void Dispose(bool disposing)
        { if(disposing)lock(_gate) { _disposed=true;_stopped=true;Monitor.PulseAll(_gate); } }
    }
}
