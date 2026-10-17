using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WinMemoryCleaner
{
    /// <summary>Independent desktop overlay. No ownership, activation or foreign-process injection.</summary>
    public partial class MonitoringOverlay : Window
    {
        private const int ExStyle = -20;
        private const int NoActivate = 0x08000000;
        private const int ToolWindow = 0x00000080;
        private const int Transparent = 0x00000020;
        private readonly MonitoringViewModel _viewModel;
        private HwndSource _source;
        private IntPtr _handle;
        private bool _repositioning, _dragging, _closed, _placementPending;
        private NativePoint _dragStart;
        private NativeRect _dragBounds;
        private readonly DispatcherTimer _repositionTimeout;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);

        /// <summary>Creates an overlay sharing the single monitoring source.</summary>
        /// <param name="viewModel">Shared monitoring state.</param>
        public MonitoringOverlay(MonitoringViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException("viewModel");
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;
            _repositionTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _repositionTimeout.Tick += OnRepositionTimeout;
            SourceInitialized += OnSourceInitialized;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnVisibleChanged;
            Closed += OnClosed;
            MouseLeftButtonDown += OnDragStart;
            MouseMove += OnDragMove;
            MouseLeftButtonUp += OnDragEnd;
            LostMouseCapture += OnCaptureLost;
            _viewModel.PropertyChanged += OnOptionsChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }
        private bool PassThrough { get { return _viewModel.ClickThrough && !_repositioning; } }
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_handle);
            if (_source != null) _source.AddHook(WindowMessage);
            ApplyInputStyles(); QueuePlacement();
        }
        private void ApplyInputStyles()
        {
            if (_handle == IntPtr.Zero || _closed) return;
            int style = GetWindowLong(_handle, ExStyle) | NoActivate | ToolWindow;
            style = PassThrough ? style | Transparent : style & ~Transparent;
            SetWindowLong(_handle, ExStyle, style);
            // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED
            SetWindowPos(_handle, new IntPtr(-1), 0, 0, 0, 0, 0x33);
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (message == 0x84 && PassThrough) { handled = true; return new IntPtr(-1); }
            if (message == 0x2E0 || message == 0x7E || message == 0x1A) QueuePlacement();
            return IntPtr.Zero;
        }
        private void QueuePlacement()
        {
            if (_closed || _placementPending || Dispatcher.HasShutdownStarted) return;
            _placementPending = true;
            Dispatcher.BeginInvoke(new Action(delegate { _placementPending = false; ApplyPlacement(); }), DispatcherPriority.Loaded);
        }
        private void ApplyPlacement()
        {
            if (_closed || _handle == IntPtr.Zero || _dragging) return;
            var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _viewModel.OverlayScreen) ?? Forms.Screen.PrimaryScreen;
            if (screen == null) return;
            NativeRect bounds;
            if (!GetWindowRect(_handle, out bounds)) return;
            var area = screen.WorkingArea;
            // Persist and clamp in physical screen pixels; Win32 handles system/per-monitor DPI transforms.
            var point = OverlayPlacement.Place(new Rect(area.X, area.Y, area.Width, area.Height), new Size(Math.Max(1, bounds.Right - bounds.Left), Math.Max(1, bounds.Bottom - bounds.Top)), _viewModel.OverlayCorner, _viewModel.OverlayX, _viewModel.OverlayY);
            SetWindowPos(_handle, new IntPtr(-1), (int)point.X, (int)point.Y, 0, 0, 0x11);
        }
        /// <summary>Temporarily accepts dragging, then restores the persisted mouse-through preference.</summary>
        public void BeginReposition()
        {
            _repositioning = true;
            RepositionHint.Visibility = Visibility.Visible;
            ApplyInputStyles();
            _repositionTimeout.Stop(); _repositionTimeout.Start();
        }
        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (PassThrough || !GetCursorPos(out _dragStart) || !GetWindowRect(_handle, out _dragBounds)) return;
            _dragging = CaptureMouse();
            e.Handled = _dragging;
        }
        private void OnDragMove(object sender, MouseEventArgs e)
        {
            NativePoint cursor;
            if (!_dragging || !GetCursorPos(out cursor)) return;
            SetWindowPos(_handle, new IntPtr(-1), _dragBounds.Left + cursor.X - _dragStart.X, _dragBounds.Top + cursor.Y - _dragStart.Y, 0, 0, 0x11);
        }
        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            FinishReposition(true); e.Handled = true;
        }
        private void OnCaptureLost(object sender, MouseEventArgs e) { if (_dragging) FinishReposition(true); }
        private void OnRepositionTimeout(object sender, EventArgs e) { FinishReposition(_dragging); }
        private void FinishReposition(bool save)
        {
            _dragging = false; _repositioning = false;
            _repositionTimeout.Stop(); ReleaseMouseCapture();
            RepositionHint.Visibility = Visibility.Collapsed;
            if (save)
            {
                NativeRect bounds;
                if (GetWindowRect(_handle, out bounds)) _viewModel.SavePosition(Forms.Screen.FromHandle(_handle).DeviceName, bounds.Left, bounds.Top);
            }
            ApplyInputStyles(); QueuePlacement();
        }
        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ClickThrough") ApplyInputStyles();
            if (e.PropertyName == "OverlayScreen" || e.PropertyName == "OverlayCorner") QueuePlacement();
            if (e.PropertyName == "OverlayOrientation") QueuePlacement();
        }
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) { QueuePlacement(); }
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) QueuePlacement();
            else if (_repositioning || _dragging) FinishReposition(false);
        }
        private void OnDisplaysChanged(object sender, EventArgs e)
        {
            if (_closed || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate { if (!_closed) { _viewModel.RefreshDisplays(); QueuePlacement(); } }));
        }
        private void OnClosed(object sender, EventArgs e)
        {
            _closed = true; _repositionTimeout.Stop();
            _viewModel.PropertyChanged -= OnOptionsChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            if (_source != null) _source.RemoveHook(WindowMessage);
        }
    }
}
