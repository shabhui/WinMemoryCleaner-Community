using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using Cursors = System.Windows.Input.Cursors;
using WpfApplication = System.Windows.Application;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Notification Service
    /// </summary>
    public class NotificationService : INotificationService
    {
        #region Fields

        private volatile int _currentRotationAngle;
        private Icon _currentIcon;
        private volatile bool _disposed;
        private readonly Icon _imageIcon;
        private readonly NotifyIcon _notifyIcon;
        private readonly object _disposeLock = new object();

        /// <summary>
        /// Serializes GDI rendering, cached appearance and pending-icon ownership. The
        /// UI callback takes this before <see cref="_currentIconLock" />; the reverse
        /// order is never used. Never held while dispatching work to the UI thread.
        /// </summary>
        private readonly object _iconRenderLock = new object();

        private struct TrayIconState
        {
            internal bool MemoryUsage, Optimizing, Transparent;
            internal int Percentage, BackgroundArgb, TextArgb;
            internal string Text;

            internal bool SameImage(TrayIconState other)
            {
                return MemoryUsage == other.MemoryUsage && Optimizing == other.Optimizing
                    && (!MemoryUsage || (Percentage == other.Percentage && Transparent == other.Transparent
                    && BackgroundArgb == other.BackgroundArgb && TextArgb == other.TextArgb));
            }

            internal bool SameUpdate(TrayIconState other)
            {
                return SameImage(other) && string.Equals(Text, other.Text, StringComparison.Ordinal);
            }
        }

        // At most one unconsumed icon and one dispatcher callback, even while the UI is busy.
        // No rendered icon is shared between a pending update and the displayed icon.
        private TrayIconState _requestedState, _appliedState;
        private bool _hasRequestedState, _hasAppliedState, _updateQueued, _replacePendingIcon;
        private Icon _pendingIcon;
        private long _dispatchId;

        /// <summary>
        /// Guards the <see cref="_currentIcon" /> swap. Normally that swap is serialized by
        /// running on the UI thread, but when no dispatcher exists the work runs inline on the
        /// calling thread, so concurrent callers could otherwise both dispose the same icon.
        /// Only ever held for a field assignment; never across a dispatcher call.
        /// </summary>
        private readonly object _currentIconLock = new object();

        /// <summary>
        /// Owned by the UI thread. Only ever read or written inside a dispatcher callback,
        /// which makes the UI thread the single point of truth and removes the need to hold
        /// <see cref="_disposeLock" /> across a dispatcher call to keep it consistent.
        /// </summary>
        private DispatcherTimer _rotationTimer;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationService" /> class.
        /// </summary>
        /// <param name="notifyIcon">Notify Icon</param>
        public NotificationService(NotifyIcon notifyIcon)
        {
            _currentRotationAngle = 0;
            _imageIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            _notifyIcon = notifyIcon;

            Initialize();
        }

        /// <summary>
        /// Initializes the notification service
        /// </summary>
        public void Initialize()
        {
            if (_notifyIcon == null)
                return;

            // Notification Areas (Menu)
            _notifyIcon.ContextMenuStrip = new TrayIconContextMenuControl();

            // Optimize
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.Optimize, null, (sender, args) =>
            {
                var mainViewModel = DependencyInjection.Container.Resolve<MainViewModel>();

                if (mainViewModel.OptimizeCommand.CanExecute(null))
                    mainViewModel.OptimizeCommand.Execute(null);
            });

            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            // Exit
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.Exit, null, (sender, args) =>
            {
                App.Shutdown();
            });

            Update(new Memory());

            _notifyIcon.Visible = true;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                }

                // DispatcherTimer has thread affinity: Stop() from another thread throws, which
                // previously left the timer running and kept firing ticks during shutdown.
                InvokeOnUi(() => { lock (_iconRenderLock) CleanupRotationTimer(); });

                try
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Icon = null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }

                try
                {
                    lock (_iconRenderLock)
                    {
                        _updateQueued = _hasRequestedState = _hasAppliedState = false;
                        DisposePendingIcon();
                        lock (_currentIconLock)
                        {
                            DisposeRenderedIcon(_currentIcon);
                            _currentIcon = null;
                        }
                        if (_imageIcon != null)
                            _imageIcon.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }

                try
                {
                    if (_notifyIcon != null)
                        _notifyIcon.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Runs an action on the UI thread without ever blocking the calling thread.
        /// </summary>
        /// <remarks>
        /// Must stay non-blocking. This used to be a blocking <c>Dispatcher.Invoke</c> reached
        /// from <see cref="Update" /> on a background thread that held <see cref="_disposeLock" />,
        /// while the UI thread took that same lock in <see cref="OnRotationTimerTick" />. Each
        /// side then waited on the other (AB-BA), which froze the window and left a process that
        /// only Task Manager could end. Both halves of that cycle are now gone, and using
        /// <c>BeginInvoke</c> here is what keeps it from being reintroduced by a caller that
        /// holds a lock.
        /// </remarks>
        /// <param name="action">The action to run on the UI thread.</param>
        private static void InvokeOnUi(Action action)
        {
            TryInvokeOnUi(action, null);
        }

        private static bool TryInvokeOnUi(Action action, EventHandler aborted)
        {
            if (action == null)
                return false;
            try
            {
                var application = WpfApplication.Current;
                var dispatcher = application == null ? null : application.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    action();
                else
                {
                    var operation = dispatcher.BeginInvoke(action);
                    if (aborted != null)
                    {
                        operation.Aborted += aborted;
                        // Also cover shutdown/abort before the handler was attached.
                        if (operation.Status == DispatcherOperationStatus.Aborted)
                            aborted(operation, EventArgs.Empty);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
                return false;
            }
        }

        /// <summary>
        /// Cleans up the rotation timer resources and resets the rotation angle
        /// </summary>
        private void CleanupRotationTimer()
        {
            try
            {
                _currentRotationAngle = 0;

                if (_rotationTimer != null)
                {
                    _rotationTimer.Stop();
                    _rotationTimer.Tick -= OnRotationTimerTick;
                    _rotationTimer = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }
        }

        /// <summary>
        /// Gets the effective background color without allocating a GDI brush.
        /// </summary>
        /// <param name="percentage">Physical memory usage.</param>
        /// <param name="isOptimizing">Whether optimization is active.</param>
        /// <param name="transparent">Whether the background is transparent.</param>
        /// <returns>The background color.</returns>
        private static Color GetBackgroundColor(int percentage, bool isOptimizing, bool transparent)
        {
            try
            {
                if (transparent)
                    return Color.Transparent;
                var brush = isOptimizing ? Settings.TrayIconOptimizingColor
                    : percentage >= Settings.TrayIconDangerLevel ? Settings.TrayIconDangerColor
                    : percentage >= Settings.TrayIconWarningLevel ? Settings.TrayIconWarningColor
                    : Settings.TrayIconBackgroundColor;
                return ((SolidBrush)brush).Color;
            }
            catch (Exception)
            {
                return Color.Black;
            }
        }

        private TrayIconState CaptureIconState(Memory memory, bool isOptimizing, bool memoryUsage)
        {
            var state = new TrayIconState
            {
                MemoryUsage = memoryUsage,
                Optimizing = isOptimizing,
                Text = GetText(memory, isOptimizing)
            };
            if (memoryUsage)
            {
                int percentage = memory.Physical.Used.Percentage;
                state.Percentage = percentage == 100 ? 99 : percentage;
                state.Transparent = Settings.TrayIconUseTransparentBackground;
                state.BackgroundArgb = GetBackgroundColor(percentage, isOptimizing, state.Transparent).ToArgb();
                state.TextArgb = GetTextColor(percentage, isOptimizing, state.Transparent).ToArgb();
            }
            return state;
        }

        /// <summary>
        /// Gets a custom icon displaying the current memory usage percentage
        /// </summary>
        /// <param name="memory">The memory information</param>
        /// <param name="isOptimizing">if set to <c>true</c> the system is optimizing</param>
        /// <returns>An icon with rendered memory percentage text</returns>
        private Icon GetMemoryUsageIcon(Memory memory, bool isOptimizing)
        {
            return RenderMemoryUsageIcon(CaptureIconState(memory, isOptimizing, true));
        }

        private Icon RenderMemoryUsageIcon(TrayIconState state)
        {
            try
            {
                lock (_iconRenderLock)
                {
                    using (var image = new Bitmap(16, 16))
                    using (var graphics = Graphics.FromImage(image))
                    using (var font = new Font("Consolas", 14F, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var format = new StringFormat())
                    using (var backgroundBrush = new SolidBrush(Color.FromArgb(state.BackgroundArgb)))
                    using (var textBrush = new SolidBrush(Color.FromArgb(state.TextArgb)))
                    {
                        // Configure format
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;

                        // Configure graphics quality
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                        // Draw background
                        if (!state.Transparent)
                        {
                            using (var path = new GraphicsPath())
                            {
                                path.AddArc(0, 0, 10, 10, 180, 90);
                                path.AddArc(5, 0, 10, 10, 270, 90);
                                path.AddArc(5, 5, 10, 10, 0, 90);
                                path.AddArc(0, 5, 10, 10, 90, 90);
                                path.CloseFigure();

                                graphics.FillPath(backgroundBrush, path);
                            }
                        }

                        // Draw text
                        graphics.DrawString(state.Percentage.ToString("00", CultureInfo.InvariantCulture), font, textBrush, 8F, 9F, format);

                        var handle = image.GetHicon();

                        using (var icon = Icon.FromHandle(handle))
                        {
                            var clonedIcon = (Icon)icon.Clone();

                            NativeMethods.DestroyIcon(handle);

                            return clonedIcon;
                        }
                    }
                }
            }
            catch
            {
                return _imageIcon;
            }
        }

        /// <summary>
        /// Gets a rotated version of the specified icon
        /// </summary>
        /// <param name="icon">The icon to rotate</param>
        /// <param name="angle">The rotation angle in degrees</param>
        /// <returns>A new icon rotated by the specified angle</returns>
        private Icon GetRotatedIcon(Icon icon, float angle)
        {
            if (icon == null || angle == 0)
                return icon;

            try
            {
                // icon is the shared _imageIcon; ToBitmap is a GDI read that must not run
                // concurrently from the UI thread and the monitor thread.
                lock (_iconRenderLock)
                {
                    using (var image = icon.ToBitmap())
                    using (var rotatedImage = new Bitmap(image.Width, image.Height))
                    using (var graphics = Graphics.FromImage(rotatedImage))
                    {
                        // Configure graphics quality
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;

                        // Rotate around center point
                        var centerX = image.Width / 2f;
                        var centerY = image.Height / 2f;

                        graphics.TranslateTransform(centerX, centerY);
                        graphics.RotateTransform(angle);
                        graphics.TranslateTransform(-centerX, -centerY);

                        graphics.DrawImage(image, new Point(0, 0));

                        var handle = rotatedImage.GetHicon();

                        using (var tempIcon = Icon.FromHandle(handle))
                        {
                            var clonedIcon = (Icon)tempIcon.Clone();

                            NativeMethods.DestroyIcon(handle);

                            return clonedIcon;
                        }
                    }
                }
            }
            catch
            {
                return icon;
            }
        }

        /// <summary>
        /// Gets the tray icon tooltip text based on memory usage and optimization state
        /// </summary>
        /// <param name="memory">The memory information</param>
        /// <param name="isOptimizing">if set to <c>true</c> the system is optimizing</param>
        /// <returns>The formatted tooltip text</returns>
        private string GetText(Memory memory, bool isOptimizing)
        {
            try
            {
                string text;

                if (isOptimizing)
                    text = Localizer.String.Optimizing.ToUpper(Localizer.Culture);
                else
                {
                    text = Settings.ShowVirtualMemory
                        ? string.Format(Localizer.Culture, "{0}: {1}%{2}{3}: {4}%", Localizer.String.PhysicalMemory, memory.Physical.Used.Percentage, Environment.NewLine, Localizer.String.VirtualMemory, memory.Virtual.Used.Percentage)
                        : string.Format(Localizer.Culture, "{0}: {1}%", Localizer.String.PhysicalMemory, memory.Physical.Used.Percentage);
                }
                
                // Truncate to 63 characters
                if (text.Length > 63)
                    text = text.Substring(0, 63);

                return text;
            }
            catch
            {
                return Constants.App.Title;
            }
        }

        /// <summary>
        /// Gets the effective text color without allocating a GDI brush.
        /// </summary>
        /// <param name="percentage">Physical memory usage.</param>
        /// <param name="isOptimizing">Whether optimization is active.</param>
        /// <param name="transparent">Whether the background is transparent.</param>
        /// <returns>The text color.</returns>
        private static Color GetTextColor(int percentage, bool isOptimizing, bool transparent)
        {
            try
            {
                var brush = !transparent ? Settings.TrayIconTextColor
                    : isOptimizing ? Settings.TrayIconOptimizingColor
                    : percentage >= Settings.TrayIconDangerLevel ? Settings.TrayIconDangerColor
                    : percentage >= Settings.TrayIconWarningLevel ? Settings.TrayIconWarningColor
                    : Settings.TrayIconTextColor;
                return ((SolidBrush)brush).Color;
            }
            catch (Exception)
            {
                return Color.White;
            }
        }

        /// <summary>
        /// Shows or hides the loading cursor and enables/disables the context menu
        /// </summary>
        /// <param name="running">if set to <c>true</c> shows loading cursor and disables menu</param>
        public void Loading(bool running)
        {
            // Non-blocking: this is called from background threads that hold locks (the
            // optimization path sets IsBusy while holding the view model lock).
            InvokeOnUi(() =>
            {
                try
                {
                    if (_disposed)
                        return;

                    Mouse.OverrideCursor = running ? Cursors.Wait : null;

                    if (_notifyIcon != null && _notifyIcon.ContextMenuStrip != null)
                        _notifyIcon.ContextMenuStrip.Enabled = !running;
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }
            });
        }

        /// <summary>
        /// Displays a balloon tip notification from the system tray
        /// /// </summary>
        /// <param name="message">The notification message text</param>
        /// <param name="title">The notification title</param>
        /// <param name="timeout">The time period in seconds to display the notification</param>
        /// <param name="icon">The notification icon type</param>
        public void Notify(string message, string title = null, int timeout = 5, Enums.Icon.Notification icon = Enums.Icon.Notification.None)
        {
            if (_notifyIcon == null)
                return;

            // Marshalled: the optimization path calls this from a background thread, and
            // NotifyIcon.Visible / ShowBalloonTip must run on the thread that owns the icon.
            InvokeOnUi(() =>
            {
                try
                {
                    if (_disposed || _notifyIcon == null)
                        return;

                    _notifyIcon.Visible = false;
                    _notifyIcon.Visible = true;

                    _notifyIcon.ShowBalloonTip(timeout * 1000, title, message, (ToolTipIcon)icon);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }
            });
        }

        /// <summary>
        /// Handles the rotation timer tick event to animate the icon rotation
        /// </summary>
        /// <param name="sender">The event sender</param>
        /// <param name="e">The event arguments</param>
        private void OnRotationTimerTick(object sender, EventArgs e)
        {
            // Already on the UI thread. Taking _disposeLock here is what let a background
            // thread inside Update block the UI thread, so the lock is deliberately absent.
            if (_disposed)
                return;

            try
            {
                lock (_iconRenderLock)
                {
                    if (_disposed || _rotationTimer == null)
                        return;
                    _currentRotationAngle = (_currentRotationAngle + 90) % 360;
                    ApplyIcon(_notifyIcon.Text, GetRotatedIcon(_imageIcon, _currentRotationAngle));
                }
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }
        }

        /// <summary>
        /// Starts the icon rotation animation for the optimization state
        /// </summary>
        private void StartRotationAnimation()
        {
            // Called only by the UI callback (or inline when no Application exists).
            try
            {
                if (_rotationTimer != null || _disposed)
                    return;
                _currentRotationAngle = 0;
                _rotationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _rotationTimer.Tick += OnRotationTimerTick;
                _rotationTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }
        }


        /// <summary>
        /// Updates the tray icon and tooltip text based on current memory usage and optimization state
        /// </summary>
        /// <param name="memory">The memory information</param>
        /// <param name="isOptimizing">if set to <c>true</c> the system is optimizing</param>
        /// <exception cref="ArgumentNullException">memory</exception>
        public void Update(Memory memory, bool isOptimizing = false)
        {
            if (memory == null)
                throw new ArgumentNullException("memory");

            if (_disposed || _notifyIcon == null)
                return;

            long dispatchId = 0;
            try
            {
                lock (_iconRenderLock)
                {
                    if (_disposed)
                        return;
                    var state = CaptureIconState(memory, isOptimizing, Settings.TrayIconShowMemoryUsage);
                    if (_hasRequestedState && _requestedState.SameUpdate(state))
                        return;

                    // A tooltip-only change can keep the displayed icon. An unconsumed
                    // update is superseded, not shared: its owned native icon is released.
                    bool replace = !_hasAppliedState || !_appliedState.SameImage(state);
                    var newIcon = !replace ? null : state.MemoryUsage ? RenderMemoryUsageIcon(state) : _imageIcon;
                    DisposePendingIcon();
                    _pendingIcon = newIcon;
                    _replacePendingIcon = replace;
                    _requestedState = state;
                    _hasRequestedState = !state.MemoryUsage || !replace || (newIcon != null && newIcon != _imageIcon);
                    if (!_updateQueued)
                    {
                        _updateQueued = true;
                        dispatchId = ++_dispatchId;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
                return;
            }

            // Rendering stays on the caller. Never wait for the UI or hold the render lock
            // across dispatch. A generation guard makes duplicate abort callbacks harmless.
            if (dispatchId != 0 && !TryInvokeOnUi(() => ApplyPendingUpdate(dispatchId),
                (sender, args) => CancelPendingUpdate(dispatchId)))
                CancelPendingUpdate(dispatchId);
        }

        private void ApplyPendingUpdate(long dispatchId)
        {
            lock (_iconRenderLock)
            {
                if (!_updateQueued || dispatchId != _dispatchId)
                    return;
                var state = _requestedState;
                var icon = _pendingIcon;
                bool replace = _replacePendingIcon;
                _pendingIcon = null;
                _updateQueued = false;
                if (_disposed)
                {
                    DisposeRenderedIcon(icon);
                    return;
                }

                if (!state.MemoryUsage && state.Optimizing)
                    StartRotationAnimation();
                else
                    CleanupRotationTimer();
                bool applied = ApplyIcon(state.Text, icon, replace);
                _hasAppliedState = applied && (!state.MemoryUsage || !replace || (icon != null && icon != _imageIcon));
                _hasRequestedState = _hasAppliedState;
                if (_hasAppliedState)
                    _appliedState = state;
            }
        }

        private void CancelPendingUpdate(long dispatchId)
        {
            lock (_iconRenderLock)
            {
                if (!_updateQueued || dispatchId != _dispatchId)
                    return;
                _updateQueued = _hasRequestedState = false;
                DisposePendingIcon();
            }
        }

        private void DisposePendingIcon()
        {
            var icon = _pendingIcon;
            _pendingIcon = null;
            DisposeRenderedIcon(icon);
        }

        private void DisposeRenderedIcon(Icon icon)
        {
            if (icon == null || icon == _imageIcon)
                return;
            try { icon.Dispose(); }
            catch (Exception ex) { Logger.Debug(ex); }
        }

        /// <summary>
        /// Assigns the tray icon text and image, then releases the icon it replaced.
        /// </summary>
        /// <remarks>
        /// Normally invoked on the UI thread. When no dispatcher exists it runs inline on the
        /// calling thread instead, so the icon swap is guarded by <see cref="_currentIconLock" />
        /// rather than relying on UI-thread serialization.
        /// </remarks>
        /// <param name="text">The tooltip text.</param>
        /// <param name="newIcon">The icon to display.</param>
        /// <param name="replaceIcon">Whether the image, rather than just the tooltip, changed.</param>
        /// <returns>Whether the requested update was applied.</returns>
        private bool ApplyIcon(string text, Icon newIcon, bool replaceIcon = true)
        {
            Icon oldIcon = null;
            bool adopted = false;
            try
            {
                lock (_currentIconLock)
                {
                    if (_disposed || _notifyIcon == null)
                        return false;
                    if (_notifyIcon.Text != text)
                        _notifyIcon.Text = text;
                    if (replaceIcon)
                    {
                        oldIcon = _currentIcon;
                        if (_notifyIcon.Icon != newIcon)
                            _notifyIcon.Icon = newIcon;
                        _currentIcon = newIcon;
                        adopted = true;
                    }
                }
                return true;
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore.
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }
            finally
            {
                if (replaceIcon && !adopted)
                    DisposeRenderedIcon(newIcon);
                if (adopted && oldIcon != newIcon)
                    DisposeRenderedIcon(oldIcon);
            }
            return false;
        }

        #endregion
    }
}