using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinMemoryCleaner
{
    /// <summary>Cleaner shell with independent, demand-driven resource monitoring.</summary>
    public partial class MainWindow : View, IDisposable
    {
        private const double ExpandedHeight = 700;
        private const double ExpandedWidth = 960;
        private const double ExpandedMinHeight = 520;
        private const double ExpandedMinWidth = 760;
        private readonly MainViewModel _viewModel;
        private MonitoringOverlay _overlay;
        private SettingsPanel _settings;
        private bool? _compactModeApplied;
        private bool _closed;
        private bool _disposed;
        private bool _actionRunning;
        private readonly Func<ProcessSnapshot,bool,bool,ProcessActionResult> _executeAction;
        private int _page;
        private double _lastExpandedHeight = ExpandedHeight;
        private double _lastExpandedWidth = ExpandedWidth;
        private DispatcherTimer _optimizationClock;
        private readonly System.Diagnostics.Stopwatch _optimizationWatch = new System.Diagnostics.Stopwatch();

        /// <summary>The shared monitoring presentation used by the shell and overlay.</summary>
        public MonitoringViewModel Monitoring { get; private set; }

        /// <summary>Creates the shell without starting detailed sampling until it is visible.</summary>
        public MainWindow() : this(delegate { return new ResourceMonitor(); }) { }
        internal MainWindow(Func<IResourceMonitor> createMonitor)
            : this(createMonitor,delegate(ProcessSnapshot target,bool terminate,bool confirmed) { return new ProcessActionService().Execute(target,terminate,confirmed); }) { }
        internal MainWindow(Func<IResourceMonitor> createMonitor,Func<ProcessSnapshot,bool,bool,ProcessActionResult> executeAction)
        {
            if(executeAction==null)throw new ArgumentNullException("executeAction");
            _executeAction=executeAction;
            var locator = (ViewModelLocator)FindResource("ViewModelLocator");
            _viewModel = locator.MainViewModel;
            Monitoring = new MonitoringViewModel(_viewModel, Dispatcher, createMonitor);
            InitializeComponent();
            Loaded += OnMainWindowLoaded;
            Closed += OnMainWindowClosed;
            IsVisibleChanged += OnWindowVisibleChanged;
            StateChanged += OnWindowStateChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.OnLanguageChangeCompleted += OnLanguageChanged;
            _viewModel.OnNavigateUriCommandCompleted += OnNavigateUriCommandCompleted;
            _viewModel.OnOptimizeCommandCompleted += OnOptimizeCommandCompleted;
            Monitoring.PropertyChanged += OnMonitoringChanged;
            Localizer.StaticPropertyChanged += OnLocalizerChanged;
            RefreshColumnHeaders();
            SelectPage(0);
            if (Monitoring.OverlayEnabled) { SyncOverlay(); Monitoring.RefreshNow(); }
            SyncOptimizationClock();
        }

        /// <summary>Selects overview (0), processes (1), or settings (2).</summary>
        /// <param name="page">The requested page.</param>
        public void SelectPage(int page)
        {
            if (OverviewPage == null || ProcessPage == null || SettingsPage == null) return;
            _page = page >= 0 && page <= 2 ? page : 0;
            OverviewPage.Visibility = _page == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProcessPage.Visibility = _page == 1 ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = _page == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (_page == 2 && _settings == null)
            {
                _settings = new SettingsPanel { DataContext = _viewModel };
                SettingsHost.Content = _settings;
            }
            if (_page == 0) OverviewNav.IsChecked = true;
            else if (_page == 1) ProcessNav.IsChecked = true;
            else SettingsNav.IsChecked = true;
            UpdateDemand();
        }

        private void UpdateDemand()
        {
            if (_closed || Monitoring == null) return;
            bool visibleConsumer = IsVisible && WindowState != WindowState.Minimized && !_viewModel.CompactMode && _page != 2;
            Monitoring.SetViewState(visibleConsumer, _page == 1);
        }
        private void OnPageChecked(object sender, RoutedEventArgs e)
        {
            var button = sender as RadioButton;
            int page;
            if (button != null && int.TryParse(Convert.ToString(button.Tag, System.Globalization.CultureInfo.InvariantCulture), out page)) SelectPage(page);
        }
        private void OnRefreshClick(object sender, RoutedEventArgs e) { Monitoring.RefreshNow(); }
        private void OnClearProcessSearch(object sender,RoutedEventArgs e)
        {
            Monitoring.ClearSearch();ProcessSearch.Focus();
        }
        private void OnProcessSearchKeyDown(object sender,KeyEventArgs e)
        {
            if(e.Key!=Key.Escape)return;
            OnClearProcessSearch(sender,e);e.Handled=true;
        }
        private void OnInstallTemperatureDriver(object sender,RoutedEventArgs e)
        {
            if(_closed||!Monitoring.CanInstallDriver)return;
            var dialog=new MessageDialog(this,Monitoring.Labels["ConfirmSensorDriver"],Enums.Dialog.Button.No,Enums.Dialog.Button.Yes) { Title=Monitoring.Labels["ConfirmSensorDriverTitle"],MaxWidth=Math.Min(600,SystemParameters.WorkArea.Width-32) };
            Monitoring.BeginDriverInstallation(dialog.ShowDialog()==true);
        }
        private void OnProcessSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var direction = Monitoring.Sort(e.Column.SortMemberPath);
            foreach (var column in ProcessGrid.Columns) column.SortDirection = null;
            e.Column.SortDirection = direction;
        }
        private void RefreshColumnHeaders()
        {
            if (ProcessGrid == null) return;
            string[] keys = { "Name", "Pid", "WorkingSet", "Private", "CpuColumn", "GpuColumn", "GpuMemory" };
            for (int i = 0; i < keys.Length; i++) ProcessGrid.Columns[i].Header = Monitoring.Labels[keys[i]];
        }
        private void OnTrimClick(object sender, RoutedEventArgs e) { ExecuteSelected(false); }
        private void OnTerminateClick(object sender, RoutedEventArgs e) { ExecuteSelected(true); }
        private void ExecuteSelected(bool terminate)
        {
            if(_closed||_disposed||_actionRunning)return;
            var selected=Monitoring.SelectedProcess;
            if(selected==null) { Monitoring.ActionStatus=Monitoring.Labels["NoSelection"];return; }
            // Capture identity before confirmation. UI checks supplement the service's retained-handle checks.
            var target=selected.Snapshot;
            if(!Monitoring.CanActOnSelectedProcess||!Monitoring.IsCurrentProcessTarget(target))
            { Monitoring.ActionStatus=Monitoring.Labels["ProcessTargetUnavailable"];return; }
            SetActionRunning(true);bool queued=false;
            try
            {
                if(terminate)
                {
                    var prompt=string.Format(Localizer.Culture,Monitoring.Labels["Confirm"],target.Name,target.Id);
                    var dialog=new MessageDialog(this,prompt,Enums.Dialog.Button.No,Enums.Dialog.Button.Yes) { Title=Monitoring.Labels["ConfirmTitle"] };
                    if(dialog.ShowDialog()!=true) { Monitoring.ActionStatus=Monitoring.Labels["NotConfirmed"];return; }
                }
                if(_closed||_disposed||!Monitoring.IsCurrentProcessTarget(target))
                { Monitoring.ActionStatus=Monitoring.Labels["ProcessTargetUnavailable"];return; }
                queued=ThreadPool.QueueUserWorkItem(delegate
                {
                    ProcessActionResult result;
                    try { result=_executeAction(target,terminate,terminate)??new ProcessActionResult(ProcessActionOutcome.Failed,"The action returned no result"); }
                    catch(Exception error) { result=new ProcessActionResult(ProcessActionOutcome.Failed,error.Message); }
                    if(_closed||Dispatcher.HasShutdownStarted)return;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if(_closed||_disposed)return;
                            SetActionRunning(false);
                            string key=result.Outcome.ToString();
                            Monitoring.ActionStatus=target.Name+" (PID "+target.Id+") — "+Monitoring.Labels[key];
                            if(result.Outcome==ProcessActionOutcome.Failed||result.Outcome==ProcessActionOutcome.AccessDenied)
                                Monitoring.ActionStatus+="："+result.Detail;
                            Monitoring.RefreshNow();
                        }),DispatcherPriority.Background);
                    }
                    catch(InvalidOperationException) { } // A closing Dispatcher cannot accept completion work.
                });
            }
            finally { if(!queued)SetActionRunning(false); }
        }
        private void SetActionRunning(bool running)
        {
            _actionRunning=running;
            if(running) { TrimSelected.IsEnabled=false;TerminateSelected.IsEnabled=false; }
            else { TrimSelected.ClearValue(IsEnabledProperty);TerminateSelected.ClearValue(IsEnabledProperty); }
        }
        private void OnMonitoringChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "OverlayEnabled") SyncOverlay();
        }
        private void SyncOverlay()
        {
            if (_closed) return;
            if (Monitoring.OverlayEnabled)
            {
                if (_overlay == null) _overlay = new MonitoringOverlay(Monitoring);
                if (!_overlay.IsVisible) _overlay.Show();
            }
            else if (_overlay != null) _overlay.Hide();
        }
        private void OnDragOverlay(object sender, RoutedEventArgs e)
        {
            Monitoring.OverlayEnabled = true;
            SyncOverlay();
            _overlay.BeginReposition();
        }
        private void OnMonitorOnlyClick(object sender, RoutedEventArgs e)
        {
            Monitoring.OverlayEnabled = true;
            SyncOverlay();
            CloseToTheNotificationArea();
        }
        private void OnDisplayDropDown(object sender, EventArgs e) { Monitoring.RefreshDisplays(); }

        private void ApplyCompactMode(bool compactMode)
        {
            if (_compactModeApplied == compactMode) return;
            if (compactMode)
            {
                if (_compactModeApplied.HasValue)
                {
                    var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(0, 0, ActualWidth, ActualHeight);
                    if (bounds.Width >= ExpandedMinWidth && bounds.Height >= ExpandedMinHeight)
                    { _lastExpandedWidth = bounds.Width; _lastExpandedHeight = bounds.Height; }
                }
                WindowState = WindowState.Normal;
                MinHeight = 0; MinWidth = 0;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.Height;
                Width = 640;
                WindowResizeGrip.Visibility = Visibility.Collapsed;
            }
            else
            {
                SizeToContent = SizeToContent.Manual;
                ResizeMode = ResizeMode.CanResizeWithGrip;
                MinHeight = ExpandedMinHeight; MinWidth = ExpandedMinWidth;
                Width = Math.Max(ExpandedMinWidth, _lastExpandedWidth);
                Height = Math.Max(ExpandedMinHeight, _lastExpandedHeight);
                WindowResizeGrip.Visibility = Visibility.Visible;
            }
            _compactModeApplied = compactMode;
            UpdateDemand();
        }
        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            ApplyCompactMode(_viewModel.CompactMode);
            UpdateDemand(); SyncOverlay(); SetFocusTo(Optimize);
        }
        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            _closed = true;
            StopOptimizationClock();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.OnLanguageChangeCompleted -= OnLanguageChanged;
            _viewModel.OnNavigateUriCommandCompleted -= OnNavigateUriCommandCompleted;
            _viewModel.OnOptimizeCommandCompleted -= OnOptimizeCommandCompleted;
            Localizer.StaticPropertyChanged -= OnLocalizerChanged;
            Monitoring.PropertyChanged -= OnMonitoringChanged;
            Dispose();
            Monitoring.Dispose();
        }
        /// <summary>Releases the lazily created settings surface and the overlay window.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>Cleans up managed subscriptions; no native resources exist.</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (!disposing) return;
            if (_overlay != null) { _overlay.Close(); _overlay = null; }
            if (_settings != null) { _settings.Dispose(); _settings = null; }
            if(Monitoring!=null)Monitoring.Dispose();
        }
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CompactMode") ApplyCompactMode(_viewModel.CompactMode);
            else if (e.PropertyName == "IsOptimizationRunning") QueueOptimizationClock();
        }
        // IsOptimizationRunning changes on the optimization worker; the clock lives on the UI thread.
        private void QueueOptimizationClock()
        {
            if (_closed) return;
            if (Dispatcher.CheckAccess()) { SyncOptimizationClock(); return; }
            if (Dispatcher.HasShutdownStarted) return;
            try { Dispatcher.BeginInvoke(new Action(SyncOptimizationClock)); }
            catch (InvalidOperationException) { } // A closing Dispatcher cannot accept clock work.
        }
        private void SyncOptimizationClock()
        {
            if (_closed || !_viewModel.IsOptimizationRunning) { StopOptimizationClock(); return; }
            if (_optimizationClock == null)
            {
                _optimizationClock = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
                _optimizationClock.Tick += OnOptimizationClockTick;
            }
            if (_optimizationClock.IsEnabled) return;
            _optimizationWatch.Restart();
            ShowOptimizationElapsed();
            _optimizationClock.Start();
        }
        private void StopOptimizationClock()
        {
            if (_optimizationClock != null) _optimizationClock.Stop();
            _optimizationWatch.Stop();
        }
        private void OnOptimizationClockTick(object sender, EventArgs e)
        {
            if (_closed || !_viewModel.IsOptimizationRunning) { StopOptimizationClock(); return; }
            ShowOptimizationElapsed();
        }
        private void ShowOptimizationElapsed()
        {
            long seconds = (long)_optimizationWatch.Elapsed.TotalSeconds;
            string time = (seconds / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (seconds % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            OptimizationElapsed.Text = string.Format(Localizer.Culture, Monitoring.Labels["OptimizationElapsed"], time);
        }
        private void OnLocalizerChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_closed) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnLanguageChanged)); return; }
            OnLanguageChanged();
        }
        private void OnLanguageChanged() { if (!_closed) RefreshColumnHeaders(); }
        private void OnWindowVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { UpdateDemand(); }
        private void OnWindowStateChanged(object sender, EventArgs e) { UpdateDemand(); }
        private void CloseToTheNotificationArea() { Hide(); ShowInTaskbar = false; }
        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            if (Settings.CloseToTheNotificationArea) CloseToTheNotificationArea();
            else App.Shutdown();
        }
        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void OnHelpButtonClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var menu = button.Resources["HelpContextMenu"] as ContextMenu;
            if (menu == null) return;
            menu.DataContext = _viewModel;
            menu.Placement = PlacementMode.Bottom; menu.PlacementTarget = button; menu.IsOpen = true;
        }
        private void OnDonateMenuItemClick(object sender, RoutedEventArgs e)
        { new DonationWindow(this).ShowDialog(); SetFocusTo(Optimize); }
        private void OnResetSettingsToDefaultConfigurationClick(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog(this, Localizer.String.ResetConfirmation, Enums.Dialog.Button.No, Enums.Dialog.Button.Yes);
            if (dialog.ShowDialog() == true && _viewModel.ResetSettingsToDefaultConfigurationCommand.CanExecute(null))
            {
                _viewModel.ResetSettingsToDefaultConfigurationCommand.Execute(null);
                Monitoring.ReloadOptions();
            }
            SetFocusTo(Optimize);
        }
        private void OnNavigateUriCommandCompleted() { SetFocusTo(Optimize); }
        private void OnOptimizeCommandCompleted()
        {
            if (Settings.CloseAfterOptimization)
            {
                if (Settings.CloseToTheNotificationArea) CloseToTheNotificationArea();
                else
                {
                    var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    delay.Tick += delegate { delay.Stop(); App.Shutdown(); }; delay.Start();
                }
            }
            else
            {
                _viewModel.IsBusy = false;
                Dispatcher.BeginInvoke(new Action(delegate { SetFocusTo(Optimize); }), DispatcherPriority.Loaded);
            }
        }
        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only the title bar drags. Table selection, scrolling and text editing retain focus.
            if (e.ChangedButton == MouseButton.Left && e.GetPosition(this).Y < 58 && e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
        private static void SetFocusTo(UIElement element)
        {
            if (element != null && element.IsEnabled && element.IsVisible && element.Focusable) Keyboard.Focus(element);
        }
    }
}
