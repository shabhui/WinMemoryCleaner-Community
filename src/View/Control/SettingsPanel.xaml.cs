using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WinMemoryCleaner
{
    /// <summary>On-demand host for the original cleaner settings and commands.</summary>
    public partial class SettingsPanel : UserControl, IDisposable
    {
        private MainViewModel _viewModel;
        /// <summary>Creates the settings surface without enumerating process candidates.</summary>
        public SettingsPanel()
        {
            InitializeComponent();
            DataContextChanged += OnContextChanged;
            DarkTheme.IsChecked = ThemeManager.Theme == Enums.Theme.Dark;
            LightTheme.IsChecked = ThemeManager.Theme == Enums.Theme.Light;
            var handler = new MouseButtonEventHandler(OnSliderMouseDown);
            FontSizeSlider.AddHandler(PreviewMouseLeftButtonDownEvent, handler, true);
            AutoOptimizationIntervalSlider.AddHandler(PreviewMouseLeftButtonDownEvent, handler, true);
            AutoOptimizationMemoryUsageSlider.AddHandler(PreviewMouseLeftButtonDownEvent, handler, true);
        }
        private void OnContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null) _viewModel.OnAddProcessToExclusionListCommandCompleted -= OnExclusionAdded;
            _viewModel = DataContext as MainViewModel;
            if (_viewModel != null) _viewModel.OnAddProcessToExclusionListCommandCompleted += OnExclusionAdded;
        }
        private void OnExclusionAdded() { ProcessExclusionScrollViewer.ScrollToEnd(); }
        private void OnProcessesDropDownOpened(object sender, EventArgs e)
        { if (_viewModel != null) _viewModel.RefreshProcesses(); }
        private void OnThemeClick(object sender, RoutedEventArgs e)
        { ThemeManager.Theme = ((RadioButton)sender).Tag.ToString() == "Light" ? Enums.Theme.Light : Enums.Theme.Dark; }
        private static void OnSliderMouseDown(object sender, MouseButtonEventArgs e)
        {
            var slider = (Slider)sender;
            var track = slider.Template == null ? null : slider.Template.FindName("PART_Track", slider) as Track;
            if (!slider.IsMoveToPointEnabled || track == null || track.Thumb == null || track.Thumb.IsMouseOver) return;
            track.Thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
        }
        /// <summary>Releases subscriptions to the retained cleaner model.</summary>
        public void Dispose()
        {
            if (_viewModel != null) _viewModel.OnAddProcessToExclusionListCommandCompleted -= OnExclusionAdded;
            _viewModel = null;
            DataContextChanged -= OnContextChanged;
        }
    }
}
