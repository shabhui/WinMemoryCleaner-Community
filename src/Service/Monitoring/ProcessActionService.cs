using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public enum ProcessActionOutcome { Trimmed, Terminated, NotConfirmed, InvalidTarget, IdentityChanged, Exited, Protected, AccessDenied, Failed }
    public sealed class ProcessActionResult
    {
        public ProcessActionOutcome Outcome { get; private set; }
        public bool Success { get { return Outcome==ProcessActionOutcome.Trimmed||Outcome==ProcessActionOutcome.Terminated; } }
        public string Detail { get; private set; }
        public ProcessActionResult(ProcessActionOutcome outcome,string detail) { Outcome=outcome;Detail=detail??string.Empty; }
    }
    public interface IProcessActionNative
    {
        IntPtr Open(int id,bool terminate,out int errorCode);
        bool CreationTime(IntPtr handle,out long value,out int errorCode);
        bool HasExited(IntPtr handle,out bool value,out int errorCode);
        bool Safety(IntPtr handle,out bool critical,out byte protection,out int errorCode);
        bool Trim(IntPtr handle,out int errorCode);
        bool Terminate(IntPtr handle,out int errorCode);
        void Close(IntPtr handle);
    }
    public sealed class ProcessActionService
    {
        private readonly IProcessActionNative _native;
        private readonly int _selfId;
        public ProcessActionService() : this(new ProcessActionNative(),(int)MonitoringNativeMethods.GetCurrentProcessId()) { }
        public ProcessActionService(IProcessActionNative native,int selfId)
        {
            if(native==null)throw new ArgumentNullException("native");_native=native;_selfId=selfId;
        }
        private static bool ProtectedName(string name)
        {
            name=Path.GetFileNameWithoutExtension(name??string.Empty);
            foreach(string critical in new[]{"system","idle","smss","csrss","wininit","winlogon","services","lsass","lsaiso","secure system","registry","WinMemoryCleaner.Community"})
                if(string.Equals(name,critical,StringComparison.OrdinalIgnoreCase))return true;
            // Process snapshots may already be extensionless (Community is part of the basename).
            return string.Equals(name,"WinMemoryCleaner",StringComparison.OrdinalIgnoreCase);
        }
        private static ProcessActionResult Failure(int error)
        {
            return new ProcessActionResult(error==5?ProcessActionOutcome.AccessDenied:error==87||error==1168?ProcessActionOutcome.Exited:ProcessActionOutcome.Failed,new Win32Exception(error).Message+" ("+error+")");
        }
        public ProcessActionResult Execute(ProcessSnapshot target,bool terminate,bool confirmed)
        {
            if(terminate&&!confirmed)return new ProcessActionResult(ProcessActionOutcome.NotConfirmed,"No action taken");
            if(target==null||target.Id<0||target.CreationTime<=0)return new ProcessActionResult(ProcessActionOutcome.InvalidTarget,"Missing process identity");
            if(target.Id<=4||target.Id==_selfId||ProtectedName(target.Name))return new ProcessActionResult(ProcessActionOutcome.Protected,"System, critical or tool process");
            IntPtr handle=IntPtr.Zero;
            try
            {
                int error;handle=_native.Open(target.Id,terminate,out error);if(handle==IntPtr.Zero)return Failure(error);
                bool exited;if(!_native.HasExited(handle,out exited,out error))return Failure(error);
                if(exited)return new ProcessActionResult(ProcessActionOutcome.Exited,"Process already exited");
                long creation;if(!_native.CreationTime(handle,out creation,out error))return Failure(error);
                if(creation!=target.CreationTime)return new ProcessActionResult(ProcessActionOutcome.IdentityChanged,"PID creation time no longer matches selection");
                bool critical;byte protection;
                if(!_native.Safety(handle,out critical,out protection,out error)||critical||protection!=0)return new ProcessActionResult(ProcessActionOutcome.Protected,"Critical/protection check denied or uncertain ("+error+")");
                if(!_native.HasExited(handle,out exited,out error))return Failure(error);
                if(exited)return new ProcessActionResult(ProcessActionOutcome.Exited,"Process exited before action");
                bool success=terminate?_native.Terminate(handle,out error):_native.Trim(handle,out error);
                return success?new ProcessActionResult(terminate?ProcessActionOutcome.Terminated:ProcessActionOutcome.Trimmed,terminate?"Termination requested":"Working set released; pages may be faulted back in"):Failure(error);
            }
            catch(Exception e) { return new ProcessActionResult(ProcessActionOutcome.Failed,e.Message); }
            finally { if(handle!=IntPtr.Zero)_native.Close(handle); }
        }
    }
    internal sealed class ProcessActionNative : IProcessActionNative
    {
        public IntPtr Open(int id,bool terminate,out int errorCode)
        {
            var handle=MonitoringNativeMethods.OpenProcess(0x1000|0x100000|(terminate?1u:0x100u),false,id);
            errorCode=handle==IntPtr.Zero?Marshal.GetLastWin32Error():0;return handle;
        }
        public bool CreationTime(IntPtr h,out long value,out int errorCode)
        {
            long exit,kernel,user;bool ok=MonitoringNativeMethods.GetProcessTimes(h,out value,out exit,out kernel,out user);errorCode=ok?0:Marshal.GetLastWin32Error();return ok;
        }
        public bool HasExited(IntPtr h,out bool value,out int errorCode)
        {
            uint result=MonitoringNativeMethods.WaitForSingleObject(h,0);value=result==0;
            errorCode=result==uint.MaxValue?Marshal.GetLastWin32Error():(result==0||result==258)?0:unchecked((int)result);
            return result==0||result==258;
        }
        public bool Safety(IntPtr h,out bool critical,out byte protection,out int errorCode)
        {
            critical=true;protection=255;errorCode=0;
            try
            {
                if(!MonitoringNativeMethods.IsProcessCritical(h,out critical)) { errorCode=Marshal.GetLastWin32Error();return false; }
            }
            catch(EntryPointNotFoundException)
            {
                uint value;int returned;int status=MonitoringNativeMethods.ProcessCritical(h,29,out value,4,out returned);if(status!=0){errorCode=status;return false;}critical=value!=0;
            }
            int needed;int protectionStatus=MonitoringNativeMethods.ProcessProtection(h,61,out protection,1,out needed);
            if(protectionStatus!=0){errorCode=protectionStatus;return false;}return true;
        }
        public bool Trim(IntPtr h,out int errorCode) { bool ok=MonitoringNativeMethods.EmptyWorkingSet(h);errorCode=ok?0:Marshal.GetLastWin32Error();return ok; }
        public bool Terminate(IntPtr h,out int errorCode) { bool ok=MonitoringNativeMethods.TerminateProcess(h,0);errorCode=ok?0:Marshal.GetLastWin32Error();return ok; }
        public void Close(IntPtr h) { MonitoringNativeMethods.CloseHandle(h); }
    }
}
#pragma warning restore 1591
