using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinMemoryCleaner.Sensors;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    [RequiresSTA]
    [Category("Desktop")]
    public sealed class MonitoringWindowTests : IDisposable
    {
        private MainWindow _window;
        private ViewModelLocator _locator;
        private ResourceDictionary _oldResources;
        private NotificationService _notifications;
        private IDictionary _oldRegistrations, _oldSingletons;
        private bool _disposed;

        private ProcessMonitor _processMonitor;
        private StringWriter _bindingOutput;
        private TextWriterTraceListener _bindingListener;
        private SourceLevels _oldBindingLevel;

        private MainWindow CreateWindow(bool overlayEnabled = false,Func<IResourceMonitor> createMonitor=null,Func<ProcessSnapshot,bool,bool,ProcessActionResult> executeAction=null,Func<SensorDriverState> inspectDriver=null,Func<bool,string,bool> beginDriver=null)
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
            _bindingOutput=new StringWriter(CultureInfo.InvariantCulture);
            _bindingListener=new TextWriterTraceListener(_bindingOutput);
            _oldBindingLevel=PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(_bindingListener);
            if(createMonitor==null)createMonitor=delegate { return new ResourceMonitor(new TemperatureSampler(null)); };
            if(executeAction==null)executeAction=delegate { return new ProcessActionResult(ProcessActionOutcome.Failed,"Controlled window test: no native process action."); };
            if(inspectDriver==null)inspectDriver=delegate { return new SensorDriverState(true,true,new Version(2,2,0),"Ready"); };
            if(beginDriver==null)beginDriver=delegate(bool confirmed,string path) { Assert.IsFalse(confirmed,"This test must not request installation; the native installer is never used.");return false; };
            _window=new MainWindow(createMonitor,executeAction,inspectDriver,beginDriver);
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

        private sealed class ProcessMonitor : IResourceMonitor
        {
            internal volatile string Mode="ready";
            internal int Samples,Disposals;
            public ResourceSnapshot Sample(bool processes,bool temperatures)
            {
                Interlocked.Increment(ref Samples);
                if(Mode=="null")return null;
                return processes&&Mode!="empty"?ProcessSnapshotForWindow():new ResourceSnapshot();
            }
            public void Suspend() { }
            public void Dispose() { Interlocked.Increment(ref Disposals); }
        }
        private static ResourceSnapshot ProcessSnapshotForWindow()
        {
            var snapshot=new ResourceSnapshot();
            snapshot.Processes.Add(new ProcessSnapshot { Id=8001,CreationTime=1101,Name="测试应用.exe",WorkingSetBytes=30 });
            snapshot.Processes.Add(new ProcessSnapshot { Id=8002,CreationTime=1102,Name="game.exe",WorkingSetBytes=20 });
            snapshot.Processes.Add(new ProcessSnapshot { Id=8003,CreationTime=1103,Name="other.exe",WorkingSetBytes=10 });
            return snapshot;
        }
        private MainWindow CreateProcessWindow(Func<ProcessSnapshot,bool,bool,ProcessActionResult> executeAction=null,string mode="ready")
        {
            _processMonitor=new ProcessMonitor { Mode=mode };
            var window=CreateWindow(false,delegate { return _processMonitor; },executeAction);
            window.Monitoring.RefreshSeconds=5;return window;
        }
        private static bool Until(Func<bool> ready,int milliseconds=2500)
        {
            var watch=Stopwatch.StartNew();
            while(!ready()&&watch.ElapsedMilliseconds<milliseconds) { Drain();Thread.Sleep(3); }
            return ready();
        }
        private void ShowProcesses(MainWindow window)
        {
            Select(1);window.Show();window.WindowState=WindowState.Normal;Drain();window.Monitoring.RefreshNow();
            Assert.IsTrue(Until(delegate { return window.Monitoring.ProcessCount==3; }),"Controlled successful process rows must reach the real window.");
            Drain();
        }
        private static void ApplyProcesses(MainWindow window,ResourceSnapshot snapshot,string failure=null)
        {
            var presentation=typeof(MonitoringViewModel).GetField("_processes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window.Monitoring);
            presentation.GetType().GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presentation,new object[]{snapshot,failure});
        }
        private static void SelectProcess(MainWindow window,int index=0)
        {
            var grid=(DataGrid)window.FindName("ProcessGrid");grid.SelectedItem=grid.Items[index];
            grid.GetBindingExpression(Selector.SelectedItemProperty).UpdateSource();Drain();
        }
        private static void InvokeAction(MainWindow window,bool terminate)
        {
            typeof(MainWindow).GetMethod("ExecuteSelected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{terminate});
        }
        private string Confirm(MainWindow window,bool confirmed,Action change=null)
        {
            string prompt=null;Exception changeError=null;
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
            EventHandler tick=delegate
            {
                var dialog=Application.Current.Windows.Cast<Window>().OfType<MessageDialog>().FirstOrDefault();
                if(dialog==null)return;
                timer.Stop();prompt=(string)dialog.DataContext.GetType().GetProperty("Message").GetValue(dialog.DataContext,null);
                try { if(change!=null)change(); }
                catch(Exception error) { changeError=error; }
                finally { dialog.DialogResult=changeError==null&&confirmed; }
            };
            timer.Tick+=tick;timer.Start();
            try { ((Button)window.FindName("TerminateSelected")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop();timer.Tick-=tick; }
            Assert.IsNull(changeError,"The controlled dialog mutation failed.");return prompt;
        }
        private void FinishBindingTrace()
        {
            if(_bindingListener==null)return;
            _bindingListener.Flush();PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindingListener);
            PresentationTraceSources.DataBindingSource.Switch.Level=_oldBindingLevel;
            string diagnostic=_bindingOutput.ToString();
            string path=Path.Combine(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location),"window-binding.log");
            byte[] bytes=Encoding.UTF8.GetBytes(diagnostic);
            using(var output=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read))output.Write(bytes,0,bytes.Length);
            _bindingListener.Dispose();_bindingOutput.Dispose();_bindingListener=null;_bindingOutput=null;
            Assert.AreEqual(string.Empty,diagnostic,"Real-window binding warnings/errors are retained in window-binding.log.");
        }

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
            try
            {
                if (_window != null) _window.Close();
                if (_locator != null) _locator.Dispose();
                if (_notifications != null) _notifications.Dispose();
                if (_oldResources != null) Application.Current.Resources = _oldResources;
                RestoreContainer("_container", _oldRegistrations);
                RestoreContainer("_singleton", _oldSingletons);
                _oldRegistrations = null; _oldSingletons = null;
                _window = null; _locator = null; _notifications = null; _oldResources = null;
                if(_processMonitor!=null)
                {
                    if(_processMonitor.Samples==0)_processMonitor.Dispose();
                    else Assert.IsTrue(Until(delegate { return _processMonitor.Disposals==1; }),"The owned monitor must be disposed exactly once.");
                }
                _processMonitor=null;Settings.Reset(true);Drain();
            }
            finally { FinishBindingTrace(); }
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
                if(_processMonitor!=null)
                {
                    if(_processMonitor.Samples==0)_processMonitor.Dispose();
                    else Assert.IsTrue(Until(delegate { return _processMonitor.Disposals==1; }));
                }
                if(_bindingListener!=null)
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindingListener);
                    PresentationTraceSources.DataBindingSource.Switch.Level=_oldBindingLevel;
                    _bindingListener.Dispose();
                }
                if(_bindingOutput!=null)_bindingOutput.Dispose();
                _processMonitor=null;_bindingListener=null;_bindingOutput=null;
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

        private static Memory DisplayMemory(int percent)
        {
            var memory=new Memory();memory.Physical.Used.Percentage=percent;return memory;
        }

        [TestCase(false,false)]
        [TestCase(false,true)]
        [TestCase(true,false)]
        [TestCase(true,true)]
        public void HiddenOrMinimizedShellDefersMemoryBindingsButKeepsLatestOverlayData(bool minimized,bool overlayEnabled)
        {
            var window=CreateProcessWindow();window.Show();Drain();
            var cleaner=(MainViewModel)window.DataContext;
            var bar=(ProgressBar)window.FindName("MemoryUsageBar");
            cleaner.Computer.Memory=DisplayMemory(20);Drain();Assert.AreEqual(20,bar.Value);
            window.Monitoring.OverlayEnabled=overlayEnabled;
            if(minimized)window.WindowState=WindowState.Minimized;else window.Hide();
            Drain();int notifications=0;
            cleaner.Computer.PropertyChanged+=delegate { notifications++; };
            Memory latest=null;
            for(int i=40;i<60;i++) { latest=DisplayMemory(i);cleaner.Computer.Memory=latest; }
            Drain();
            Assert.AreEqual(0,notifications,"Hidden/minimized main-window bindings should receive no periodic memory notifications.");
            Assert.AreSame(latest,cleaner.Computer.Memory,"Deferral must not leave the shared memory source stale.");
            Assert.AreEqual("59%",window.Monitoring.MemoryText);
            if(overlayEnabled)Assert.IsTrue(Application.Current.Windows.Cast<Window>().Any(w=>w is MonitoringOverlay&&w.IsVisible));
            window.WindowState=WindowState.Normal;if(!window.IsVisible)window.Show();Drain();
            Assert.AreEqual(1,notifications,"Restore must publish the latest snapshot exactly once, not replay intermediate samples.");
            Assert.AreEqual(59,bar.Value,"The restored real binding must immediately display the current percentage.");
            cleaner.Computer.Memory=DisplayMemory(87);Drain();
            Assert.AreEqual(2,notifications);Assert.AreEqual(87,bar.Value);
        }

        [Test]
        public void InitiallyHiddenShellPublishesTheLatestMemoryOnItsFirstShow()
        {
            var window=CreateProcessWindow();var cleaner=(MainViewModel)window.DataContext;int notifications=0;
            cleaner.Computer.PropertyChanged+=delegate { notifications++; };
            cleaner.Computer.Memory=DisplayMemory(32);Assert.AreEqual(0,notifications);
            window.Show();Drain();
            Assert.AreEqual(1,notifications);Assert.AreEqual(32,((ProgressBar)window.FindName("MemoryUsageBar")).Value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VisibleSettingsAndCompactCleanerKeepMemoryBindingsActive(bool compact)
        {
            var window=CreateProcessWindow();window.Show();Drain();var cleaner=(MainViewModel)window.DataContext;
            if(compact)cleaner.CompactMode=true;else Select(2);
            Drain();int notifications=0;cleaner.Computer.PropertyChanged+=delegate { notifications++; };
            cleaner.Computer.Memory=DisplayMemory(67);Drain();
            Assert.AreEqual(1,notifications,"A visible cleaner is a memory-display consumer even when detailed monitoring is paused.");
            Assert.AreEqual("67%",window.Monitoring.MemoryText);
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
                Assert.AreEqual(window.Monitoring.Labels["Vertical"], ((ComboBox)window.FindName("OverlayLayoutChoice")).Text);
                Assert.AreEqual(window.Monitoring.Labels["Custom"], ((ComboBox)window.FindName("OverlayCornerChoice")).Text);
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
            int executions=0;
            var window=CreateProcessWindow(delegate { Interlocked.Increment(ref executions);return new ProcessActionResult(ProcessActionOutcome.Terminated,string.Empty); });
            ShowProcesses(window);SelectProcess(window);
            string prompt=Confirm(window,false);Drain();
            Assert.IsNotNull(prompt);Assert.AreEqual(0,executions);
            StringAssert.Contains("测试应用.exe",prompt);StringAssert.Contains("PID 8001",prompt);
            StringAssert.Contains("未保存的数据可能丢失",prompt);StringAssert.Contains("未确认",window.Monitoring.ActionStatus);
        }

        [Test]
        public void ProcessSearchFeedbackAndEscapeUseTheRealControls()
        {
            var window=CreateProcessWindow();ShowProcesses(window);
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();
            var search=(TextBox)window.FindName("ProcessSearch");var clear=window.FindName("ClearProcessSearch") as Button;
            var count=window.FindName("ProcessCountDisplay") as TextBlock;var empty=window.FindName("ProcessEmptyState") as TextBlock;
            Assert.IsNotNull(clear);Assert.IsNotNull(count);Assert.IsNotNull(empty);
            Assert.AreEqual("Clear process search",AutomationProperties.GetName(clear));Assert.IsFalse(clear.IsEnabled);
            search.Text="game";Assert.AreEqual("game",window.Monitoring.SearchQuery);
            Assert.IsTrue(clear.IsEnabled);Assert.IsTrue(Until(delegate { return window.Monitoring.ProcessesView.Count==1; }));
            StringAssert.Contains("Matches: 1 / Total: 3",count.Text);
            search.Text="no-match";Assert.IsTrue(Until(delegate { return window.Monitoring.ProcessesView.Count==0; }));Drain();
            Assert.AreEqual(Visibility.Visible,empty.Visibility);Assert.AreEqual("No matching processes",empty.Text);
            window.Activate();search.Focus();Drain();Assert.IsTrue(search.IsKeyboardFocused);
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(search),Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent };
            search.RaiseEvent(escape);Drain();Assert.IsTrue(escape.Handled);Assert.AreEqual(string.Empty,search.Text);
            Assert.IsTrue(search.IsKeyboardFocused);Assert.AreEqual(3,window.Monitoring.ProcessesView.Count);Assert.IsFalse(clear.IsEnabled);
            search.Text=" ";Assert.IsTrue(clear.IsEnabled);clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Drain();
            Assert.AreEqual(string.Empty,search.Text);Assert.IsTrue(search.IsKeyboardFocused);Assert.IsFalse(clear.IsEnabled);
            search.Text="game";var grid=(DataGrid)window.FindName("ProcessGrid");
            grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(grid),Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.KeyDownEvent });
            Assert.AreEqual("game",search.Text,"Esc outside the search input must not clear its text.");Assert.IsTrue(window.IsVisible);
        }

        [Test]
        public void ProcessStatusShowsLoadingFailureAndSuccessfulEmptyWithoutInventingCounts()
        {
            var window=CreateProcessWindow(mode:"null");
            Select(1);window.Show();window.WindowState=WindowState.Normal;Drain();window.Monitoring.RefreshNow();
            var status=window.FindName("ProcessStatus") as TextBlock;var empty=window.FindName("ProcessEmptyState") as TextBlock;
            Assert.IsNotNull(status);Assert.IsNotNull(empty);
            Assert.IsTrue(Until(delegate { return window.Monitoring.ProcessStatusText==window.Monitoring.Labels["ProcessFailed"]; }));
            Assert.AreEqual("—",window.Monitoring.ProcessCountText);Assert.AreEqual(window.Monitoring.Labels["ProcessFailed"],empty.Text);
            window.Hide();window.Monitoring.SetViewState(true,true);Drain();
            Assert.AreEqual(window.Monitoring.Labels["ProcessLoading"],status.Text);Assert.AreEqual("—",window.Monitoring.ProcessCountText);
            ApplyProcesses(window,new ResourceSnapshot());Drain();
            Assert.AreEqual(window.Monitoring.Labels["ProcessEmpty"],status.Text);Assert.AreEqual(window.Monitoring.Labels["ProcessEmpty"],empty.Text);
            Assert.AreEqual(string.Format(Localizer.Culture,window.Monitoring.Labels["ProcessMatches"],0,0),window.Monitoring.ProcessCountText);
        }

        [Test]
        public void ProcessFeedbackStaysBoundedAndThemeAwareAtTheExpandedMinimum()
        {
            var window=CreateProcessWindow();ShowProcesses(window);window.Width=window.MinWidth;window.Height=window.MinHeight;
            var status=window.FindName("ProcessStatus") as TextBlock;var count=window.FindName("ProcessCountDisplay") as TextBlock;
            var clear=window.FindName("ClearProcessSearch") as Button;Assert.IsNotNull(status);Assert.IsNotNull(count);Assert.IsNotNull(clear);
            string detail=new string('x',4096);
            foreach(string language in new[]{"zh-Hans","en","de"})
            {
                Localizer.Language=new Language(CultureInfo.GetCultureInfo(language));window.Monitoring.ClearSearch();ApplyProcesses(window,ProcessSnapshotForWindow());
                SelectProcess(window);ApplyProcesses(window,null,detail);Drain();window.UpdateLayout();
                Assert.AreEqual(window.Monitoring.Labels["ProcessStale"],status.Text);Assert.AreEqual(detail,status.ToolTip);
                Assert.AreEqual(BindingStatus.Active,status.GetBindingExpression(TextBlock.TextProperty).Status);
                Assert.AreEqual(BindingStatus.Active,status.GetBindingExpression(FrameworkElement.ToolTipProperty).Status);
                Assert.LessOrEqual(status.ActualHeight,44);Assert.Greater(((DataGrid)window.FindName("ProcessGrid")).ActualHeight,70);
                foreach(string name in new[]{"ProcessSearch","ClearProcessSearch","ProcessCountDisplay","ProcessStatus","TerminateSelected"})
                {
                    var element=(FrameworkElement)window.FindName(name);var point=element.TranslatePoint(new Point(),window);
                    Assert.GreaterOrEqual(point.X,0);Assert.LessOrEqual(point.X+element.ActualWidth,window.ActualWidth);
                    Assert.LessOrEqual(point.Y+element.ActualHeight,window.ActualHeight);
                }
                Assert.AreEqual(window.Monitoring.Labels["ClearSearchName"],AutomationProperties.GetName(clear));
                SelectProcess(window);Assert.IsFalse(((Button)window.FindName("TrimSelected")).IsEnabled);
                Assert.IsFalse(((Button)window.FindName("TerminateSelected")).IsEnabled);
            }
            object oldBrush=Application.Current.Resources["SecondaryForeground"];
            try
            {
                foreach(Brush brush in new Brush[]{Brushes.White,Brushes.Black})
                { Application.Current.Resources["SecondaryForeground"]=brush;Drain();Assert.AreSame(brush,status.Foreground); }
            }
            finally { if(oldBrush==null)Application.Current.Resources.Remove("SecondaryForeground");else Application.Current.Resources["SecondaryForeground"]=oldBrush; }
            Assert.AreEqual(760,window.MinWidth);Assert.AreEqual(520,window.MinHeight);
        }

        [Test]
        public void ActionEntryRejectsStalePendingAndInactiveRowsEvenWhenButtonsAreBypassed()
        {
            int executions=0;
            var window=CreateProcessWindow(delegate { Interlocked.Increment(ref executions);return new ProcessActionResult(ProcessActionOutcome.Trimmed,string.Empty); });
            ShowProcesses(window);SelectProcess(window);ApplyProcesses(window,null,"controlled stale list");SelectProcess(window);
            ((Button)window.FindName("TrimSelected")).IsEnabled=true;InvokeAction(window,false);Drain();
            Assert.AreEqual(0,executions);Assert.AreEqual(window.Monitoring.Labels["ProcessTargetUnavailable"],window.Monitoring.ActionStatus);
            ApplyProcesses(window,ProcessSnapshotForWindow());SelectProcess(window);window.Monitoring.SearchQuery="other";
            InvokeAction(window,false);Drain();Assert.AreEqual(0,executions);
            Assert.AreEqual(window.Monitoring.Labels["ProcessTargetUnavailable"],window.Monitoring.ActionStatus);
            window.Monitoring.ClearSearch();Select(0);InvokeAction(window,false);Drain();Assert.AreEqual(0,executions);
            Assert.IsNull(window.Monitoring.SelectedProcess);
        }

        [Test]
        public void ConfirmedTargetChangedWhileDialogWasOpenNeverQueuesAction()
        {
            int executions=0;
            var window=CreateProcessWindow(delegate { Interlocked.Increment(ref executions);return new ProcessActionResult(ProcessActionOutcome.Terminated,string.Empty); });
            ShowProcesses(window);
            foreach(string change in new[]{"failure","exit","reuse","pending","filter","leave"})
            {
                Select(1);window.Monitoring.ClearSearch();ApplyProcesses(window,ProcessSnapshotForWindow());SelectProcess(window);
                string prompt=Confirm(window,true,delegate
                {
                    var snapshot=ProcessSnapshotForWindow();
                    if(change=="failure")ApplyProcesses(window,null,"controlled failure during confirmation");
                    else if(change=="exit") { snapshot.Processes.RemoveAt(0);ApplyProcesses(window,snapshot); }
                    else if(change=="reuse") { snapshot.Processes[0].CreationTime++;ApplyProcesses(window,snapshot); }
                    else if(change=="leave")Select(0);
                    else
                    {
                        window.Monitoring.SearchQuery="other";
                        if(change=="filter")Assert.IsTrue(Until(delegate { return window.Monitoring.ProcessesView.Count==1; }));
                    }
                });
                Assert.IsNotNull(prompt,change);var watch=Stopwatch.StartNew();
                Until(delegate { return executions!=0||watch.ElapsedMilliseconds>=80; });Assert.AreEqual(0,executions,change);
                Assert.AreEqual(window.Monitoring.Labels["ProcessTargetUnavailable"],window.Monitoring.ActionStatus,change);
                Assert.IsFalse(((Button)window.FindName("TrimSelected")).IsEnabled,change);
                Assert.IsFalse(((Button)window.FindName("TerminateSelected")).IsEnabled,change);
            }
        }

        [Test]
        public void ValidConfirmedActionKeepsCapturedIdentityAndRejectsConcurrentEntry()
        {
            int executions=0;ProcessSnapshot captured=null;bool wasTermination=false,wasConfirmed=false;
            var window=CreateProcessWindow(delegate(ProcessSnapshot target,bool terminate,bool confirmed)
            {
                captured=target;wasTermination=terminate;wasConfirmed=confirmed;Interlocked.Increment(ref executions);
                return new ProcessActionResult(ProcessActionOutcome.Terminated,string.Empty);
            });
            ShowProcesses(window);SelectProcess(window);
            string prompt=Confirm(window,true,delegate { SelectProcess(window,1);InvokeAction(window,false); });
            Assert.IsNotNull(prompt);Assert.IsTrue(Until(delegate { return window.Monitoring.ActionStatus.Contains(window.Monitoring.Labels["Terminated"]); }));
            Assert.AreEqual(1,executions);Assert.IsNotNull(captured);Assert.AreEqual(8001,captured.Id);Assert.AreEqual(1101,captured.CreationTime);
            Assert.IsTrue(wasTermination);Assert.IsTrue(wasConfirmed);Assert.IsTrue(((Button)window.FindName("TrimSelected")).IsEnabled);
        }

        [Test]
        public void TemperatureSettings_BindStatusAndCancelOfficialDriverConfirmation()
        {
            var window=CreateWindow();window.Show();Select(2);Drain();
            window.Width=window.MinWidth;window.Height=window.MinHeight;window.UpdateLayout();
            var status=window.FindName("TemperatureStatus") as TextBlock;
            var button=window.FindName("InstallTemperatureDriver") as Button;
            Assert.IsNotNull(status);Assert.IsNotNull(button);
            Assert.AreEqual(BindingStatus.Active,status.GetBindingExpression(TextBlock.TextProperty).Status);
            Assert.AreEqual(BindingStatus.Active,status.GetBindingExpression(FrameworkElement.ToolTipProperty).Status);
            Assert.AreEqual(BindingStatus.Active,button.GetBindingExpression(UIElement.IsEnabledProperty).Status);
            foreach(string culture in new[]{"zh-Hans","en","de"})
            {
                Localizer.Language=new Language(System.Globalization.CultureInfo.GetCultureInfo(culture));Drain();
                Assert.AreEqual(window.Monitoring.Labels["InstallSensorDriver"],button.Content);
                Assert.AreEqual(window.Monitoring.TemperatureStatusText,status.Text);
                Assert.IsTrue(button.IsEnabled);Assert.Greater(status.ActualWidth,80);
                foreach(string source in new[]{"DownloadSensorInstallerNote","LocalSensorInstallerNote"})
                {
                    string prompt=null;double promptWidth=0;var cancel=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
                    cancel.Tick+=delegate
                    {
                        var dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w is MessageDialog);
                        if(dialog==null)return;
                        cancel.Stop();Assert.AreSame(window,dialog.Owner);
                        prompt=(string)dialog.DataContext.GetType().GetProperty("Message").GetValue(dialog.DataContext,null);
                        dialog.UpdateLayout();promptWidth=dialog.ActualWidth;
                        dialog.DialogResult=false;
                    };
                    cancel.Start();
                    try
                    {
                        if(source=="DownloadSensorInstallerNote")button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else Assert.IsFalse((bool)typeof(MainWindow).GetMethod("ConfirmTemperatureDriver",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{source}));
                    }
                    finally { cancel.Stop(); }
                    Assert.AreEqual(window.Monitoring.Labels[source]+Environment.NewLine+Environment.NewLine+window.Monitoring.Labels["ConfirmSensorDriver"],prompt);
                    StringAssert.Contains("PawnIO 2.2.0",prompt);StringAssert.Contains("Secure Boot",prompt);
                    Assert.IsNull(typeof(MonitoringViewModel).GetField("_installer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window.Monitoring));
                    Assert.IsFalse(window.Monitoring.IsInstallingDriver);
                    Assert.LessOrEqual(promptWidth,Math.Min(600,SystemParameters.WorkArea.Width-32),"Driver confirmation must wrap into a readable width.");
                }
            }
            window.Close();Drain();Assert.IsFalse(window.Monitoring.CanInstallDriver);
            Assert.IsFalse(window.Monitoring.BeginDriverInstallation(true));
        }

        private static bool TemperaturePromptPending(MainWindow window)
        { return (int)typeof(MainWindow).GetField("_temperaturePromptPending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)!=0; }

        private static void ClickTemperatureToggle(MainWindow window,string name,bool enabled)
        {
            var toggle=(CheckBox)window.FindName(name);Assert.IsNotNull(toggle);
            toggle.IsChecked=enabled;toggle.GetBindingExpression(ToggleButton.IsCheckedProperty).UpdateSource();
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private static IList<string> ExerciseTemperaturePrompt(MainWindow window,Action trigger,bool confirmed,Action<MessageDialog> inspect=null)
        {
            var prompts=new List<string>();Exception failure=null;
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) };
            EventHandler tick=delegate
            {
                var dialog=Application.Current.Windows.Cast<Window>().OfType<MessageDialog>().FirstOrDefault(w=>w.Owner==window);
                if(dialog==null||!dialog.IsVisible||!dialog.IsLoaded)return;
                try
                {
                    prompts.Add(((MessageViewModel)dialog.DataContext).Message);
                    if(inspect!=null)inspect(dialog);
                }
                catch(Exception error) { failure=error; }
                finally { if(dialog.IsVisible)dialog.DialogResult=failure==null&&confirmed; }
            };
            timer.Tick+=tick;timer.Start();
            try
            {
                trigger();Assert.IsTrue(Until(delegate { return !TemperaturePromptPending(window); },6000),"The explicit-action driver check must finish.");
                Drain();
            }
            finally
            {
                timer.Stop();timer.Tick-=tick;
                foreach(var dialog in Application.Current.Windows.Cast<Window>().OfType<MessageDialog>().Where(w=>w.Owner==window).ToArray())dialog.Close();
            }
            Assert.IsNull(failure,"Controlled dialog inspection failed: "+failure);return prompts;
        }

        private static void InvalidateTemperaturePrompt(MainWindow window,string reason)
        {
            switch(reason)
            {
                case "uncheck": ClickTemperatureToggle(window,"CpuTemperatureToggle",false);break;
                case "page": window.SelectPage(0);break;
                case "hide": window.Hide();break;
                case "minimize": window.WindowState=WindowState.Minimized;break;
                case "close": window.Close();break;
                case "dispose": window.Dispose();break;
                default: Assert.Fail("Unknown controlled prompt invalidation.");break;
            }
        }

        [TestCase("CpuTemperatureToggle",false,"zh-Hans")]
        [TestCase("CpuTemperatureToggle",true,"zh-Hans")]
        [TestCase("CpuTemperatureToggle",false,"en")]
        [TestCase("CpuTemperatureToggle",true,"de")]
        [TestCase("OverlayToggle",false,"zh-Hans")]
        [TestCase("OverlayToggle",true,"en")]
        public void TemperatureEnable_MissingDriverConfirmsOnceAndPreservesDisplayChoice(string trigger,bool confirmed,string culture)
        {
            int checks=0,installs=0,inspectionThread=0;
            var window=CreateWindow(inspectDriver:delegate
            {
                inspectionThread=Thread.CurrentThread.ManagedThreadId;Interlocked.Increment(ref checks);
                return new SensorDriverState(false,false,null,"NotInstalled");
            },beginDriver:delegate(bool consent,string path) { Assert.IsTrue(consent);Assert.IsNull(path);installs++;return true; });
            window.Show();Select(2);window.Width=window.MinWidth;window.Height=window.MinHeight;
            if(trigger=="OverlayToggle")window.Monitoring.ShowCpuTemperature=true;
            Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));Drain();
            var prompts=ExerciseTemperaturePrompt(window,delegate
            {
                ClickTemperatureToggle(window,trigger,true);ClickTemperatureToggle(window,trigger,true);
            },confirmed,delegate(MessageDialog dialog)
            {
                Assert.AreSame(window,dialog.Owner);Assert.AreEqual(window.Monitoring.Labels["ConfirmSensorDriverTitle"],dialog.Title);
                dialog.UpdateLayout();Assert.Greater(dialog.ActualWidth,200);
                Assert.LessOrEqual(dialog.ActualWidth,Math.Min(600,SystemParameters.WorkArea.Width-32));
                Assert.LessOrEqual(dialog.ActualHeight,SystemParameters.WorkArea.Height);
            });
            Assert.AreEqual(1,checks);Assert.AreNotEqual(Thread.CurrentThread.ManagedThreadId,inspectionThread,"Trust inspection must not block the UI thread.");
            Assert.AreEqual(1,prompts.Count);Assert.AreEqual(confirmed?1:0,installs);
            Assert.AreEqual(window.Monitoring.Labels["TemperatureDriverNeeded"]+Environment.NewLine+Environment.NewLine+window.Monitoring.Labels["DownloadSensorInstallerNote"]+Environment.NewLine+Environment.NewLine+window.Monitoring.Labels["ConfirmSensorDriver"],prompts[0]);
            StringAssert.Contains("PawnIO 2.2.0",prompts[0]);Assert.IsTrue(window.Monitoring.ShowCpuTemperature);
            if(trigger=="OverlayToggle")Assert.IsTrue(window.Monitoring.OverlayEnabled);
            Assert.IsNull(typeof(MonitoringViewModel).GetField("_installer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window.Monitoring),"Only the fake installer delegate may be called.");
        }

        [TestCase("AccessDenied","TemperatureDriverAdministrator","zh-Hans")]
        [TestCase("AccessDenied","TemperatureDriverAdministrator","en")]
        [TestCase("DriverNotReady","TemperatureDriverNotReady","zh-Hans")]
        [TestCase("UnverifiedDriver","TemperatureDriverUnverified","de")]
        [TestCase("KeepExistingDriver","TemperatureDriverUnverified","en")]
        [TestCase("Unknown","TemperatureDriverInspectionFailed","zh-Hans")]
        [TestCase("NotInstalled","TemperatureDriverInspectionFailed","en")]
        public void TemperatureEnable_ExistingOrUncertainDriverOnlyExplainsStatus(string status,string messageKey,string culture)
        {
            int installs=0;
            var window=CreateWindow(inspectDriver:delegate { return new SensorDriverState(false,true,null,status); },
                beginDriver:delegate { installs++;return true; });
            window.Show();Select(2);Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));Drain();
            var prompts=ExerciseTemperaturePrompt(window,delegate { ClickTemperatureToggle(window,"CpuTemperatureToggle",true); },true,delegate(MessageDialog dialog)
            {
                var model=(MessageViewModel)dialog.DataContext;
                Assert.AreEqual(window.Monitoring.Labels["TemperatureDriverTitle"],dialog.Title);
                Assert.AreEqual(window.Monitoring.Labels["TemperatureDriverAcknowledge"],model.RightButton.Key);
                Assert.IsNull(model.LeftButton.Value,"An explanatory notice must not offer installation.");
            });
            Assert.AreEqual(1,prompts.Count);Assert.AreEqual(window.Monitoring.Labels[messageKey],prompts[0]);Assert.AreEqual(0,installs);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TemperatureEnable_FailedInspectionNeverStartsInstallation(bool throws)
        {
            int installs=0;
            var window=CreateWindow(inspectDriver:delegate { if(throws)throw new InvalidOperationException("Controlled inspection failure");return null; },
                beginDriver:delegate { installs++;return true; });
            window.Show();Select(2);Drain();
            var prompts=ExerciseTemperaturePrompt(window,delegate { ClickTemperatureToggle(window,"CpuTemperatureToggle",true); },true);
            Assert.AreEqual(1,prompts.Count);Assert.AreEqual(window.Monitoring.Labels["TemperatureDriverInspectionFailed"],prompts[0]);Assert.AreEqual(0,installs);
        }

        [Test]
        public void TemperatureEnable_ReadyDriverDoesNotPromptOrInstall()
        {
            int checks=0,installs=0;
            var window=CreateWindow(inspectDriver:delegate { checks++;return new SensorDriverState(true,true,new Version(2,2,0),"Ready"); },
                beginDriver:delegate { installs++;return true; });
            window.Show();Select(2);Drain();
            var prompts=ExerciseTemperaturePrompt(window,delegate { ClickTemperatureToggle(window,"CpuTemperatureToggle",true); },true);
            Assert.AreEqual(1,checks);Assert.AreEqual(0,prompts.Count);Assert.AreEqual(0,installs);
        }

        [Test]
        public void TemperatureEnable_StartupRefreshAndNonEnablingActionsNeverInspectOrInstall()
        {
            int checks=0,installs=0;
            var window=CreateWindow(true,inspectDriver:delegate { checks++;return new SensorDriverState(false,false,null,"NotInstalled"); },
                beginDriver:delegate { installs++;return true; });
            var prompts=ExerciseTemperaturePrompt(window,delegate
            {
                window.Show();Select(2);Drain();window.Monitoring.RefreshNow();
                window.Monitoring.ShowCpuTemperature=false;window.Monitoring.ShowCpuTemperature=true;Drain();
                ClickTemperatureToggle(window,"CpuTemperatureToggle",false);
                ClickTemperatureToggle(window,"OverlayToggle",false);ClickTemperatureToggle(window,"OverlayToggle",true);
                window.Hide();ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
                window.Show();window.WindowState=WindowState.Minimized;ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
                window.WindowState=WindowState.Normal;Select(0);ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
                window.Close();ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
            },true);
            Assert.AreEqual(0,checks);Assert.AreEqual(0,installs);Assert.AreEqual(0,prompts.Count);
        }

        [TestCase("uncheck")]
        [TestCase("page")]
        [TestCase("hide")]
        [TestCase("minimize")]
        [TestCase("close")]
        [TestCase("dispose")]
        public void TemperatureEnable_LateInspectionCannotPromptAfterUserLeaves(string reason)
        {
            int checks=0,installs=0;
            using(var inspecting=new ManualResetEvent(false))
            using(var release=new ManualResetEvent(false))
            {
                var window=CreateWindow(inspectDriver:delegate
                {
                    Interlocked.Increment(ref checks);inspecting.Set();release.WaitOne(5000);
                    return new SensorDriverState(false,false,null,"NotInstalled");
                },beginDriver:delegate { installs++;return true; });
                window.Show();Select(2);Drain();
                var prompts=ExerciseTemperaturePrompt(window,delegate
                {
                    ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
                    try
                    {
                        Assert.IsTrue(inspecting.WaitOne(2000));ClickTemperatureToggle(window,"CpuTemperatureToggle",true);
                        InvalidateTemperaturePrompt(window,reason);
                    }
                    finally { release.Set(); }
                },true);
                Assert.AreEqual(1,checks);Assert.AreEqual(0,installs);Assert.AreEqual(0,prompts.Count);
            }
        }

        [TestCase("uncheck")]
        [TestCase("page")]
        [TestCase("close")]
        public void TemperatureEnable_ConfirmationRechecksCurrentWindowState(string reason)
        {
            int installs=0;
            var window=CreateWindow(inspectDriver:delegate { return new SensorDriverState(false,false,null,"NotInstalled"); },
                beginDriver:delegate { installs++;return true; });
            window.Show();Select(2);Drain();
            var prompts=ExerciseTemperaturePrompt(window,delegate { ClickTemperatureToggle(window,"CpuTemperatureToggle",true); },true,
                delegate { InvalidateTemperaturePrompt(window,reason); });
            Assert.AreEqual(1,prompts.Count);Assert.AreEqual(0,installs);
        }

        [TestCase("zh-Hans")]
        [TestCase("en")]
        [TestCase("de")]
        public void TemperatureInstallerChoicesRemainBoundAndFitAtMinimumSize(string culture)
        {
            var window=CreateWindow();window.Show();Select(2);Drain();
            window.Width=window.MinWidth;window.Height=window.MinHeight;
            Localizer.Language=new Language(CultureInfo.GetCultureInfo(culture));Drain();window.UpdateLayout();
            var buttons=new[]{(Button)window.FindName("InstallTemperatureDriver"),(Button)window.FindName("SelectTemperatureDriverInstaller"),(Button)window.FindName("CancelTemperatureDriverPreparation")};
            string[] labels={"InstallSensorDriver","SelectSensorInstaller","CancelSensorPreparation"};
            var panel=buttons[0].Parent as WrapPanel;Assert.IsNotNull(panel);
            var bounds=new List<Rect>();
            for(int i=0;i<buttons.Length;i++)
            {
                Button button=buttons[i];Assert.IsNotNull(button);Assert.AreSame(panel,button.Parent);
                Assert.AreEqual(window.Monitoring.Labels[labels[i]],button.Content);
                Assert.AreEqual(BindingStatus.Active,button.GetBindingExpression(ContentControl.ContentProperty).Status);
                Assert.AreEqual(BindingStatus.Active,button.GetBindingExpression(UIElement.IsEnabledProperty).Status);
                Assert.AreEqual(i!=2,button.IsEnabled);
                Rect rect=button.TransformToAncestor(panel).TransformBounds(new Rect(new Point(),button.RenderSize));
                Assert.Greater(rect.Width,40);Assert.Greater(rect.Height,20);
                Assert.GreaterOrEqual(rect.Left,-1);Assert.LessOrEqual(rect.Right,panel.ActualWidth+1);
                foreach(Rect earlier in bounds){Rect intersection=Rect.Intersect(earlier,rect);Assert.IsTrue(intersection.IsEmpty||intersection.Width<=0||intersection.Height<=0,"Installer buttons must not overlap.");}
                bounds.Add(rect);
            }
            Assert.IsFalse(window.Monitoring.CanCancelDriverPreparation);
            buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsNull(typeof(MonitoringViewModel).GetField("_installer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window.Monitoring));
            Assert.AreEqual(1,Descendants<TextBlock>(window).Count(t=>t.Text==window.Monitoring.Labels["SensorInstallerSourceNote"]));
            if(Environment.GetEnvironmentVariable("WMC_CAPTURE_INSTALLER_SETTINGS")=="1")
            {
                var settings=(ScrollViewer)window.FindName("SettingsPage");
                settings.ScrollToVerticalOffset(Math.Max(0,panel.TransformToAncestor((FrameworkElement)settings.Content).Transform(new Point()).Y-48));
                Drain();window.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(TestContext.CurrentContext.WorkDirectory,"installer-settings-"+culture+".png")))encoder.Save(stream);
            }
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);var match=child as T;
                if(match!=null)yield return match;
                foreach(var nested in Descendants<T>(child))yield return nested;
            }
        }
        private static void AssertThemedBar(ProgressBar bar,Brush track,Brush accent)
        {
            bar.ApplyTemplate();
            var trackPart=bar.Template.FindName("PART_Track",bar) as Border;var indicator=bar.Template.FindName("PART_Indicator",bar) as Border;
            Assert.IsNotNull(trackPart,bar.Name+" must draw a themed track instead of the light system track.");Assert.IsNotNull(indicator,bar.Name);
            Assert.AreSame(track,trackPart.Background,bar.Name);Assert.AreSame(accent,indicator.Background,bar.Name);
        }

        [Test]
        public void OptimizationProgressReplacesTheSummaryAndCountsElapsedTime()
        {
            var window=CreateWindow();window.Show();Drain();window.UpdateLayout();
            var cleaner=(MainViewModel)window.DataContext;var optimize=(Button)window.FindName("Optimize");
            var panel=window.FindName("OptimizationProgressPanel") as FrameworkElement;var summary=window.FindName("MemorySummary") as FrameworkElement;
            Assert.IsNotNull(panel,"A running optimization must show its progress, not only a disabled button.");Assert.IsNotNull(summary);
            var step=(TextBlock)window.FindName("OptimizationStep");var count=(TextBlock)window.FindName("OptimizationCount");
            var elapsed=(TextBlock)window.FindName("OptimizationElapsed");var bar=(ProgressBar)window.FindName("OptimizationProgressBar");
            Assert.AreEqual(Visibility.Collapsed,panel.Visibility);Assert.AreEqual(Visibility.Visible,summary.Visibility);
            double footer=((FrameworkElement)optimize.Parent).ActualHeight;
            string start=string.Format(Localizer.Culture,window.Monitoring.Labels["OptimizationElapsed"],"0:00");
            try
            {
                cleaner.OptimizationProgressTotal=8;cleaner.OptimizationProgressValue=3;cleaner.OptimizationProgressStep=Localizer.String.WorkingSet;
                cleaner.IsOptimizationRunning=true;Drain();window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible,panel.Visibility);Assert.AreEqual(Visibility.Hidden,summary.Visibility,"Hidden keeps the footer height stable.");
                Assert.AreEqual(Localizer.String.WorkingSet,step.Text);Assert.AreEqual("3 / 8",count.Text);
                Assert.AreEqual(8.0,bar.Maximum);Assert.AreEqual(3.0,bar.Value);
                Assert.AreEqual(footer,((FrameworkElement)optimize.Parent).ActualHeight,0.5,"Showing progress must not resize the footer.");
                Assert.IsTrue(optimize.IsVisible,"The disabled optimize button stays in place.");
                Assert.AreEqual(start,elapsed.Text);
                Assert.IsTrue(Until(delegate { return elapsed.Text!=start; },3000),"Elapsed time must advance while the cleaner works.");
                StringAssert.Contains("0:0",elapsed.Text);
                cleaner.CompactMode=true;Drain();window.UpdateLayout();
                Assert.Less(window.ActualHeight,350);Assert.AreEqual(Visibility.Visible,panel.Visibility);Assert.Greater(panel.ActualWidth,200);
            }
            finally { cleaner.IsOptimizationRunning=false;cleaner.CompactMode=false;Drain(); }
            Assert.AreEqual(Visibility.Collapsed,panel.Visibility);Assert.AreEqual(Visibility.Visible,summary.Visibility);
            var clock=typeof(MainWindow).GetField("_optimizationClock",BindingFlags.Instance|BindingFlags.NonPublic);Assert.IsNotNull(clock);
            Assert.IsFalse(((DispatcherTimer)clock.GetValue(window)).IsEnabled,"The elapsed clock must stop with the optimization.");
        }

        [Test]
        public void ThemedProgressAndSearchUseThePaletteAndShowTheSearchHint()
        {
            var window=CreateProcessWindow();ShowProcesses(window);
            Brush track=Brushes.Gray,accent=Brushes.DeepSkyBlue,surface=Brushes.DimGray;
            Application.Current.Resources["SecondaryBorder"]=track;Application.Current.Resources["Accent"]=accent;
            Application.Current.Resources["SecondaryBackground"]=surface;Drain();
            var search=(TextBox)window.FindName("ProcessSearch");
            Assert.AreSame(surface,search.Background,"The search box must follow the theme instead of staying a white system box.");
            Assert.AreSame(track,search.BorderBrush);
            search.ApplyTemplate();var hint=search.Template.FindName("SearchHint",search) as TextBlock;
            Assert.IsNotNull(hint,"An empty search box must say what it searches.");
            Assert.AreEqual(window.Monitoring.Labels["Search"],hint.Text);Assert.AreEqual(Visibility.Visible,hint.Visibility);
            search.UpdateLayout();var hintAt=hint.TranslatePoint(new Point(0,hint.ActualHeight/2),search);
            search.Text="game";Drain();Assert.AreEqual(Visibility.Collapsed,hint.Visibility,"Typed text replaces the hint.");
            search.UpdateLayout();var typed=search.GetRectFromCharacterIndex(0);
            Assert.AreEqual(typed.Left,hintAt.X,3.0,"The hint must start where typed text starts.");
            Assert.AreEqual(typed.Top+typed.Height/2,hintAt.Y,3.0,"The hint must be vertically aligned with typed text.");
            search.Text=string.Empty;Drain();Assert.AreEqual(Visibility.Visible,hint.Visibility);
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();
            Assert.AreEqual("Search name or PID",hint.Text);
            Select(0);Drain();window.UpdateLayout();
            var memory=window.FindName("MemoryUsageBar") as ProgressBar;Assert.IsNotNull(memory);
            AssertThemedBar(memory,track,accent);
            var cleaner=(MainViewModel)window.DataContext;
            try
            {
                cleaner.IsOptimizationRunning=true;Drain();window.UpdateLayout();
                AssertThemedBar((ProgressBar)window.FindName("OptimizationProgressBar"),track,accent);
            }
            finally { cleaner.IsOptimizationRunning=false;Drain(); }
        }

        [Test]
        public void NumericHeadersAlignWithTheirRightAlignedValues()
        {
            var window=CreateProcessWindow();ShowProcesses(window);window.UpdateLayout();
            var headers=Descendants<DataGridColumnHeader>(window).Where(h=>h.Column!=null).ToList();
            Assert.AreEqual(7,headers.Count,"Every process column fits at the default width.");
            foreach(var header in headers)
            {
                bool numeric=header.Column.DisplayIndex>0;string name=header.Column.Header as string;
                Assert.AreEqual(numeric?HorizontalAlignment.Right:HorizontalAlignment.Left,header.HorizontalContentAlignment,name);
                if(!numeric)continue;
                var content=Descendants<ContentPresenter>(header).FirstOrDefault();Assert.IsNotNull(content,name);
                double right=content.TranslatePoint(new Point(content.ActualWidth,0),header).X;
                Assert.AreEqual(header.ActualWidth-header.Padding.Right,right,2.0,name+" must end where its right-aligned values end.");
            }
        }

        [Test]
        public void SettingsGroupsAreThemedCardsLikeTheOverlayCard()
        {
            var window=CreateWindow();Brush surface=Brushes.DimGray;Application.Current.Resources["SecondaryBackground"]=surface;
            Select(2);window.Show();Drain();window.UpdateLayout();
            var groups=Descendants<GroupBox>((DependencyObject)window.FindName("SettingsHost")).ToList();
            Assert.AreEqual(6,groups.Count,"All legacy settings groups stay present.");
            foreach(var group in groups)
            {
                string title=group.Header as string;group.ApplyTemplate();
                var frames=Descendants<Border>(group).Where(b=>b.TemplatedParent==group).ToList();
                Assert.AreEqual(1,frames.Count,title+" must be one flat card, not the double-line system frame.");
                Assert.AreSame(surface,frames[0].Background,title);Assert.AreEqual(new Thickness(0),frames[0].BorderThickness,title);
                if(!group.IsVisible)continue;
                var header=Descendants<TextBlock>(group).FirstOrDefault(t=>t.Text==title);Assert.IsNotNull(header,title);
                Assert.AreEqual(18.0,header.FontSize,title);Assert.AreEqual(FontWeights.SemiBold,header.FontWeight,title);
            }
        }

        [Test]
        public void DisplayPickerNamesThePrimaryDefaultAndKeepsAChosenScreen()
        {
            var window=CreateWindow();Select(2);window.Show();Drain();window.UpdateLayout();
            var picker=Descendants<ComboBox>(window).FirstOrDefault(c=>
            {
                var binding=BindingOperations.GetBinding(c,ItemsControl.ItemsSourceProperty);
                return binding!=null&&binding.Path!=null&&binding.Path.Path=="Displays";
            });
            Assert.IsNotNull(picker);var initial=picker.SelectedItem as DisplayChoice;
            string primary=System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
            window.Monitoring.OverlayScreen=primary;Drain();
            Assert.AreEqual(primary,SelectedDisplay(picker));Assert.AreEqual(primary,Settings.Monitoring.Screen);
            window.Monitoring.RefreshDisplays();Drain();string afterRefresh=SelectedDisplay(picker);
            Assert.AreEqual(primary,Settings.Monitoring.Screen,"Refreshing the list must not save the primary-display default (selection: "+(afterRefresh??"none")+").");
            Assert.AreEqual(primary,afterRefresh,"Refreshing the drop-down list must keep the chosen display.");
            window.Monitoring.OverlayScreen=string.Empty;Drain();
            var choice=picker.SelectedItem as DisplayChoice;
            Assert.IsNotNull(choice,"The saved primary-display default must be a visible choice, not a blank box.");
            Assert.AreEqual(string.Empty,choice.Id);Assert.AreEqual(window.Monitoring.Labels["PrimaryDisplay"],choice.Name);
            Assert.AreSame(initial,choice,"The default is selected from the first frame.");
            string chinese=choice.Name;
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();
            Assert.AreSame(choice,picker.SelectedItem);Assert.AreEqual("Primary display (default)",choice.Name);
            Assert.AreNotEqual(chinese,choice.Name);Assert.AreEqual(string.Empty,Settings.Monitoring.Screen);
            window.Monitoring.RefreshDisplays();Drain();Assert.AreSame(choice,picker.SelectedItem);
        }
        private static string SelectedDisplay(ComboBox picker)
        {
            var choice=picker.SelectedItem as DisplayChoice;return choice==null?null:choice.Id;
        }

        [Test]
        public void ReleaseLayout_ScrollContentKeepsAGutterAtTheMinimum()
        {
            var window=CreateProcessWindow();window.Show();window.Width=window.MinWidth;window.Height=window.MinHeight;
            foreach(int page in new[]{0,2})
            {
                Select(page);Drain();window.UpdateLayout();
                var scroll=(ScrollViewer)window.FindName(page==0?"OverviewPage":"SettingsPage");
                var content=(FrameworkElement)scroll.Content;
                Assert.GreaterOrEqual(content.Margin.Right,10,"Cards need a gutter before the scrollbar.");
            }
        }

        [Test]
        public void ReleaseLayout_CompactSummaryShowsCurrentMemoryWithoutRepeatingExpandedCard()
        {
            var window=CreateProcessWindow();window.Show();Drain();var cleaner=(MainViewModel)window.DataContext;
            var usage=window.FindName("CompactMemoryUsage") as TextBlock;
            Assert.IsNotNull(usage,"Compact mode must show usage, not only installed capacity.");
            Assert.AreEqual(Visibility.Collapsed,usage.Visibility,"Expanded overview already contains the memory card.");
            cleaner.CompactMode=true;
            cleaner.Computer.Memory=new Memory(new Structs.Windows.MemoryStatusEx { TotalPhys=16L*1024*1024*1024,AvailPhys=6L*1024*1024*1024,MemoryLoad=62 });
            Drain();window.UpdateLayout();StringAssert.Contains("62%",usage.Text);Assert.IsTrue(usage.IsVisible);
            cleaner.Computer.Memory=new Memory(new Structs.Windows.MemoryStatusEx { TotalPhys=16L*1024*1024*1024,AvailPhys=4L*1024*1024*1024,MemoryLoad=75 });
            Drain();StringAssert.Contains("75%",usage.Text);
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();
            StringAssert.Contains("Physical memory",usage.Text);Assert.Less(window.ActualHeight,350);
        }

        [Test]
        public void ReleaseLayout_EmptyActionFeedbackDoesNotReserveSpace()
        {
            var window=CreateProcessWindow();ShowProcesses(window);
            var status=Descendants<TextBlock>(window).Single(t=>
            {
                var binding=BindingOperations.GetBinding(t,TextBlock.TextProperty);
                return binding!=null&&binding.Path!=null&&binding.Path.Path=="ActionStatus";
            });
            Assert.AreEqual(Visibility.Collapsed,status.Visibility);
            window.Monitoring.ActionStatus="Controlled feedback";Drain();Assert.AreEqual(Visibility.Visible,status.Visibility);
            window.Monitoring.ActionStatus=string.Empty;Drain();Assert.AreEqual(Visibility.Collapsed,status.Visibility);
        }

        [Test]
        public void ReleaseLayout_EnglishNumericHeadersWrapWithoutClipping()
        {
            var window=CreateProcessWindow();ShowProcesses(window);window.Width=window.MinWidth;window.Height=window.MinHeight;
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();window.UpdateLayout();
            var headers=Descendants<DataGridColumnHeader>(window).Where(h=>h.Column!=null&&h.Column.DisplayIndex>0).ToList();
            foreach(var header in headers)
            {
                var label=Descendants<TextBlock>(header).FirstOrDefault(t=>t.Text==(string)header.Column.Header);
                Assert.IsNotNull(label);Assert.AreEqual(TextWrapping.Wrap,label.TextWrapping,label.Text);
                var edge=label.TranslatePoint(new Point(label.ActualWidth,0),header);
                Assert.LessOrEqual(edge.X,header.ActualWidth-header.Padding.Right+1,label.Text);
            }
            Assert.Greater(((DataGrid)window.FindName("ProcessGrid")).ActualHeight,70);
        }

        [Test]
        public void ReleaseLayout_CompactToggleUsesAContrastingThemedOutline()
        {
            var window=CreateProcessWindow();window.Show();
            Application.Current.Resources["SecondaryDisabled"]=Brushes.DimGray;Drain();
            var toggle=Descendants<CheckBox>(window).Single(c=>
            {
                var binding=BindingOperations.GetBinding(c,ToggleButton.IsCheckedProperty);
                return binding!=null&&binding.Path!=null&&binding.Path.Path=="CompactMode";
            });
            Assert.AreSame(Brushes.DimGray,toggle.BorderBrush,"The compact toggle outline must remain visible in the light theme.");
        }

        [Test]
        public void ReleaseLayout_CancelledActionFollowsLanguageChanges()
        {
            var window=CreateProcessWindow();ShowProcesses(window);SelectProcess(window);Confirm(window,false);
            Assert.AreEqual(window.Monitoring.Labels["NotConfirmed"],window.Monitoring.ActionStatus);
            Localizer.Language=new Language(CultureInfo.GetCultureInfo("en"));Drain();
            Assert.AreEqual(window.Monitoring.Labels["NotConfirmed"],window.Monitoring.ActionStatus);
        }

        [Test]
        public void ReleaseLayout_OptimizationStateImmediatelyNotifiesAvailability()
        {
            var window=CreateProcessWindow();window.Show();Drain();var cleaner=(MainViewModel)window.DataContext;
            int changes=0;cleaner.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e) { if(e.PropertyName=="CanOptimize")changes++; };
            var button=(Button)window.FindName("Optimize");Assert.IsTrue(button.IsEnabled);
            try
            {
                cleaner.IsOptimizationRunning=true;Drain();Assert.Greater(changes,0,"The worker state must notify command availability.");
                Assert.IsFalse(button.IsEnabled);
            }
            finally { cleaner.IsOptimizationRunning=false;Drain(); }
            Assert.IsTrue(button.IsEnabled);
        }
    }
}
#pragma warning restore 1591
