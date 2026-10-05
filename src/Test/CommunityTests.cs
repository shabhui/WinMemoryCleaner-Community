using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            Assert.IsTrue(Constants.App.Repository.AboutUri.IsFile);
            Assert.AreEqual("README.md", Path.GetFileName(Constants.App.Repository.AboutUri.LocalPath));
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
                var doc = new XmlDocument();
                doc.Load(file);
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
