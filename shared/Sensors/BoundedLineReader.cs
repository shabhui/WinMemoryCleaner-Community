using System;
using System.IO;
using System.Text;

namespace WinMemoryCleaner.Sensors
{
    // The borrowed pipe is disposed by its process owner, not by this reader.
    internal sealed class BoundedLineReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer=new byte[4096];
        private readonly byte[] _line=new byte[SensorProtocol.MaximumFrameBytes];
        private int _position,_count;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        internal BoundedLineReader(Stream stream)
        { if(stream==null)throw new ArgumentNullException("stream");_stream=stream; }
        private int NextByte()
        {
            if(_position>=_count) { _count=_stream.Read(_buffer,0,_buffer.Length);_position=0;if(_count==0)return -1; }
            return _buffer[_position++];
        }
        internal string ReadLine()
        {
            int length=0;
            while(true)
            {
                int value=NextByte();
                if(value<0) { if(length==0)return null;throw new InvalidDataException("Truncated sensor frame."); }
                if(value==10)
                {
                    if(length>0&&_line[length-1]==13)length--;
                    try { return Utf8.GetString(_line,0,length); }
                    catch(DecoderFallbackException ex) { throw new InvalidDataException("Invalid sensor UTF-8.",ex); }
                }
                if(length>=_line.Length)throw new InvalidDataException("Sensor frame exceeds 64 KiB before newline.");
                _line[length++]=(byte)value;
            }
        }
    }
}
