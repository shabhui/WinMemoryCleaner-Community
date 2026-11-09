using System;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class SensorDriverNoticeTests
    {
        [Test]
        public void ReadyDriverNeedsNoInstallationPrompt()
        { Assert.IsNull(SensorDriverNotice.FromState(new SensorDriverState(true, true, new Version(2, 2, 0), "Ready"))); }

        [Test]
        public void OnlyConfirmedAbsenceOffersInstallation()
        {
            var notice = SensorDriverNotice.FromState(new SensorDriverState(false, false, null, "NotInstalled"));
            Assert.IsTrue(notice.OfferInstallation); Assert.AreEqual("TemperatureDriverNeeded", notice.MessageKey);
            Assert.IsFalse(SensorDriverNotice.FromState(new SensorDriverState(false, true, null, "NotInstalled")).OfferInstallation);
        }

        [TestCase("AccessDenied", "TemperatureDriverAdministrator")]
        [TestCase("DriverNotReady", "TemperatureDriverNotReady")]
        [TestCase("UnverifiedDriver", "TemperatureDriverUnverified")]
        [TestCase("KeepExistingDriver", "TemperatureDriverUnverified")]
        [TestCase("InspectionFailed", "TemperatureDriverInspectionFailed")]
        [TestCase("unexpected", "TemperatureDriverInspectionFailed")]
        [TestCase("Ready", "TemperatureDriverInspectionFailed")]
        public void FailureStatesCannotBeMistakenForMissingDrivers(string status, string expected)
        {
            var notice = SensorDriverNotice.FromState(new SensorDriverState(false, true, null, status));
            Assert.IsFalse(notice.OfferInstallation); Assert.AreEqual(expected, notice.MessageKey);
        }

        [Test]
        public void NullInspectionNeverAuthorizesInstallation()
        {
            var notice = SensorDriverNotice.FromState(null);
            Assert.IsFalse(notice.OfferInstallation); Assert.AreEqual("TemperatureDriverInspectionFailed", notice.MessageKey);
        }
    }
}
#pragma warning restore 1591
