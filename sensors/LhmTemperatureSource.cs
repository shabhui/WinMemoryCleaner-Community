using System;
using System.Collections.Generic;
using System.Security;
using LibreHardwareMonitor.Hardware;

namespace WinMemoryCleaner.Sensors
{
    /// <summary>Temperature-only adapter. The host serializes all sampling and disposal.</summary>
    internal sealed class LhmTemperatureSource : IDisposable
    {
        private readonly Func<SensorDriverState> _inspect;
        private readonly Func<bool,IList<SensorReading>> _acquire;
        private readonly Action _release;
        private Computer _computer;
        private bool _cpu,_disposed,_cpuReadFailed,_gpuReadFailed;
        private string _hardwareError=string.Empty;

        internal LhmTemperatureSource()
        { _inspect=SensorDriverTrust.Inspect;_acquire=ReadComputer;_release=CloseComputer; }
        internal LhmTemperatureSource(Func<SensorDriverState> inspect,Func<bool,IList<SensorReading>> acquire,Action release)
        {
            if(inspect==null)throw new ArgumentNullException("inspect");
            if(acquire==null)throw new ArgumentNullException("acquire");
            if(release==null)throw new ArgumentNullException("release");
            _inspect=inspect;_acquire=acquire;_release=release;
        }
        internal static Computer CreateComputer(bool cpuAllowed)
        {
            return new Computer(new DefaultSensorSettings())
            {
                IsCpuEnabled=cpuAllowed,IsGpuEnabled=true,
                IsBatteryEnabled=false,IsControllerEnabled=false,IsPowerMonitorEnabled=false,
                IsMemoryEnabled=false,IsMotherboardEnabled=false,IsNetworkEnabled=false,
                IsPsuEnabled=false,IsStorageEnabled=false
            };
        }
        internal SensorMessage Sample(bool cpuAllowed)
        {
            if(_disposed)throw new ObjectDisposedException("LhmTemperatureSource");
            SensorDriverState driver;
            try { driver=_inspect()??new SensorDriverState(false,true,null,"InspectionFailed"); }
            catch(Exception error) { driver=new SensorDriverState(false,true,null,Denied(error)?"AccessDenied":"InspectionFailed"); }
            bool enabled=cpuAllowed&&driver.CpuAllowed;
            var message=new SensorMessage { Kind="snapshot",CpuStatus=cpuAllowed?(driver.Status=="AccessDenied"?"AccessDenied":"Unsupported"):"Disabled",GpuStatus="Unsupported" };
            _hardwareError=string.Empty;_cpuReadFailed=false;_gpuReadFailed=false;
            try
            {
                var candidates=new List<SensorReading>();
                IList<SensorReading> acquired=_acquire(enabled);
                if(acquired!=null)foreach(SensorReading reading in acquired)
                    if(reading!=null&&(enabled||!String.Equals(reading.HardwareType,"Cpu",StringComparison.Ordinal)))candidates.Add(reading);
                var selected=new List<SensorReading>(TemperatureSelection.Select(candidates));
                // Retain the hottest valid representatives if a machine exposes more than the wire limit.
                selected.Sort(delegate(SensorReading left,SensorReading right)
                { int order=right.Celsius.Value.CompareTo(left.Celsius.Value);return order!=0?order:StringComparer.Ordinal.Compare(left.HardwareId,right.HardwareId); });
                foreach(SensorReading reading in selected) { if(message.Readings.Count==64)break;message.Readings.Add(reading); }
                if(enabled)message.CpuStatus=TemperatureSelection.Highest(message.Readings,true)!=null?"Available":(_cpuReadFailed?"Error":"Unsupported");
                message.GpuStatus=TemperatureSelection.Highest(message.Readings,false)!=null?"Available":(_gpuReadFailed?"Error":"Unsupported");
            }
            catch(Exception error)
            {
                message.Readings.Clear();
                if(enabled)message.CpuStatus=Denied(error)?"AccessDenied":"Error";
                message.GpuStatus=Denied(error)?"AccessDenied":"Error";
                _hardwareError=error.GetType().Name+": "+error.Message;
            }
            string detail="LibreHardwareMonitorLib 0.9.6; driver="+driver.Status;
            if(!String.IsNullOrEmpty(_hardwareError))detail+="; "+_hardwareError;
            message.Detail=detail.Length>1024?detail.Substring(0,1024):detail;
            message.SampleUtc=DateTime.UtcNow;
            return message;
        }
        private static bool Denied(Exception error) { return error is UnauthorizedAccessException||error is SecurityException; }
        private IList<SensorReading> ReadComputer(bool cpuAllowed)
        {
            if(_computer==null||_cpu!=cpuAllowed)
            {
                CloseComputer();_cpu=cpuAllowed;_computer=CreateComputer(cpuAllowed);
                try { _computer.Open(); }
                catch { CloseComputer();throw; }
            }
            var candidates=new List<SensorReading>();
            foreach(IHardware hardware in _computer.Hardware)ReadDevice(hardware,candidates,0);
            return candidates;
        }
        internal void ReadDevice(IHardware hardware,IList<SensorReading> candidates,int depth)
        {
            if(hardware==null||depth>16)return;
            string type=hardware.HardwareType.ToString();
            if(type!="Cpu"&&type!="GpuNvidia"&&type!="GpuAmd"&&type!="GpuIntel")return;
            try
            {
                hardware.Update();
                var current=new List<SensorReading>();
                foreach(ISensor sensor in hardware.Sensors)
                {
                    if(sensor==null||sensor.SensorType!=SensorType.Temperature)continue;
                    float? value=sensor.Value;
                    current.Add(new SensorReading { HardwareId=hardware.Identifier.ToString(),HardwareType=type,DeviceName=hardware.Name,
                        SensorId=sensor.Identifier.ToString(),SensorName=sensor.Name,SensorType="Temperature",Celsius=value.HasValue?(double?)value.Value:null });
                }
                foreach(SensorReading reading in current) { if(candidates.Count>=8192)throw new InvalidOperationException("Too many sensor candidates.");candidates.Add(reading); }
            }
            catch(Exception error)
            {
                if(type=="Cpu")_cpuReadFailed=true;else _gpuReadFailed=true;
                _hardwareError=type+" update failed: "+error.GetType().Name;
                return; // A failed device contributes no previous sensor values.
            }
            foreach(IHardware child in hardware.SubHardware)ReadDevice(child,candidates,depth+1);
        }
        private void CloseComputer()
        {
            Computer computer=_computer;_computer=null;
            if(computer!=null)computer.Close();
        }
        public void Dispose() { Dispose(true);GC.SuppressFinalize(this); }
        private void Dispose(bool disposing)
        { if(!_disposed&&disposing) { _disposed=true;_release(); } }
    }
}
