using NUnit.Framework;
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    public sealed class MonitoringWindowTests : IDisposable
    {
        private MainWindow _window;
        private ViewModelLocator _locator;
        private ResourceDictionary _oldResources;
        private NotificationService _notifications;
        private IDictionary _oldRegistrations, _oldSingletons;
        private bool _disposed;

        private MainWindow CreateWindow(bool overlayEnabled = false)
        {
            Assert.IsNotNull(typeof(MainWindow).GetProperty("Monitoring"), "The real shell must expose its independent monitoring model.");
            Assert.IsNotNull(typeof(MainWindow).GetMethod("SelectPage"), "Navigation must select the real overview/process/settings pages.");
            Assert.IsTrue((bool)typeof(App).GetProperty("IsTestBuild", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null));
            Settings.Reset(true);
            Settings.Monitoring.OverlayEnabled = overlayEnabled;
            Settings.UseHotkey = false;
            Settings.FontSize = 14;
            Localizer.Language = new Language(System.Globalization.CultureInfo.GetCultureInfo("zh-Hans"));
            typeof(App).GetProperty("Version").SetValue(null, typeof(App).Assembly.GetName().Version, null);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Assert.IsTrue(application.Dispatcher.CheckAccess());
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _oldResources = application.Resources;
            _notifications = new NotificationService(null);
            _oldRegistrations = SnapshotContainer("_container");
            _oldSingletons = SnapshotContainer("_singleton");
            DependencyInjection.Container.Register<IComputerService, ComputerService>();
            DependencyInjection.Container.Register<IHotkeyService, HotkeyService>();
            DependencyInjection.Container.Register<INotificationService>(_notifications);
            _locator = new ViewModelLocator();
            var resources = new ResourceDictionary();
            resources[typeof(Button)] = new Style(typeof(Button));
            resources["ViewModelLocator"] = _locator;
            resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
            resources["InverseBooleanToVisibilityConverter"] = new InverseBooleanToVisibilityConverter();
            resources["NullToVisibilityConverter"] = new NullToVisibilityConverter();
            resources["InverseBooleanConverter"] = new InverseBooleanConverter();
            resources["BrushToHexConverter"] = new BrushToHexConverter();
            resources["StringFormatConverter"] = new StringFormatConverter();
            resources["EnumToBooleanConverter"] = new EnumToBooleanConverter();
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WinMemoryCleaner.Community;component/Resources/MonitoringStyles.xaml", UriKind.Relative) });
            application.Resources = resources;
            _window = new MainWindow();
            return _window;
        }

        private object Monitoring
        {
            get { return typeof(MainWindow).GetProperty("Monitoring").GetValue(_window, null); }
        }
        private void Select(int page) { typeof(MainWindow).GetMethod("SelectPage").Invoke(_window, new object[] { page }); }
        private static void Drain()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        private static void Set(object instance, string property, object value) { instance.GetType().GetProperty(property).SetValue(instance, value, null); }

        private static IDictionary ContainerState(string name)
        {
            return (IDictionary)typeof(DependencyInjection.Container).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }
        private static IDictionary SnapshotContainer(string name)
        {
            var live = ContainerState(name);
            var saved = new Hashtable(live);
            live.Clear();
            return saved;
        }
        private static void RestoreContainer(string name, IDictionary saved)
        {
            if (saved == null) return;
            var live = ContainerState(name);
            live.Clear();
            foreach (DictionaryEntry entry in saved) live.Add(entry.Key, entry.Value);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_window != null) _window.Close();
            if (_locator != null) _locator.Dispose();
            if (_notifications != null) _notifications.Dispose();
            if (_oldResources != null) Application.Current.Resources = _oldResources;
            RestoreContainer("_container", _oldRegistrations);
            RestoreContainer("_singleton", _oldSingletons);
            _oldRegistrations = null; _oldSingletons = null;
            _window = null; _locator = null; _notifications = null; _oldResources = null;
            Settings.Reset(true);
            Drain();
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                if (_window != null) _window.Dispose();
                if (_locator != null) _locator.Dispose();
                if (_notifications != null) _notifications.Dispose();
                _window = null; _locator = null; _notifications = null;
            }
        }

        [Test]
        public void Navigation_SelectsOnePageWithoutReplacingTheOriginalCleaner()
        {
            var window = CreateWindow();
            var cleaner = window.DataContext;
            Select(1);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("OverviewPage")).Visibility);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("ProcessPage")).Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("SettingsPage")).Visibility);
            Select(2);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("ProcessPage")).Visibility);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)window.FindName("SettingsPage")).Visibility);
            Assert.AreSame(cleaner, window.DataContext);
            var optimize = (Button)window.FindName("Optimize");
            Assert.AreSame(((MainViewModel)cleaner).OptimizeCommand, optimize.Command);
            Assert.IsTrue(((DataGrid)window.FindName("ProcessGrid")).EnableRowVirtualization);
        }

        [Test]
        public void SettingsPage_PreservesAllLegacyGroupsAndExclusionCommands()
        {
            var window = CreateWindow(); Select(2); window.Show(); Drain();
            var panel = ((ContentControl)window.FindName("SettingsHost")).Content as FrameworkElement;
            Assert.IsNotNull(panel, "The original settings must be instantiated on demand.");
            Assert.AreSame(window.DataContext, panel.DataContext);
            Assert.AreEqual(10, ((ItemsControl)panel.FindName("SettingItemsControl")).Items.Count);
            Assert.Greater(((ItemsControl)panel.FindName("MemoryAreaItemsControl")).Items.Count, 0);
            Assert.Greater(((ItemsControl)panel.FindName("TrayIconItemsControl")).Items.Count, 0);
            var interval = (Slider)panel.FindName("AutoOptimizationIntervalSlider");
            interval.Value = 7;
            interval.GetBindingExpression(RangeBase.ValueProperty).UpdateSource();
            Assert.AreEqual(7, Settings.AutoOptimizationInterval);
            Assert.IsNotNull(panel.FindName("AutoOptimizationMemoryUsageSlider"));
            Assert.IsNotNull(panel.FindName("FontSizeSlider"));
            Assert.IsNotNull(panel.FindName("OptimizationModifiers"));
            Assert.IsNotNull(panel.FindName("OptimizationKey"));
            Assert.IsNotNull(panel.FindName("TrayIconDangerColor"));
            Assert.IsNotNull(panel.FindName("TrayIconWarningLevel"));
            Assert.IsNotNull(panel.FindName("ProcessExclusionList"));
            var add = (Button)panel.FindName("AddExclusion");
            Assert.AreSame(((MainViewModel)window.DataContext).AddProcessToExclusionListCommand, add.Command);
        }

        [Test]
        public void ProcessColumns_RefreshForChineseAndEnglish()
        {
            var window = CreateWindow(); Select(1);
            var grid = (DataGrid)window.FindName("ProcessGrid");
            Assert.AreEqual("进程名称", grid.Columns[0].Header);
            Assert.AreEqual("工作集内存", grid.Columns[2].Header);
            Assert.AreEqual("私有内存", grid.Columns[3].Header);
            Assert.AreEqual("专用显存", grid.Columns[6].Header);
            Localizer.Language = new Language(System.Globalization.CultureInfo.GetCultureInfo("en"));
            Assert.AreEqual("Process name", grid.Columns[0].Header);
            Assert.AreEqual("Working set", grid.Columns[2].Header);
        }

        [Test]
        public void Overlay_IsIndependentAndTurnsOffWithoutKeepingDetailedDemand()
        {
            var window = CreateWindow(); window.Show(); Drain();
            Assert.IsFalse((bool)Monitoring.GetType().GetProperty("OverlayEnabled").GetValue(Monitoring, null));
            Set(Monitoring, "OverlayEnabled", true); Drain();
            var overlay = Application.Current.Windows.Cast<Window>().Single(w => w.GetType().Name == "MonitoringOverlay");
            Assert.IsNull(overlay.Owner);
            Assert.IsFalse(overlay.ShowActivated);
            Assert.IsFalse(overlay.ShowInTaskbar);
            Assert.IsTrue(overlay.Topmost);
            window.Hide(); Drain();
            Assert.IsTrue(overlay.IsVisible, "Hiding the cleaner must not hide an enabled independent overlay.");
            Set(Monitoring, "OverlayEnabled", false); Drain();
            Assert.IsFalse(overlay.IsVisible);
            Assert.AreEqual(5, ((MainViewModel)window.DataContext).GetType().GetProperty("MemoryRefreshSeconds").GetValue(window.DataContext, null));
        }

        [Test]
        public void Overlay_AllReadingsFitOneCompactHorizontalLine()
        {
            var window = CreateWindow(); window.Show(); Drain();
            Set(Monitoring, "ShowCpuTemperature", true);
            Set(Monitoring, "ShowGpuTemperature", true);
            Set(Monitoring, "OverlayEnabled", true); Drain();
            var overlay = Application.Current.Windows.Cast<Window>().Single(w => w is MonitoringOverlay);
            overlay.UpdateLayout();
            Assert.LessOrEqual(overlay.ActualHeight, 36, "A horizontal monitor must not remain the old tall panel.");
            Assert.That(overlay.ActualWidth, Is.InRange(120.0, 430.0));
            var metrics = overlay.FindName("OverlayMetrics") as StackPanel;
            Assert.IsNotNull(metrics);
            Assert.AreEqual(Orientation.Horizontal, metrics.Orientation);
            var groups = metrics.Children.Cast<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible).ToArray();
            Assert.AreEqual(3, groups.Length, "CPU and GPU temperatures belong beside their usage, not in extra rows.");
            var y = groups[0].TranslatePoint(new Point(), overlay).Y;
            foreach (var group in groups) Assert.AreEqual(y, group.TranslatePoint(new Point(), overlay).Y, 1.0);
        }

        [Test]
        public void Overlay_SettingsSwitchToNarrowVerticalAndPersistLayout()
        {
            var window = CreateWindow(); window.Show(); Select(2); Drain();
            Set(Monitoring, "ShowCpuTemperature", true);
            Set(Monitoring, "ShowGpuTemperature", true);
            Set(Monitoring, "OverlayEnabled", true); Drain();
            var choice = window.FindName("OverlayLayoutChoice") as ComboBox;
            Assert.IsNotNull(choice, "The user must be able to switch orientation in settings.");
            choice.SelectedIndex = 1;
            choice.GetBindingExpression(ComboBox.SelectedIndexProperty).UpdateSource(); Drain();
            var overlay = Application.Current.Windows.Cast<Window>().Single(w => w is MonitoringOverlay);
            overlay.UpdateLayout();
            Assert.That(overlay.ActualWidth, Is.InRange(70.0, 160.0));
            Assert.That(overlay.ActualHeight, Is.InRange(60.0, 100.0));
            var metrics = overlay.FindName("OverlayMetrics") as StackPanel;
            Assert.IsNotNull(metrics);
            Assert.AreEqual(Orientation.Vertical, metrics.Orientation);
            var json = Settings.Monitoring.Serialize();
            var loaded = MonitoringOptions.Deserialize(json);
            var layout = loaded.GetType().GetProperty("Layout");
            Assert.IsNotNull(layout); Assert.AreEqual(1, layout.GetValue(loaded, null));
            choice.SelectedIndex = 0;
            choice.GetBindingExpression(ComboBox.SelectedIndexProperty).UpdateSource(); Drain();
            Assert.LessOrEqual(overlay.ActualHeight, 36);
        }

        [Test]
        public void Overlay_LanguageSwitchPreservesVerticalLayoutAndCustomPosition()
        {
            var window = CreateWindow(); window.Show(); Select(2); Drain();
            Set(Monitoring, "OverlayLayout", 1);
            window.Monitoring.SavePosition("display-for-language-test", 123, 456); Drain();
            Assert.AreEqual(1, Settings.Monitoring.Layout);
            Assert.AreEqual(4, Settings.Monitoring.Corner);
            foreach (string language in new[] { "en", "zh-Hans" })
            {
                Localizer.Language = new Language(System.Globalization.CultureInfo.GetCultureInfo(language)); Drain();
                Assert.AreEqual(1, Settings.Monitoring.Layout, "Translating layout choices must not persist a temporary cleared selection.");
                Assert.AreEqual(4, Settings.Monitoring.Corner, "Translating corners must keep the custom position.");
                Assert.AreEqual("display-for-language-test", Settings.Monitoring.Screen);
                Assert.AreEqual(123, Settings.Monitoring.X); Assert.AreEqual(456, Settings.Monitoring.Y);
                Assert.AreEqual(1, ((ComboBox)window.FindName("OverlayLayoutChoice")).SelectedIndex);
            }
        }

        [Test]
        public void Overlay_MissingReadingsStayShortWithoutGrowingTheWindow()
        {
            CreateWindow();
            Set(Monitoring, "ShowCpuTemperature", true);
            Set(Monitoring, "ShowGpuTemperature", true);
            var overlay = new MonitoringOverlay((MonitoringViewModel)Monitoring);
            try
            {
                overlay.Show(); Drain(); overlay.UpdateLayout();
                foreach (var name in new[] { "OverlayCpuValue", "OverlayGpuValue", "OverlayCpuTemperatureValue", "OverlayGpuTemperatureValue" })
                {
                    var reading = overlay.FindName(name) as TextBlock;
                    Assert.IsNotNull(reading, name);
                    Assert.AreEqual("—", reading.Text, "Unavailable and warm-up states belong in details, not long floating labels.");
                }
                Assert.LessOrEqual(overlay.ActualHeight, 36);
            }
            finally { overlay.Close(); }
        }

        [Test]
        public void Overlay_OnlySelectedHardwareGroupsRemainVisible()
        {
            var window = CreateWindow(); window.Show(); Drain();
            Set(Monitoring, "ShowCpu", false);
            Set(Monitoring, "ShowGpu", false);
            Set(Monitoring, "OverlayEnabled", true); Drain();
            var overlay = Application.Current.Windows.Cast<Window>().Single(w => w is MonitoringOverlay);
            var metrics = overlay.FindName("OverlayMetrics") as StackPanel;
            Assert.IsNotNull(metrics);
            Assert.AreEqual(1, metrics.Children.Cast<FrameworkElement>().Count(child => child.Visibility == Visibility.Visible));
            overlay.UpdateLayout(); Assert.LessOrEqual(overlay.ActualWidth, 120);
            Set(Monitoring, "ShowCpuTemperature", true); Drain();
            Assert.AreEqual(2, metrics.Children.Cast<FrameworkElement>().Count(child => child.Visibility == Visibility.Visible));
        }

        [Test]
        public void MonitorOnlyButton_HidesTheShellWithoutChangingCloseBehavior()
        {
            var window = CreateWindow(); window.Show(); Select(2); Drain();
            Settings.CloseToTheNotificationArea = false;
            var button = window.FindName("ShowMonitorOnly") as Button;
            Assert.IsNotNull(button, "Keeping only the strip must be an explicit, discoverable action.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain();
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(window.ShowInTaskbar);
            Assert.IsTrue((bool)Monitoring.GetType().GetProperty("OverlayEnabled").GetValue(Monitoring, null));
            Assert.IsTrue(Application.Current.Windows.Cast<Window>().Single(w => w is MonitoringOverlay).IsVisible);
            Assert.IsFalse(Settings.CloseToTheNotificationArea, "Do not overwrite the user's unrelated close preference.");
            Assert.AreEqual(0, (int)Monitoring.GetType().GetProperty("ProcessCount").GetValue(Monitoring, null));
        }

        [Test]
        public void EnabledOverlay_RestoresWhenTheMainWindowStartsHidden()
        {
            var window = CreateWindow(true); Drain();
            Assert.IsFalse(window.IsVisible, "A minimized startup must not show the large shell just to initialize the monitor.");
            var overlay = Application.Current.Windows.Cast<Window>().SingleOrDefault(w => w is MonitoringOverlay);
            Assert.IsNotNull(overlay, "Saved enabled monitor must initialize before the shell's first Loaded event.");
            Assert.IsTrue(overlay.IsVisible);
            Assert.LessOrEqual(overlay.ActualHeight, 36);
        }

        [Test]
        public void CompactLayout_KeepsCleanerUsableAndRestoresExpandedMinimum()
        {
            var window = CreateWindow(); window.Show(); Drain();
            var cleaner = (MainViewModel)window.DataContext;
            cleaner.CompactMode = true; Drain(); window.UpdateLayout();
            Assert.Less(window.ActualHeight, 350);
            Assert.Greater(((Button)window.FindName("Optimize")).ActualWidth, 50);
            cleaner.CompactMode = false; Drain();
            Assert.AreEqual(760, window.MinWidth);
            Assert.AreEqual(520, window.MinHeight);
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            Select(1); Drain(); window.UpdateLayout();
            var grid = (DataGrid)window.FindName("ProcessGrid");
            Assert.Greater(grid.ActualHeight, 70);
            var button = (Button)window.FindName("TerminateSelected");
            var position = button.TranslatePoint(new Point(), window);
            Assert.LessOrEqual(position.Y + button.ActualHeight, window.ActualHeight);
        }

        [Test]
        public void TerminationDialog_IdentifiesSelectionAndCancellationTakesNoAction()
        {
            var window = CreateWindow(); window.Show(); Select(1); Drain();
            var snapshotType = typeof(MainWindow).Assembly.GetType("WinMemoryCleaner.ProcessSnapshot");
            var rowType = typeof(MainWindow).Assembly.GetType("WinMemoryCleaner.ProcessRow");
            Assert.IsNotNull(snapshotType); Assert.IsNotNull(rowType);
            var snapshot = Activator.CreateInstance(snapshotType);
            Set(snapshot, "Id", Process.GetCurrentProcess().Id);
            Set(snapshot, "Name", "测试应用.exe");
            Set(snapshot, "CreationTime", 1L);
            var row = Activator.CreateInstance(rowType, new[] { snapshot });
            Set(Monitoring, "SelectedProcess", row);
            string prompt = null;
            var cancel = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            cancel.Tick += delegate
            {
                var dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w is MessageDialog);
                if (dialog == null) return;
                cancel.Stop();
                prompt = (string)dialog.DataContext.GetType().GetProperty("Message").GetValue(dialog.DataContext, null);
                dialog.DialogResult = false;
            };
            cancel.Start();
            ((Button)window.FindName("TerminateSelected")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            cancel.Stop(); Drain();
            Assert.IsNotNull(prompt);
            StringAssert.Contains("测试应用.exe", prompt);
            StringAssert.Contains("PID " + Process.GetCurrentProcess().Id, prompt);
            StringAssert.Contains("未保存的数据可能丢失", prompt);
            StringAssert.Contains("未确认", (string)Monitoring.GetType().GetProperty("ActionStatus").GetValue(Monitoring, null));
        }
    }
}
#pragma warning restore 1591
