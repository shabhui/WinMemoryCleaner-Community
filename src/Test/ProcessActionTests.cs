using System;
using System.Collections.Generic;
using NUnit.Framework;
#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class ProcessActionTests
    {
        private sealed class Native : IProcessActionNative
        {
            public int Opens, Closes, Trims, Ends;
            public bool OpenOk=true, IdentityOk=true, ExitOk=true, SafetyOk=true, ActionOk=true, Exited, Critical;
            public byte Protection;
            public long Creation=100;
            public int Error=5;
            public readonly List<IntPtr> Handles=new List<IntPtr>();
            public IntPtr Open(int id,bool terminate,out int error) { Opens++; error=OpenOk?0:Error; return OpenOk?new IntPtr(55):IntPtr.Zero; }
            public bool CreationTime(IntPtr h,out long value,out int error) { Handles.Add(h);value=Creation;error=IdentityOk?0:Error;return IdentityOk; }
            public bool HasExited(IntPtr h,out bool value,out int error) { Handles.Add(h);value=Exited;error=ExitOk?0:Error;return ExitOk; }
            public bool Safety(IntPtr h,out bool critical,out byte protection,out int error) { Handles.Add(h);critical=Critical;protection=Protection;error=SafetyOk?0:Error;return SafetyOk; }
            public bool Trim(IntPtr h,out int error) { Handles.Add(h);Trims++;error=ActionOk?0:Error;return ActionOk; }
            public bool Terminate(IntPtr h,out int error) { Handles.Add(h);Ends++;error=ActionOk?0:Error;return ActionOk; }
            public void Close(IntPtr h) { Handles.Add(h);Closes++; }
        }
        private static ProcessSnapshot Target() { return new ProcessSnapshot { Id=123,Name="verification-child",CreationTime=100 }; }
        [Test]
        public void UnconfirmedTerminationNeverOpensOrActs()
        {
            var n=new Native();var result=new ProcessActionService(n,900).Execute(Target(),true,false);
            Assert.AreEqual(ProcessActionOutcome.NotConfirmed,result.Outcome); Assert.AreEqual(0,n.Opens); Assert.AreEqual(0,n.Ends);
        }
        [Test]
        public void InvalidOrUnknownIdentityIsRejectedBeforeOpening()
        {
            var n=new Native();var s=new ProcessActionService(n,900);
            Assert.AreEqual(ProcessActionOutcome.InvalidTarget,s.Execute(null,false,true).Outcome);
            var target=Target();target.CreationTime=0;
            Assert.AreEqual(ProcessActionOutcome.InvalidTarget,s.Execute(target,true,true).Outcome);Assert.AreEqual(0,n.Opens);
        }
        [TestCase("smss")][TestCase("csrss.exe")][TestCase("wininit")][TestCase("lsass")][TestCase("WinMemoryCleaner.Community")]
        public void CriticalNamesAreRejectedWithoutAnOpen(string name)
        {
            var n=new Native();var target=Target();target.Name=name;
            Assert.AreEqual(ProcessActionOutcome.Protected,new ProcessActionService(n,900).Execute(target,true,true).Outcome);Assert.AreEqual(0,n.Opens);
        }
        [Test]
        public void SelfAndSystemPidsAreRejected()
        {
            var n=new Native();var s=new ProcessActionService(n,900);var target=Target();target.Id=900;
            Assert.AreEqual(ProcessActionOutcome.Protected,s.Execute(target,true,true).Outcome);target.Id=4;
            Assert.AreEqual(ProcessActionOutcome.Protected,s.Execute(target,false,true).Outcome);Assert.AreEqual(0,n.Opens);
        }
        [Test]
        public void PidReuseCannotActAndClosesTheRetainedHandle()
        {
            var n=new Native { Creation=101 };
            Assert.AreEqual(ProcessActionOutcome.IdentityChanged,new ProcessActionService(n,900).Execute(Target(),true,true).Outcome);
            Assert.AreEqual(0,n.Ends);Assert.AreEqual(1,n.Closes);
        }
        [Test]
        public void ExitedProcessCannotBeActedOn()
        {
            var n=new Native { Exited=true };
            Assert.AreEqual(ProcessActionOutcome.Exited,new ProcessActionService(n,900).Execute(Target(),false,true).Outcome);
            Assert.AreEqual(0,n.Trims);Assert.AreEqual(1,n.Closes);
        }
        [Test]
        public void DeniedOpenAndIdentityAreTypedFailures()
        {
            var n=new Native { OpenOk=false };var s=new ProcessActionService(n,900);
            Assert.AreEqual(ProcessActionOutcome.AccessDenied,s.Execute(Target(),true,true).Outcome);Assert.AreEqual(0,n.Closes);
            n.OpenOk=true;n.IdentityOk=false;
            Assert.AreEqual(ProcessActionOutcome.AccessDenied,s.Execute(Target(),true,true).Outcome);Assert.AreEqual(1,n.Closes);Assert.AreEqual(0,n.Ends);
        }
        [Test]
        public void CriticalProtectedAndUnknownSafetyFailClosed()
        {
            var n=new Native { Critical=true };var s=new ProcessActionService(n,900);
            Assert.AreEqual(ProcessActionOutcome.Protected,s.Execute(Target(),true,true).Outcome);
            n.Critical=false;n.Protection=1;
            Assert.AreEqual(ProcessActionOutcome.Protected,s.Execute(Target(),true,true).Outcome);
            n.Protection=0;n.SafetyOk=false;
            Assert.AreEqual(ProcessActionOutcome.Protected,s.Execute(Target(),true,true).Outcome);
            Assert.AreEqual(0,n.Ends);Assert.AreEqual(3,n.Closes);
        }
        [TestCase(false)][TestCase(true)]
        public void ValidationAndActionUseOneHandleAndExactlyOneAction(bool terminate)
        {
            var n=new Native();var result=new ProcessActionService(n,900).Execute(Target(),terminate,true);
            Assert.IsTrue(result.Success);Assert.AreEqual(terminate?ProcessActionOutcome.Terminated:ProcessActionOutcome.Trimmed,result.Outcome);
            Assert.AreEqual(1,n.Opens);Assert.AreEqual(1,n.Closes);Assert.AreEqual(terminate?0:1,n.Trims);Assert.AreEqual(terminate?1:0,n.Ends);
            foreach(var h in n.Handles)Assert.AreEqual(new IntPtr(55),h);
        }
        [Test]
        public void ActionPermissionFailureDoesNotClaimSuccess()
        {
            var n=new Native { ActionOk=false };var result=new ProcessActionService(n,900).Execute(Target(),false,true);
            Assert.AreEqual(ProcessActionOutcome.AccessDenied,result.Outcome);Assert.IsFalse(result.Success);Assert.AreEqual(1,n.Closes);
        }
    }
}
#pragma warning restore 1591
