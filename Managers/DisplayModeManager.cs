using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MugiSideBrowser.Helpers;

namespace MugiSideBrowser.Managers
{
    public enum DisplayMode
    {
        AppBar,
        AutoHide,
        Normal
    }

    public class DisplayModeManager
    {
        private readonly Window _window;
        private readonly AppBarHelper _appBarHelper;
        private DisplayMode _currentMode = DisplayMode.AppBar;
        private DispatcherTimer? _mouseTimer;
        private bool _isSlidOut = false;
        private bool _isDragging = false;
        private double _currentFullWidth = Constants.FullWidthDefault;

        public DisplayModeManager(Window window, AppBarHelper appBarHelper)
        {
            _window = window;
            _appBarHelper = appBarHelper;
        }

        public DisplayMode CurrentMode => _currentMode;
        public bool IsSlidOut => _isSlidOut;
        public double CurrentFullWidth => _currentFullWidth;
        public bool IsDragging => _isDragging;

        public event Action<DisplayMode>? ModeChanged;
        public event Action? WindowControlsStateChanged;

        public void SetCurrentFullWidth(double width)
        {
            _currentFullWidth = width;
        }

        public void SetDragging(bool isDragging)
        {
            _isDragging = isDragging;
        }

        public void TransitionToMode(DisplayMode mode)
        {
            _window.BeginAnimation(Window.LeftProperty, null);
            _window.BeginAnimation(Window.WidthProperty, null);
            _window.BeginAnimation(Window.TopProperty, null);

            _currentMode = mode;

            if (mode == DisplayMode.AppBar)
            {
                TransitionToAppBarMode();
            }
            else if (mode == DisplayMode.Normal)
            {
                TransitionToNormalMode();
            }
            else if (mode == DisplayMode.AutoHide)
            {
                TransitionToAutoHideMode();
            }

            ModeChanged?.Invoke(mode);
            WindowControlsStateChanged?.Invoke();
        }

        private void TransitionToAppBarMode()
        {
            _window.ShowInTaskbar = false;
            ApplyToolWindowStyle(true);
            StopAutoHideTimer();

            var mi = _appBarHelper.CurrentWorkAreaRect;
            double dpi = DpiHelper.GetDpiScale(_window);

            _window.Width = _currentFullWidth;
            _window.Left = (mi.Left + (mi.Right - mi.Left) / 2) / dpi - (_window.Width / 2);
            _window.Top = (mi.Top + (mi.Bottom - mi.Top) / 2) / dpi - (_window.Height / 2);

            _appBarHelper.Register();
            _window.Topmost = true;

            _window.Top = mi.Top / dpi;
            _window.Height = (mi.Bottom - mi.Top) / dpi;
        }

        private void TransitionToNormalMode()
        {
            _appBarHelper.Unregister();
            StopAutoHideTimer();

            _window.Topmost = false;
            _window.ShowInTaskbar = true;
            ApplyToolWindowStyle(false);

            try
            {
                var mi = DpiHelper.GetMonitorInfo(_window);
                double dpi = DpiHelper.GetDpiScale(_window);
                double workAreaHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpi;

                double targetHeight = workAreaHeight - Constants.WorkAreaMargin;
                if (targetHeight < Constants.MinPaneHeight) targetHeight = Constants.MinPaneHeight;

                _window.Height = targetHeight;
                _window.Top = (mi.rcWork.Top / dpi) + Constants.TopMargin;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adjusting window size: {ex.Message}");
            }
        }

        private void TransitionToAutoHideMode()
        {
            _appBarHelper.Unregister();
            _window.Topmost = true;
            _window.ShowInTaskbar = false;
            ApplyToolWindowStyle(true);
            StartAutoHideTimer();
        }

        public void StartAutoHideTimer()
        {
            if (_mouseTimer == null)
            {
                _mouseTimer = new DispatcherTimer();
                _mouseTimer.Interval = TimeSpan.FromMilliseconds(Constants.AutoHideTimerIntervalMs);
                _mouseTimer.Tick += MouseTimer_Tick;
            }
            _mouseTimer.Start();

            _isSlidOut = false;
            SlideOut();
        }

        public void StopAutoHideTimer()
        {
            _mouseTimer?.Stop();
            _window.Width = _currentFullWidth;
        }

        private void MouseTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentMode != DisplayMode.AutoHide || _isDragging) return;

            var point = DpiHelper.GetCursorPos();
            var helper = new WindowInteropHelper(_window);
            var mi = DpiHelper.GetMonitorInfo(helper.Handle);

            double dpi = DpiHelper.GetDpiScale(_window);
            bool isMouseInTriggerZone = false;
            bool isMouseInWindow = false;

            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                isMouseInTriggerZone = (point.X >= mi.rcMonitor.Right - Constants.TriggerZonePixel && 
                                        point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
                isMouseInWindow = (point.X >= mi.rcMonitor.Right - (_currentFullWidth * dpi) && 
                                   point.X <= mi.rcMonitor.Right && 
                                   point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
            }
            else
            {
                isMouseInTriggerZone = (point.X <= mi.rcMonitor.Left + Constants.TriggerZonePixel && 
                                        point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
                isMouseInWindow = (point.X >= mi.rcMonitor.Left && 
                                   point.X <= mi.rcMonitor.Left + (_currentFullWidth * dpi) && 
                                   point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
            }

            if (isMouseInTriggerZone && !_isSlidOut)
            {
                SlideIn();
            }
            else if (!isMouseInWindow && _isSlidOut)
            {
                SlideOut();
            }
        }

        private void SlideIn()
        {
            if (_isSlidOut) return;
            _isSlidOut = true;
            _window.Topmost = true;
            AnimateWindow(_currentFullWidth);
        }

        private void SlideOut()
        {
            _isSlidOut = false;
            AnimateWindow(Constants.TriggerWidth);

            var helper = new WindowInteropHelper(_window);
            if (NativeMethods.GetForegroundWindow() == helper.Handle)
            {
                IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(taskbarHwnd);
                }
            }
        }

        private void AnimateWindow(double targetWidth)
        {
            double dpi = DpiHelper.GetDpiScale(_window);
            var mi = _appBarHelper.CurrentWorkAreaRect;

            double targetLeft;
            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                targetLeft = (mi.Right / dpi) - targetWidth;
            }
            else
            {
                targetLeft = mi.Left / dpi;
            }

            _window.Top = mi.Top / dpi;
            _window.Height = (mi.Bottom - mi.Top) / dpi;

            var duration = TimeSpan.FromMilliseconds(Constants.AnimationDurationMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            _window.BeginAnimation(Window.WidthProperty, widthAnim);
            _window.BeginAnimation(Window.LeftProperty, leftAnim);
        }

        private void ApplyToolWindowStyle(bool enable)
        {
            var helper = new WindowInteropHelper(_window);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero) return;

            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if (enable)
            {
                exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            }
            else
            {
                exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;
            }
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, exStyle);
        }

        public void HandleStateChanged(WindowState state)
        {
            if (state == WindowState.Minimized)
            {
                if (_currentMode == DisplayMode.AppBar || _currentMode == DisplayMode.AutoHide)
                {
                    _window.WindowState = WindowState.Normal;
                }
            }
        }
    }
}
