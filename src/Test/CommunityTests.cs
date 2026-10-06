using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using System.Xml;
using NUnit.Framework;

#pragma warning disable 1591

namespace WinMemoryCleaner.CommunityTests
{
    [TestFixture]
    [CLSCompliant(false)] // NUnit's params object[] test-case attributes are not CLS-compliant.
    public sealed class CommunityRegressionTests
    {
        [Test]
        public void Identity_DoesNotReuseUpstreamSettingsOrExecutableName()
        {
            Assert.AreEqual("WinMemoryCleaner.Community", Constants.App.Name);
            Assert.AreEqual(Constants.App.Name, typeof(App).Assembly.GetName().Name);
            Assert.AreEqual(@"SOFTWARE\WinMemoryCleaner.Community", Constants.App.Registry.Key.Settings);
            Assert.AreEqual(Constants.App.Registry.Key.Settings + @"\ProcessExclusionList", Constants.App.Registry.Key.ProcessExclusionList);
            Assert.AreNotEqual("C7F29A45-8B3E-4D2F-9A1C-5E7B2D4F8C6A", Constants.App.Id);
            Assert.AreNotEqual("Windows Memory Cleaner.lnk", Constants.App.Shortcut);
            Assert.AreEqual("Igor Mundstein", Constants.App.Author.Name);
            Assert.AreEqual("GPL-3.0", Constants.App.License);
        }

        [Test]
        public void ReleaseChannel_IsUnconfiguredAndUsesLocalDocumentation()
        {
            Assert.IsFalse(Constants.App.ReleaseChannelConfigured);
            Assert.IsFalse(Helper.IsAutoUpdateSupported);
            Assert.IsNull(Constants.App.Repository.AssemblyInfoUri);
            Assert.IsNull(Constants.App.Repository.LatestExeUri);
            Assert.IsNull(Constants.App.Repository.LatestExeHashUri);
            Assert.IsFalse(Updater.IsVerifiableUpdateChannel(Constants.App.Repository.LatestExeHashUri));
            Assert.IsTrue(Constants.App.Repository.AboutUri.IsFile);
            Assert.AreEqual("README.md", Path.GetFileName(Constants.App.Repository.AboutUri.LocalPath));
        }

        [TestCase("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886", "dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886")]
        [TestCase("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886  WinMemoryCleaner.Community.exe", "dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886")]
        [TestCase("SHA256 (WinMemoryCleaner.Community.exe) = DC91898B20976C1B197B28D64EA8C4BE8D9832271B91E46EB97A0A61EA987886", "dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886")]
        [TestCase("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae *release.zip", "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae")]
        public void ChecksumText_IsParsedFromTheFormatsPublishersUse(string text, string expected)
        {
            var actual = Helper.GetSha256FromChecksumText(text);

            Assert.IsNotNull(actual, "No checksum was parsed from: " + text);
            StringAssert.AreEqualIgnoringCase(expected, actual, "Checksums are case-insensitive.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea98788")]
        [TestCase("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea98788664")]
        [TestCase("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea98788z")]
        [TestCase("no checksum is published here")]
        public void InvalidChecksumText_IsRejected(string text)
        {
            Assert.IsNull(Helper.GetSha256FromChecksumText(text));
        }

        [Test]
        public void FileSha256_MatchesKnownChecksumsAndHandlesUnreadableFiles()
        {
            var empty = WriteTemporaryFile(string.Empty);
            var text = WriteTemporaryFile("WinMemoryCleaner Community");

            try
            {
                Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Helper.GetFileSha256(empty));
                Assert.AreEqual("dc91898b20976c1b197b28d64ea8c4be8d9832271b91e46eb97a0a61ea987886", Helper.GetFileSha256(text));
                Assert.IsNull(Helper.GetFileSha256(null));
                Assert.IsNull(Helper.GetFileSha256(string.Empty));
                Assert.IsNull(Helper.GetFileSha256(Path.Combine(Path.GetTempPath(), "wmc-community-missing-" + Guid.NewGuid().ToString("N") + ".bin")));
            }
            finally
            {
                Helper.DeleteFile(empty);
                Helper.DeleteFile(text);
            }
        }

        [TestCase("http://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/WinMemoryCleaner.Community.exe.sha256", false)]
        [TestCase("file:///C:/update/WinMemoryCleaner.Community.exe.sha256", false)]
        [TestCase("https://github.com/shabhui/WinMemoryCleaner-Community/releases/latest/download/WinMemoryCleaner.Community.exe.sha256", true)]
        public void UpdateChannel_IsVerifiableOnlyOverHttps(string checksumSource, bool expected)
        {
            Assert.AreEqual(expected, Updater.IsVerifiableUpdateChannel(new Uri(checksumSource)));
        }

        [Test]
        public void UpdateChannel_IsNotVerifiableWithoutAChecksumSource()
        {
            Assert.IsFalse(Updater.IsVerifiableUpdateChannel(null));
            Assert.IsFalse(Updater.IsVerifiableUpdateChannel(new Uri("WinMemoryCleaner.Community.exe.sha256", UriKind.Relative)));
        }

        [Test]
        public void UnverifiedUpdateDownload_IsDiscardedAndNeverInstalled()
        {
            // Whatever the state of the channel, a completed download may only be installed after a
            // successful checksum verification: without that verification the payload must be gone
            // and no replacement process may be scheduled.
            var downloaded = WriteTemporaryFile("unverified update payload");
            var target = Path.Combine(Path.GetTempPath(), "wmc-community-target-" + Guid.NewGuid().ToString("N") + ".exe");
            var method = typeof(Updater).GetMethod("OnFileDownloadCompleted", BindingFlags.Static | BindingFlags.NonPublic);

            try
            {
                Assert.IsNotNull(method, "The file download completion handler was renamed; update this guard.");

                if (Helper.IsAutoUpdateSupported)
                    Assert.IsTrue(Updater.IsVerifiableUpdateChannel(Constants.App.Repository.LatestExeHashUri), "Updates must never be supported without a verifiable checksum source.");

                var updateInfo = Tuple.Create(downloaded, target, "WinMemoryCleaner.Community.exe", new Version(3, 0, 8, 2), new string[0]);

                method.Invoke(null, new object[] { null, new AsyncCompletedEventArgs(null, false, updateInfo) });

                Assert.IsFalse(File.Exists(downloaded), "The unverified download must be deleted.");
                Assert.IsFalse(File.Exists(target), "The unverified download must never replace the executable.");
                Assert.IsNull(Updater.Process);
            }
            finally
            {
                Helper.DeleteFile(downloaded);
                Helper.DeleteFile(target);
            }
        }

        private static string WriteTemporaryFile(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "wmc-community-hash-" + Guid.NewGuid().ToString("N") + ".bin");

            File.WriteAllText(path, content);

            return path;
        }

        [Test]
        public void TestBuild_IsIsolatedAndDefaultsToNoAutomaticUpdate()
        {
            Assert.IsTrue(App.IsTestBuild);
            Settings.Reset(true);
            Assert.IsFalse(Settings.AutoUpdate);
            // Save and migration are intentionally no-ops in this compile-time test build.
            Assert.DoesNotThrow(Settings.Save);
            Assert.DoesNotThrow(Migrator.Run);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnconfiguredUpdater_DoesNotCreateClientOrChangeNetworkSettings(bool enabled)
        {
            var originalEnabled = Settings.AutoUpdate;
            var protocol = ServicePointManager.SecurityProtocol;
            var connectionLimit = ServicePointManager.DefaultConnectionLimit;
            var lastCheck = typeof(Updater).GetField("_lastCheck", BindingFlags.Static | BindingFlags.NonPublic);
            var client = typeof(Updater).GetField("_client", BindingFlags.Static | BindingFlags.NonPublic);
            var lastCheckBefore = lastCheck.GetValue(null);
            try
            {
                Settings.AutoUpdate = enabled;
                Updater.Update();
                Assert.IsNull(client.GetValue(null));
                Assert.IsNull(Updater.Process);
                Assert.AreEqual(lastCheckBefore, lastCheck.GetValue(null));
                Assert.AreEqual(protocol, ServicePointManager.SecurityProtocol);
                Assert.AreEqual(connectionLimit, ServicePointManager.DefaultConnectionLimit);
            }
            finally
            {
                Settings.AutoUpdate = originalEnabled;
            }
        }

        [TestCase(true, true, 24, true)]
        [TestCase(true, true, 23, false)]
        [TestCase(true, true, -1, false)]
        [TestCase(true, false, 48, false)]
        [TestCase(false, true, 48, false)]
        [TestCase(false, false, 48, false)]
        public void UpdatePolicy_RespectsSettingSupportAndInterval(bool supported, bool enabled, int hours, bool expected)
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(expected, Updater.ShouldCheckForUpdates(supported, enabled, now, now.AddHours(-hours)));
        }

        [TestCase(@"C:\Utilitários\WinMemoryCleaner.Community.exe", "André")]
        [TestCase(@"C:\工具 & 测试\WinMemoryCleaner.Community.exe", "用户 & <test>")]
        public void StartupTask_WritesValidUtf16AndEscapesText(string executablePath, string userName)
        {
            var file = Path.Combine(Path.GetTempPath(), "wmc-community-task-" + Guid.NewGuid().ToString("N") + ".xml");
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                var title = Constants.App.Title + " & <test>";
                var xml = StartupTask.CreateXml(title, executablePath, "S-1-5-21-1234", userName,
                    new DateTime(2026, 1, 2, 3, 4, 5));
                App.WriteStartupTaskXml(file, xml);

                var bytes = File.ReadAllBytes(file);
                Assert.AreEqual(0xff, bytes[0], "UTF-16 LE BOM is required.");
                Assert.AreEqual(0xfe, bytes[1]);
                var settings = new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit };
                XmlDocument doc;
                using (var reader = XmlReader.Create(file, settings))
                {
                    doc = new XmlDocument { XmlResolver = null };
                    doc.Load(reader);
                }
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                Assert.AreEqual("\"" + executablePath + "\"", doc.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", ns).InnerText);
                Assert.AreEqual(title + " (" + userName + ")", doc.SelectSingleNode("/t:Task/t:RegistrationInfo/t:Author", ns).InnerText);
                Assert.AreEqual("2026-01-02T03:04:05", doc.SelectSingleNode("/t:Task/t:RegistrationInfo/t:Date", ns).InnerText);
                Assert.AreEqual("HighestAvailable", doc.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:RunLevel", ns).InnerText);
                Assert.AreEqual("IgnoreNew", doc.SelectSingleNode("/t:Task/t:Settings/t:MultipleInstancesPolicy", ns).InnerText);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    // No NotifyIcon or application window is created by these PR #204 regression tests.
    [TestFixture]
    [RequiresSTA]
    public sealed class HeadlessConcurrencyTests
    {
        [Test]
        public void DispatcherMarshalling_DoesNotWaitForBusyUiThread()
        {
            var application = System.Windows.Application.Current ?? new System.Windows.Application();
            Assert.IsTrue(application.Dispatcher.CheckAccess());
            var invoke = typeof(NotificationService).GetMethod("InvokeOnUi", BindingFlags.Static | BindingFlags.NonPublic);
            Exception error = null;
            var callbackThread = 0;
            var uiThread = Thread.CurrentThread.ManagedThreadId;
            var worker = new Thread(() =>
            {
                try
                {
                    invoke.Invoke(null, new object[] { (Action)(() => callbackThread = Thread.CurrentThread.ManagedThreadId) });
                }
                catch (Exception ex) { error = ex; }
            });
            worker.IsBackground = true;
            worker.Start();
            // Deliberately do not pump the UI queue until the background caller has returned.
            var returnedWithoutUi = worker.Join(2000);
            var frame = new DispatcherFrame();
            application.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Assert.IsTrue(worker.Join(2000));
            Assert.IsNull(error);
            Assert.IsTrue(returnedWithoutUi, "Synchronous Dispatcher.Invoke would block here.");
            Assert.AreEqual(uiThread, callbackThread);
        }

        [Test]
        public void BothIconRenderPaths_CanRunConcurrentlyWithoutFallbackOrDisposedHandles()
        {
            using (var service = new NotificationService(null))
            {
                var type = typeof(NotificationService);
                var renderUsage = type.GetMethod("GetMemoryUsageIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                var renderRotation = type.GetMethod("GetRotatedIcon", BindingFlags.Instance | BindingFlags.NonPublic);
                var image = (Icon)type.GetField("_imageIcon", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
                var memory = new Memory(WinMemoryCleaner.Test.Mocker.CreateMemoryStatusEx());
                var errors = new ConcurrentQueue<Exception>();
                var workers = new List<Thread>();
                for (var i = 0; i < 8; i++)
                {
                    var rotate = i % 2 == 0;
                    var worker = new Thread(() =>
                    {
                        try
                        {
                            for (var j = 0; j < 25; j++)
                            {
                                var icon = (Icon)(rotate
                                    ? renderRotation.Invoke(service, new object[] { image, 45F })
                                    : renderUsage.Invoke(service, new object[] { memory, false }));
                                if (icon == null || object.ReferenceEquals(icon, image))
                                    throw new InvalidOperationException("Rendering fell back to the shared icon.");
                                using (icon)
                                {
                                    if (icon.Handle == IntPtr.Zero)
                                        throw new InvalidOperationException("Invalid rendered icon handle.");
                                }
                            }
                        }
                        catch (Exception ex) { errors.Enqueue(ex); }
                    });
                    worker.IsBackground = true;
                    workers.Add(worker);
                    worker.Start();
                }
                foreach (var worker in workers)
                    Assert.IsTrue(worker.Join(15000), "Icon rendering blocked.");
                Assert.IsEmpty(errors.ToArray());
            }
        }
    }
}

#pragma warning restore 1591
