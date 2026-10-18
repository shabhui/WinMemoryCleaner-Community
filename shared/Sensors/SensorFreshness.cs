using System;
using System.Diagnostics;

namespace WinMemoryCleaner.Sensors
{
    internal sealed class SensorFreshness
    {
        private readonly long _frequency;
        private bool _accepted;
        private long _sequence=-1,_received;
        private DateTime _sample;
        private double _ageAtReceive;
        internal SensorFreshness() : this(Stopwatch.Frequency) { }
        internal SensorFreshness(long frequency)
        { if(frequency<=0)throw new ArgumentOutOfRangeException("frequency");_frequency=frequency; }
        internal bool Accept(SensorMessage message,DateTime utcNow,long monotonicTicks,int refreshSeconds)
        {
            if(message==null)throw new ArgumentNullException("message");
            if(message.Kind!="snapshot"||message.Sequence<=_sequence||message.SampleUtc.Kind!=DateTimeKind.Utc||utcNow.Kind!=DateTimeKind.Utc)return false;
            double age=(utcNow-message.SampleUtc).TotalSeconds;
            if(age< -5||age>SensorProtocol.MaximumAge(refreshSeconds))return false;
            _accepted=true;_sequence=message.Sequence;_received=monotonicTicks;_sample=message.SampleUtc;_ageAtReceive=Math.Max(0,age);
            return true;
        }
        internal bool IsFresh(DateTime utcNow,long monotonicTicks,int refreshSeconds)
        {
            if(!_accepted||monotonicTicks<_received||utcNow.Kind!=DateTimeKind.Utc)return false;
            double elapsed=(monotonicTicks-_received)/(double)_frequency,age=(utcNow-_sample).TotalSeconds;
            return age>= -5&&Math.Max(age,_ageAtReceive+elapsed)<=SensorProtocol.MaximumAge(refreshSeconds);
        }
        internal void Reset() { _accepted=false;_sequence=-1;_received=0;_ageAtReceive=0; }
    }
}
