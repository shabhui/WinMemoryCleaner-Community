using NUnit.Framework;
using System;
using System.Reflection;
using System.Windows;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    // These behavioral tests intentionally run against the real production policy.
    // An absent type is a failed feature assertion, not a compile/import failure.
    [TestFixture]
    public sealed class MonitoringPresentationTests
    {
        private static object Invoke(string typeName, string method, params object[] args)
        {
            var type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner." + typeName);
            Assert.IsNotNull(type, typeName + " must implement the approved lightweight monitoring policy");
            var info = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(info, method + " must be available");
            return info.Invoke(null, args);
        }

        private static bool Flag(object demand, string name)
        {
            return (bool)demand.GetType().GetProperty(name).GetValue(demand, null);
        }

        [Test]
        public void HiddenWindowAndDisabledOverlay_DoNotRequestDetailedSampling()
        {
            var demand = Invoke("MonitoringPolicy", "GetDemand", false, true, false, true);
            Assert.IsFalse(Flag(demand, "CpuGpu"));
            Assert.IsFalse(Flag(demand, "Processes"));
            Assert.IsFalse(Flag(demand, "Temperatures"));
        }

        [Test]
        public void OverlayRequestsMetrics_ButNeverEnumeratesProcesses()
        {
            var demand = Invoke("MonitoringPolicy", "GetDemand", false, true, true, true);
            Assert.IsTrue(Flag(demand, "CpuGpu"));
            Assert.IsFalse(Flag(demand, "Processes"));
            Assert.IsTrue(Flag(demand, "Temperatures"));
        }

        [Test]
        public void ProcessPageRequestsProcesses_OnlyWhenVisible()
        {
            var demand = Invoke("MonitoringPolicy", "GetDemand", true, true, false, false);
            Assert.IsTrue(Flag(demand, "Processes"));
            Assert.IsFalse(Flag(demand, "Temperatures"));
        }

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(5, 5)]
        [TestCase(0, 2)]
        [TestCase(-100, 2)]
        [TestCase(3, 2)]
        [TestCase(999999, 2)]
        public void RefreshIntervals_AcceptOnlyApprovedValues(int input, int expected)
        {
            Assert.AreEqual(expected, Invoke("MonitoringPolicy", "NormalizeRefresh", input));
        }

        [Test]
        public void ChangedGenerationOrDisposedOwner_RejectsInFlightSnapshot()
        {
            Assert.AreEqual(false, Invoke("MonitoringPolicy", "IsCurrent", 4, 5, false));
            Assert.AreEqual(false, Invoke("MonitoringPolicy", "IsCurrent", 4, 4, true));
            Assert.AreEqual(true, Invoke("MonitoringPolicy", "IsCurrent", 4, 4, false));
        }

        [Test]
        public void ProcessSearch_MatchesNamesOrPid_CaseInsensitive()
        {
            Assert.AreEqual(true, Invoke("MonitoringPolicy", "MatchesProcess", "Game.exe", 4321, " GAME "));
            Assert.AreEqual(true, Invoke("MonitoringPolicy", "MatchesProcess", "Game.exe", 4321, "4321"));
            Assert.AreEqual(true, Invoke("MonitoringPolicy", "MatchesProcess", "Game.exe", 4321, ""));
            Assert.AreEqual(false, Invoke("MonitoringPolicy", "MatchesProcess", "Game.exe", 4321, "9999"));
        }

        [Test]
        public void OverlayPosition_ClampsRemovedScreenCoordinatesToVisibleArea()
        {
            var position = (Point)Invoke("OverlayPlacement", "Place", new Rect(0, 0, 1920, 1080), new Size(220, 100), 4, 8000.0, -500.0);
            Assert.AreEqual(1700.0, position.X);
            Assert.AreEqual(0.0, position.Y);
        }

        [Test]
        public void OverlayPosition_HandlesNegativeMonitorAndAllCorners()
        {
            var area = new Rect(-1920, 0, 1920, 1080);
            var size = new Size(220, 100);
            var tl = (Point)Invoke("OverlayPlacement", "Place", area, size, 0, double.NaN, double.NaN);
            var br = (Point)Invoke("OverlayPlacement", "Place", area, size, 3, double.NaN, double.NaN);
            Assert.AreEqual(new Point(-1904, 16), tl);
            Assert.AreEqual(new Point(-236, 964), br);
        }

        [Test]
        public void OverlayPosition_InvalidCustomValuesUseSafeTopLeft()
        {
            var point = (Point)Invoke("OverlayPlacement", "Place", new Rect(0, 0, 800, 600), new Size(220, 100), 4, double.NaN, double.PositiveInfinity);
            Assert.AreEqual(new Point(16, 16), point);
        }
    }
}
#pragma warning restore 1591
