using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorInstallationTests
    {
        private string _root;
        private int _installCalls, _prepareCalls, _completed;
        private const string PinnedHash = "f92de04e5a02256e86ffa1b4252fe669282198b7329227f40e51bca9267d133d";
        [SetUp]
        public void Setup()
        { _root=Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR")??TestContext.CurrentContext.WorkDirectory,"installer-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_root);_installCalls=0;_prepareCalls=0;_completed=0; }
        [TearDown]
        public void Cleanup() { Directory.Delete(_root,true); }
        private static SensorDriverState Missing() { return new SensorDriverState(false,false,null,"NotInstalled"); }
        private static SensorDriverState Ready() { return new SensorDriverState(true,true,new Version(2,1,0),"Ready"); }
        private SensorDriverInstaller Installer(Func<SensorDriverState> inspect,Func<string,int> execute,bool validSignature)
        {
            return new SensorDriverInstaller(inspect,delegate { Interlocked.Increment(ref _prepareCalls);return new SensorPayloadLease(_root); },
                delegate(string path) { Assert.AreEqual(Path.Combine(_root,"PawnIO_setup.exe"),path);return validSignature; },
                delegate(string path) { Assert.AreEqual(Path.Combine(_root,"PawnIO_setup.exe"),path);Interlocked.Increment(ref _installCalls);return execute(path); },
                delegate { Interlocked.Increment(ref _completed); });
        }
        private static void Finish(SensorDriverInstaller installer)
        { DateTime limit=DateTime.UtcNow.AddSeconds(10);while(installer.IsInstalling&&DateTime.UtcNow<limit)Thread.Sleep(5);Assert.IsFalse(installer.IsInstalling,"Installer worker did not finish."); }
        [Test]
        public void UnconfirmedOrCancelledUserActionNeverPreparesOrExecutes()
        {
            var installer=Installer(Missing,delegate { return 0; },true);
            Assert.IsFalse(installer.Begin(false));Assert.IsFalse(installer.IsInstalling);Assert.AreEqual(0,_prepareCalls);Assert.AreEqual(0,_installCalls);
        }
        [Test]
        public void AlreadyReadyDriverNeedsNoInstaller()
        {
            var installer=Installer(Ready,delegate { return 0; },true);Assert.IsTrue(installer.Begin(true));Finish(installer);
            Assert.AreEqual(0,_prepareCalls);Assert.AreEqual(0,_installCalls);Assert.AreEqual("Ready",installer.Status);
        }
        [TestCase(2,2,0)]
        [TestCase(0,0,0)]
        public void ExistingNewerOrUnidentifiedSharedDriverIsNeverOverwritten(int major,int minor,int patch)
        {
            Version version=major==0?null:new Version(major,minor,patch);
            var installer=Installer(delegate { return new SensorDriverState(false,true,version,"UnverifiedDriver"); },delegate { return 0; },true);
            Assert.IsTrue(installer.Begin(true));Finish(installer);Assert.AreEqual(0,_installCalls);Assert.AreEqual(0,_prepareCalls);Assert.AreEqual("KeepExistingDriver",installer.Status);
        }
        [Test]
        public void FailureToConfirmServiceAbsenceCannotAuthorizeInstallation()
        {
            var installer=Installer(delegate { return new SensorDriverState(false,false,null,"InspectionFailed"); },delegate { return 0; },true);
            Assert.IsTrue(installer.Begin(true));Finish(installer);Assert.AreEqual(0,_installCalls);Assert.AreEqual("InspectionFailed",installer.Status);
        }
        [Test]
        public void InvalidInstallerSignatureBlocksExecution()
        {
            var installer=Installer(Missing,delegate { return 0; },false);Assert.IsTrue(installer.Begin(true));Finish(installer);
            Assert.AreEqual(0,_installCalls);Assert.AreEqual(1,_prepareCalls);Assert.AreEqual("SignatureInvalid",installer.Status);
        }
        [Test]
        public void ServiceAppearingDuringPreparationIsPreserved()
        {
            int inspections=0;
            var installer=Installer(delegate { return Interlocked.Increment(ref inspections)==1?Missing():new SensorDriverState(false,true,null,"UnverifiedDriver"); },delegate { return 0; },true);
            Assert.IsTrue(installer.Begin(true));Finish(installer);Assert.AreEqual(0,_installCalls);Assert.AreEqual("KeepExistingDriver",installer.Status);
        }
        [Test]
        public void ExitZeroWithoutVerifiedDeviceCannotClaimReady()
        {
            var installer=Installer(Missing,delegate { return 0; },true);Assert.IsTrue(installer.Begin(true));Finish(installer);
            Assert.AreEqual(1,_installCalls);Assert.AreEqual(1,_completed);Assert.AreEqual("InstalledNotReady",installer.Status);
        }
        [Test]
        public void SuccessfulInstallReinspectsAndNotifiesBackend()
        {
            int inspections=0;
            var installer=Installer(delegate { return Interlocked.Increment(ref inspections)<3?Missing():Ready(); },delegate { return 0; },true);
            Assert.IsTrue(installer.Begin(true));Finish(installer);Assert.AreEqual(1,_installCalls);Assert.AreEqual(1,_completed);Assert.AreEqual("Ready",installer.Status);Assert.GreaterOrEqual(inspections,3);
        }
        [TestCase(3010,"RestartRequired")]
        [TestCase(1602,"Cancelled")]
        [TestCase(5,"InstallFailed")]
        public void ExitCodeReportsRestartCancelOrFailureWithoutRestarting(int code,string expected)
        {
            var installer=Installer(Missing,delegate { return code; },true);Assert.IsTrue(installer.Begin(true));Finish(installer);
            Assert.AreEqual(1,_installCalls);Assert.AreEqual(1,_completed);StringAssert.StartsWith(expected,installer.Status);
        }
        [Test]
        public void UacCancellationIsNotReportedAsSuccess()
        {
            var installer=Installer(Missing,delegate { throw new Win32Exception(1223); },true);Assert.IsTrue(installer.Begin(true));Finish(installer);
            Assert.AreEqual("Cancelled",installer.Status);
        }
        [Test]
        public void DuplicateRequestsExecuteAtMostOneInstaller()
        {
            using(var entered=new ManualResetEvent(false))
            using(var release=new ManualResetEvent(false))
            {
                var installer=Installer(Missing,delegate { entered.Set();if(!release.WaitOne(5000))throw new TimeoutException();return 0; },true);
                Assert.IsTrue(installer.Begin(true));Assert.IsTrue(entered.WaitOne(2000));
                try { Assert.IsFalse(installer.Begin(true));Assert.AreEqual(1,_installCalls); }
                finally { release.Set();Finish(installer); }
            }
        }
        [Test]
        public void OnlyPinnedSignedReadyDeviceEnablesCpu()
        { var state=SensorDriverTrust.Evaluate(true,new Version(2,1,0),PinnedHash,true,true);Assert.IsTrue(state.CpuAllowed);Assert.IsTrue(state.ServicePresent);Assert.AreEqual("Ready",state.Status); }
        [TestCase(false,true,true)]
        [TestCase(true,false,true)]
        [TestCase(true,true,false)]
        public void MissingServiceSignatureOrDeviceCannotEnableCpu(bool present,bool signature,bool ready)
        { Assert.IsFalse(SensorDriverTrust.Evaluate(present,new Version(2,1,0),PinnedHash,signature,ready).CpuAllowed); }
        [Test]
        public void ValidSignatureOnUnrecognizedImageIsNotSufficient()
        { Assert.IsFalse(SensorDriverTrust.Evaluate(true,new Version(2,1,0),new string('0',64),true,true).CpuAllowed); }
        [Test]
        public void NewerDeviceVersionCannotAssertPinnedDriverMode()
        { var state=SensorDriverTrust.Evaluate(true,new Version(2,2,0),PinnedHash,true,true);Assert.IsFalse(state.CpuAllowed);Assert.IsTrue(state.ServicePresent);Assert.AreEqual(new Version(2,2,0),state.Version); }
        [Test]
        public void ArbitraryExeCannotBecomeOfficialInstaller()
        { string path=Path.Combine(_root,"not-installer.exe");File.WriteAllText(path,"not executable data");Assert.IsFalse(SensorDriverTrust.VerifyInstaller(path)); }
        [TestCase((string)null)][TestCase("")][TestCase("C:\\bad:path.exe")]
        public void UnsafeInstallerPathIsRejectedWithoutEscapingVerification(string path)
        { Assert.IsFalse(SensorDriverTrust.VerifyInstaller(path)); }
    }
}
#pragma warning restore 1591
