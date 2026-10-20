using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using NUnit.Framework;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    /// <summary>Verifies the payload that is really embedded in this build, without executing it or touching hardware.</summary>
    [TestFixture]
    public sealed class SensorEmbeddedPayloadTests
    {
        private const string ManifestResource = "WinMemoryCleaner.Sensors.Payload.manifest.json";
        private static readonly string[] Required =
        {
            "WinMemoryCleaner.Sensors.exe", "WinMemoryCleaner.Sensors.exe.config", "LibreHardwareMonitorLib.dll", "PawnIO_setup.exe",
            "sensors-dependencies.lock.json", "sensors-build-info.json", "THIRD-PARTY-NOTICES.md",
            "MPL-2.0.txt", "HidSharp-LICENSE.txt", "DotNET-MIT.txt", "PawnIO-COPYING.txt", "PawnIO-LGPL-2.1.txt", "PawnIO-Linking-Exception.txt"
        };
        private string _root;
        [SetUp]
        public void Setup()
        { _root = Path.Combine(Environment.GetEnvironmentVariable("PI_SCRATCH_DIR") ?? TestContext.CurrentContext.WorkDirectory, "embedded-payload-" + Guid.NewGuid().ToString("N")); }
        [TearDown]
        public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        private static string Hash(byte[] data)
        {
            using (var hash = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (byte value in hash.ComputeHash(data)) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }
        private static Dictionary<string, object> Object(object value) { return (Dictionary<string, object>)value; }
        [Test]
        public void EmbeddedPayloadMatchesItsManifestLockAndBuildInfo()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            byte[] manifest;
            using (Stream stream = assembly.GetManifestResourceStream(ManifestResource))
            {
                Assert.IsNotNull(stream, "This build has no embedded sensor payload; run tools\\build-sensors.ps1 before building.");
                using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); manifest = buffer.ToArray(); }
            }
            Dictionary<string, object> parsed = Object(new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(manifest)));
            Assert.AreEqual(3, parsed.Count);Assert.AreEqual(SensorProtocol.PayloadVersion, parsed["version"]);Assert.AreEqual(1, parsed["protocol"]);
            var entries = (object[])parsed["files"];
            Assert.Greater(entries.Length, 20);
            var expected = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (object entry in entries)
            {
                Dictionary<string, object> file = Object(entry);
                string path = (string)file["path"];
                Assert.AreEqual("WinMemoryCleaner.Sensors.Payload." + path + ".gz", file["resource"], path);
                Assert.IsFalse(expected.ContainsKey(path), path);
                expected.Add(path, file);
            }
            foreach (string required in Required) Assert.IsTrue(expected.ContainsKey(required), "The real payload is missing " + required);
            var payload = new SensorPayload(_root, manifest, assembly.GetManifestResourceStream);
            using (SensorPayloadLease lease = payload.Prepare())
            {
                Assert.IsNotNull(lease);
                Assert.AreEqual(0, Directory.GetDirectories(lease.Directory).Length);
                foreach (string required in Required) Assert.IsTrue(File.Exists(Path.Combine(lease.Directory, required)), required);
                foreach (var pair in expected)
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(lease.Directory, pair.Key));
                    Assert.AreEqual(Convert.ToInt64(Object(pair.Value)["length"], CultureInfo.InvariantCulture), bytes.Length, pair.Key);
                    Assert.AreEqual((string)Object(pair.Value)["sha256"], Hash(bytes), pair.Key);
                }
                Dictionary<string, object> lockFile = Object(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(lease.Directory, "sensors-dependencies.lock.json"))));
                Assert.AreEqual(SensorProtocol.PayloadVersion, lockFile["payload"]);
                Assert.AreEqual(12, ((object[])lockFile["assets"]).Length);
                foreach (object asset in (object[])lockFile["assets"])
                {
                    Dictionary<string, object> pinned = Object(asset);
                    string name = (string)pinned["name"];
                    Assert.IsTrue(expected.ContainsKey(name), name);
                    Assert.AreEqual((string)pinned["sha256"], Object(expected[name])["sha256"], name);
                }
                Dictionary<string, object> installer = Object(lockFile["installer"]);
                Assert.AreEqual((string)installer["sha256"], Object(expected[(string)installer["name"]])["sha256"], "PawnIO_setup.exe");
                Assert.AreEqual(3225016, Convert.ToInt64(Object(expected["PawnIO_setup.exe"])["length"], CultureInfo.InvariantCulture));
                Dictionary<string, object> buildInfo = Object(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(lease.Directory, "sensors-build-info.json"))));
                Assert.AreEqual(SensorProtocol.PayloadVersion, buildInfo["payloadVersion"]);
                Assert.AreEqual(9, ((object[])buildInfo["analyzers"]).Length);
                Assert.Greater(((object[])buildInfo["sources"]).Length, 10);
                Dictionary<string, object> ruleset = Object(buildInfo["ruleset"]);
                Assert.AreEqual(64, ((string)ruleset["sha256"]).Length);
                AssemblyName backend = AssemblyName.GetAssemblyName(lease.Executable);
                Assert.AreEqual("WinMemoryCleaner.Sensors", backend.Name);
                Assert.GreaterOrEqual(backend.Version.Major, 1);
            }
        }
        [Test]
        public void PayloadResourcesArePinnedToTheManifestNames()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            var names = new List<string>(assembly.GetManifestResourceNames());
            Assert.Contains(ManifestResource, names);
            int count = 0;
            foreach (string name in names) if (name.StartsWith("WinMemoryCleaner.Sensors.Payload.", StringComparison.Ordinal)) count++;
            Assert.AreEqual(25, count);
        }
    }
}
#pragma warning restore 1591
