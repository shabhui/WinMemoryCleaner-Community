using NUnit.Framework;
using System;
using System.Reflection;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class MonitoringOptionsTests
    {
        private static Type OptionsType()
        {
            var type = Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.MonitoringOptions");
            Assert.IsNotNull(type, "Monitoring options must have safe independent defaults and persistence");
            return type;
        }
        private static object Read(string json)
        {
            return OptionsType().GetMethod("Deserialize").Invoke(null, new object[] { json });
        }
        private static object Property(object options, string name)
        {
            var property = options.GetType().GetProperty(name);
            Assert.IsNotNull(property, "Monitoring options must persist " + name);
            return property.GetValue(options, null);
        }
        [Test]
        public void FirstRun_DefaultsToTwoSecondsAndDisabledClickThroughOverlay()
        {
            var options = Read(null);
            Assert.AreEqual(2, Property(options, "RefreshSeconds"));
            Assert.AreEqual(false, Property(options, "OverlayEnabled"));
            Assert.AreEqual(true, Property(options, "ClickThrough"));
            Assert.AreEqual(7, Property(options, "Items"));
        }
        [TestCase("invalid-json")]
        [TestCase("{\"RefreshSeconds\":0,\"Corner\":100,\"Items\":128}")]
        public void CorruptOptions_KeepOverlayVisibleAndRefreshBounded(string json)
        {
            var options = Read(json);
            Assert.AreEqual(2, Property(options, "RefreshSeconds"));
            Assert.That((int)Property(options, "Corner"), Is.InRange(0, 4));
            Assert.That((int)Property(options, "Items"), Is.InRange(1, 31));
        }
        [Test]
        public void Options_RoundTripPreservesUserPositionAndDisplayItems()
        {
            var type = OptionsType(); var options = Activator.CreateInstance(type);
            type.GetProperty("RefreshSeconds").SetValue(options, 5, null);
            type.GetProperty("OverlayEnabled").SetValue(options, true, null);
            type.GetProperty("Screen").SetValue(options, "DISPLAY2", null);
            type.GetProperty("Corner").SetValue(options, 4, null);
            type.GetProperty("X").SetValue(options, -500.0, null);
            type.GetProperty("Y").SetValue(options, 75.0, null);
            type.GetProperty("Items").SetValue(options, 25, null);
            var json = (string)type.GetMethod("Serialize").Invoke(options, null);
            var loaded = Read(json);
            foreach (var name in new[] { "RefreshSeconds", "OverlayEnabled", "Screen", "Corner", "X", "Y", "Items" })
                Assert.AreEqual(Property(options, name), Property(loaded, name), name);
        }
        [Test]
        public void LegacyOptions_DefaultToHorizontalWithoutLosingPositionOrItems()
        {
            var options = Read("{\"OverlayEnabled\":true,\"Corner\":4,\"X\":-500,\"Y\":75,\"Items\":31}");
            Assert.AreEqual(0, Property(options, "Layout"));
            Assert.AreEqual(true, Property(options, "OverlayEnabled"));
            Assert.AreEqual(4, Property(options, "Corner"));
            Assert.AreEqual(-500.0, Property(options, "X"));
            Assert.AreEqual(75.0, Property(options, "Y"));
            Assert.AreEqual(31, Property(options, "Items"));
        }
        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(100)]
        public void InvalidLayout_RecoversToHorizontal(int layout)
        {
            Assert.AreEqual(0, Property(Read("{\"Layout\":" + layout + "}"), "Layout"));
        }
        [Test]
        public void VerticalLayout_SurvivesSerialization()
        {
            var options = Read("{\"Layout\":1,\"Items\":31}");
            Assert.AreEqual(1, Property(options, "Layout"));
            var json = (string)options.GetType().GetMethod("Serialize").Invoke(options, null);
            Assert.AreEqual(1, Property(Read(json), "Layout"));
            Assert.AreEqual(31, Property(Read(json), "Items"));
        }
    }
}
#pragma warning restore 1591
