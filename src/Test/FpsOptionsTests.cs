using System;
using System.Reflection;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class FpsOptionsTests
    {
        [Test]
        public void FpsSelectionRoundTripsWithoutResettingExistingDisplayItems()
        {
            var options=MonitoringOptions.Deserialize("{\"Items\":39,\"FpsTargetMode\":1,\"RefreshSeconds\":5}");
            Assert.AreEqual(39,options.Items,"An opt-in FPS item must survive normalization alongside CPU/GPU/memory.");
            var mode=typeof(MonitoringOptions).GetProperty("FpsTargetMode");
            Assert.IsNotNull(mode,"The user's foreground/manual choice must be saved independently of a transient process PID.");
            Assert.AreEqual(1,mode.GetValue(options,null));
            var restored=MonitoringOptions.Deserialize(options.Serialize());
            Assert.AreEqual(39,restored.Items);Assert.AreEqual(5,restored.RefreshSeconds);
            Assert.AreEqual(1,mode.GetValue(restored,null));
        }

        [Test]
        public void FpsIsOptInAndCanBeTheOnlySelectedOverlayItem()
        {
            var defaults=MonitoringOptions.Deserialize(null);
            Assert.AreEqual(0,defaults.Items&32,"Existing settings must not unexpectedly start game tracing.");
            var fpsOnly=MonitoringOptions.Deserialize("{\"Items\":32}");
            Assert.AreEqual(32,fpsOnly.Items,"Selecting only FPS must not silently enable memory instead.");
            var mixed=MonitoringOptions.Deserialize("{\"Items\":255}");
            Assert.AreEqual(63,mixed.Items,"Unsupported future bits remain ignored; existing items plus FPS are retained.");
        }

        [TestCase(-1)]
        [TestCase(8)]
        public void InvalidFpsSelectionFallsBackWithoutPersistingAProcessIdentity(int value)
        {
            var options=new MonitoringOptions();
            var mode=typeof(MonitoringOptions).GetProperty("FpsTargetMode");
            Assert.IsNotNull(mode,"FPS source selection must be a saved, normalized option.");
            mode.SetValue(options,value,null);options.Normalize();
            Assert.AreEqual(0,mode.GetValue(options,null));
            var data=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(options.Serialize()) as System.Collections.Generic.Dictionary<string,object>;
            Assert.IsNotNull(data);Assert.IsFalse(data.ContainsKey("FpsTargetId"));Assert.IsFalse(data.ContainsKey("FpsCreationTime"));
        }
    }
}
#pragma warning restore 1591
