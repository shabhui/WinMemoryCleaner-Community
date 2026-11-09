using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using Forms = System.Windows.Forms;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    internal sealed class TrayRefreshContext : IDisposable
    {
        internal readonly Forms.NotifyIcon Icon = new Forms.NotifyIcon();
        internal readonly NotificationService Service;

        internal TrayRefreshContext()
        {
            Assert.IsTrue(App.IsTestBuild);
            Settings.Reset(true);
            Settings.TrayIconShowMemoryUsage = true;
            Localizer.Language = new Language(CultureInfo.GetCultureInfo("en"));
            // Keep NotifyIcon invisible and skip menu/window initialization. This exercises
            // the real Update path without creating a shell icon or changing user settings.
            Service = new NotificationService(null);
            typeof(NotificationService).GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Service, Icon);
        }

        internal object Field(string name)
        {
            var field = typeof(NotificationService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return field.GetValue(Service);
        }

        internal static Memory Memory(int physical, int virtualUsage = 40)
        {
            var memory = new Memory();
            memory.Physical.Used.Percentage = physical;
            memory.Virtual.Used.Percentage = virtualUsage;
            return memory;
        }

        public void Dispose()
        {
            Service.Dispose();
            Icon.Dispose();
            Settings.Reset(true);
            GC.SuppressFinalize(this);
        }
    }

    [TestFixture]
    [RequiresSTA]
    public sealed class NotificationRefreshTests
    {
        [Test]
        public void RepeatedEquivalentUpdatesReuseTheCurrentIcon()
        {
            using (var context = new TrayRefreshContext())
            {
                context.Service.Update(TrayRefreshContext.Memory(50));
                var original = context.Icon.Icon;
                Assert.IsNotNull(original);
                Assert.AreNotSame(context.Field("_imageIcon"), original);
                for (int i = 0; i < 100; i++) context.Service.Update(TrayRefreshContext.Memory(50));
                Assert.AreSame(original, context.Icon.Icon);
                Assert.AreNotEqual(IntPtr.Zero, original.Handle);
            }
        }

        [Test]
        public void TooltipOnlyChangesDoNotRedrawTheMemoryIcon()
        {
            using (var context = new TrayRefreshContext())
            {
                Settings.ShowVirtualMemory = true;
                context.Service.Update(TrayRefreshContext.Memory(50, 40));
                var original = context.Icon.Icon;
                string text = context.Icon.Text;
                context.Service.Update(TrayRefreshContext.Memory(50, 70));
                Assert.AreNotEqual(text, context.Icon.Text);
                StringAssert.Contains("70%", context.Icon.Text);
                Assert.AreSame(original, context.Icon.Icon);
                text = context.Icon.Text;
                Localizer.Language = new Language(CultureInfo.GetCultureInfo("zh-Hans"));
                context.Service.Update(TrayRefreshContext.Memory(50, 70));
                Assert.AreNotEqual(text, context.Icon.Text);
                Assert.AreSame(original, context.Icon.Icon);
            }
        }

        [Test]
        public void ColorsThresholdsTransparencyAndOptimizingStateInvalidateTheAppearance()
        {
            using (var context = new TrayRefreshContext())
            {
                var memory = TrayRefreshContext.Memory(50);
                context.Service.Update(memory);
                AssertRedrawn(context, memory, delegate { ((SolidBrush)Settings.TrayIconTextColor).Color = Color.Lime; });
                AssertRedrawn(context, memory, delegate { ((SolidBrush)Settings.TrayIconBackgroundColor).Color = Color.Blue; });
                AssertRedrawn(context, memory, delegate { Settings.TrayIconWarningLevel = 40; });
                AssertRedrawn(context, memory, delegate { Settings.TrayIconUseTransparentBackground = true; });
                AssertRedrawn(context, memory, delegate { Settings.TrayIconDangerLevel = 45; });
                var normal = context.Icon.Icon;
                context.Service.Update(memory, true);
                Assert.AreNotSame(normal, context.Icon.Icon);
                var optimizing = context.Icon.Icon;
                context.Service.Update(memory, true);
                Assert.AreSame(optimizing, context.Icon.Icon);
                context.Service.Update(memory, false);
                Assert.AreNotSame(optimizing, context.Icon.Icon);
            }
        }

        private static void AssertRedrawn(TrayRefreshContext context, Memory memory, Action change)
        {
            var previous = context.Icon.Icon;
            change();
            context.Service.Update(memory);
            Assert.AreNotSame(previous, context.Icon.Icon);
            Assert.AreNotSame(context.Field("_imageIcon"), context.Icon.Icon, "A render failure must not masquerade as cache invalidation.");
        }

        [Test]
        public void SwitchingFromAnimatedImageToMemoryStopsRotation()
        {
            using (var context = new TrayRefreshContext())
            {
                var memory = TrayRefreshContext.Memory(50);
                Settings.TrayIconShowMemoryUsage = false;
                context.Service.Update(memory, true);
                Assert.IsNotNull(context.Field("_rotationTimer"));
                Settings.TrayIconShowMemoryUsage = true;
                context.Service.Update(memory, true);
                Assert.IsNull(context.Field("_rotationTimer"));
                var memoryIcon = context.Icon.Icon;
                Assert.AreNotSame(context.Field("_imageIcon"), memoryIcon);
                Settings.TrayIconShowMemoryUsage = false;
                context.Service.Update(memory, false);
                Assert.AreSame(context.Field("_imageIcon"), context.Icon.Icon);
            }
        }

        [Test]
        public void ParallelUpdatesFinishAndLeaveAValidFinalIcon()
        {
            using (var context = new TrayRefreshContext())
            {
                var errors = new List<Exception>();
                var threads = new List<Thread>();
                for (int i = 0; i < 6; i++)
                {
                    int usage = 20 + i;
                    var thread = new Thread(delegate()
                    {
                        try { for (int j = 0; j < 25; j++) context.Service.Update(TrayRefreshContext.Memory(usage)); }
                        catch (Exception error) { lock (errors) errors.Add(error); }
                    });
                    thread.IsBackground = true;
                    threads.Add(thread);
                    thread.Start();
                }
                foreach (var thread in threads) Assert.IsTrue(thread.Join(5000), "A tray update blocked.");
                Assert.IsEmpty(errors);
                context.Service.Update(TrayRefreshContext.Memory(42));
                StringAssert.Contains("42%", context.Icon.Text);
                Assert.AreNotEqual(IntPtr.Zero, context.Icon.Icon.Handle);
                context.Service.Dispose();
                context.Service.Update(TrayRefreshContext.Memory(42));
                Assert.IsNull(context.Field("_currentIcon"));
            }
        }
    }

    // Run separately from the headless suite: WPF permits only one Application lifetime
    // per AppDomain, and the legacy headless dispatcher test shuts its Application down.
    [TestFixture]
    [RequiresSTA]
    [Category("Desktop")]
    public sealed class NotificationDispatchTests
    {
        private static void EnsureApplication()
        {
            var application = System.Windows.Application.Current ?? new System.Windows.Application();
            application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            Assert.IsTrue(application.Dispatcher.CheckAccess());
        }

        private static void Drain()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        [Test]
        public void BusyUiReceivesOneLatestUpdateInsteadOfAnIconBacklog()
        {
            EnsureApplication();
            using (var context = new TrayRefreshContext())
            {
                int posted = 0;
                DispatcherHookEventHandler hook = delegate { Interlocked.Increment(ref posted); };
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted += hook;
                try
                {
                    var thread = new Thread(delegate()
                    {
                        for (int i = 0; i < 100; i++) context.Service.Update(TrayRefreshContext.Memory(i));
                        context.Service.Update(TrayRefreshContext.Memory(42));
                    });
                    thread.IsBackground = true;
                    thread.Start();
                    Assert.IsTrue(thread.Join(10000), "Update must return while the UI is deliberately not pumping.");
                    Assert.AreEqual(1, posted, "Only one UI callback may wait; it must consume the latest state.");
                }
                finally { Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= hook; Drain(); }
                StringAssert.Contains("42%", context.Icon.Text);
                Assert.AreNotEqual(IntPtr.Zero, context.Icon.Icon.Handle);
            }
        }

        [Test]
        public void AbortedDispatchReleasesPendingIconAndAllowsRetry()
        {
            EnsureApplication();
            using (var context = new TrayRefreshContext())
            {
                DispatcherOperation operation = null;
                DispatcherHookEventHandler hook = delegate(object sender, DispatcherHookEventArgs args) { operation = args.Operation; };
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted += hook;
                try
                {
                    var thread = new Thread(delegate() { context.Service.Update(TrayRefreshContext.Memory(50)); });
                    thread.IsBackground = true;
                    thread.Start();
                    Assert.IsTrue(thread.Join(5000));
                }
                finally { Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= hook; }
                Assert.IsNotNull(operation);
                var pending = (Icon)context.Field("_pendingIcon");
                Assert.IsNotNull(pending);
                Assert.IsTrue(operation.Abort());
                Assert.IsNull(context.Field("_pendingIcon"));
                Assert.Throws<ObjectDisposedException>(delegate { pending.Handle.ToInt64(); });
                context.Service.Update(TrayRefreshContext.Memory(50));
                Assert.IsNotNull(context.Icon.Icon);
                StringAssert.Contains("50%", context.Icon.Text);
                Drain();
            }
        }

        [Test]
        public void DisposeWhileQueuedReleasesTheUnappliedNativeIcon()
        {
            EnsureApplication();
            using (var context = new TrayRefreshContext())
            {
                var thread = new Thread(delegate() { context.Service.Update(TrayRefreshContext.Memory(50)); });
                thread.IsBackground = true;
                thread.Start();
                Assert.IsTrue(thread.Join(5000));
                var pending = (Icon)context.Field("_pendingIcon");
                Assert.IsNotNull(pending);
                context.Service.Dispose();
                Assert.IsNull(context.Field("_pendingIcon"));
                Assert.Throws<ObjectDisposedException>(delegate { pending.Handle.ToInt64(); });
                Drain();
                Assert.IsNull(context.Field("_currentIcon"));
                Assert.IsFalse(context.Icon.Visible);
            }
        }
    }
}
#pragma warning restore 1591
