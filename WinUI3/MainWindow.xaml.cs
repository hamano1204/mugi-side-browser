using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace MugiSideBrowser.WinUI3
{
    public sealed partial class MainWindow : Window
    {
        private readonly IntPtr _hwnd;
        private readonly AppWindow _appWindow;
        private readonly AppBarHelper _appBarHelper;
        private readonly NativeMethods.SUBCLASSPROC _subclassProc;

        // Custom Window Message for AppBar Callback
        private const int AppBarCallbackMessage = 0x0400 + 100; // WM_USER + 100
        private static readonly uint ShowWindowMessage = NativeMethods.RegisterWindowMessage("MugiSideBrowser_ShowWindowMessage");

        // Dragging & Resizing States
        private bool _isResizing = false;
        private Windows.Foundation.Point _resizeStartPoint;
        private int _resizeStartWidth;
        private int _resizeStartHeight;

        private bool _isDragging = false;
        private Windows.Foundation.Point _dragStartPoint;
        private Windows.Graphics.PointInt32 _dragStartWindowPos;

        public MainWindow()
        {
            InitializeComponent();

            _hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // Subclass the Window to intercept Win32 messages
            _subclassProc = WindowSubclassProc;
            NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, 1, IntPtr.Zero);

            // Initialize AppBar Helper
            _appBarHelper = new AppBarHelper(this);

            // Setup Borderless and Topmost properties using AppWindow Presenter
            ConfigureAppWindow();

            // Set Initial size (logical 460px converted to physical)
            double scale = _appBarHelper.GetDpiScale();
            int physicalWidth = (int)(460 * scale);
            int physicalHeight = (int)(800 * scale);
            _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

            this.Closed += MainWindow_Closed;

            // Auto-register AppBar on startup
            _appBarHelper.Register();
        }

        private void ConfigureAppWindow()
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                
                // Hide Titlebar and Border
                presenter.SetBorderAndTitleBar(false, false);
            }
            _appWindow.IsShownInSwitchers = false; // Hide from Alt+Tab / Taskbar
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _appBarHelper.Unregister();
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == ShowWindowMessage)
            {
                _appWindow.Show();
                if (_appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = true;
                }
                return (IntPtr)1;
            }

            return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        // --- Custom Resizing Grip Handlers ---
        private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            _isResizing = true;
            element.CapturePointer(e.Pointer);

            var properties = e.GetCurrentPoint(null);
            _resizeStartPoint = properties.Position;

            _resizeStartWidth = _appWindow.Size.Width;
            _resizeStartHeight = _appWindow.Size.Height;
            e.Handled = true;
        }

        private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizing) return;

            var element = sender as FrameworkElement;
            if (element == null) return;

            var properties = e.GetCurrentPoint(null);
            var currentPoint = properties.Position;

            double scale = _appBarHelper.GetDpiScale();
            double diffX = (currentPoint.X - _resizeStartPoint.X) * scale;
            double diffY = (currentPoint.Y - _resizeStartPoint.Y) * scale;

            int newWidth = _resizeStartWidth;
            int newHeight = _resizeStartHeight;

            if (element.Name == "BottomResizeGrip")
            {
                newHeight = _resizeStartHeight + (int)diffY;
                int minHeight = (int)(200 * scale);
                if (newHeight < minHeight) newHeight = minHeight;
            }
            else if (element.Name == "LeftResizeGrip")
            {
                // Left resize behaves differently depending on whether anchor is left/right
                if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
                {
                    newWidth = _resizeStartWidth - (int)diffX;
                }
                else
                {
                    newWidth = _resizeStartWidth + (int)diffX;
                }

                int minWidth = (int)(300 * scale);
                int maxWidth = (int)(800 * scale);
                if (newWidth < minWidth) newWidth = minWidth;
                if (newWidth > maxWidth) newWidth = maxWidth;
            }

            _appWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
            _appBarHelper.SetPosition();
            e.Handled = true;
        }

        private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                var element = sender as FrameworkElement;
                element?.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        // --- Custom Title Bar Dragging ---
        private void TitleBar_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var element = sender as Border;
            if (element == null) return;

            _isDragging = true;
            element.CapturePointer(e.Pointer);

            var properties = e.GetCurrentPoint(null);
            _dragStartPoint = properties.Position;
            _dragStartWindowPos = _appWindow.Position;
            e.Handled = true;
        }

        private void TitleBar_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;

            var properties = e.GetCurrentPoint(null);
            var currentPoint = properties.Position;

            double scale = _appBarHelper.GetDpiScale();
            int diffX = (int)((currentPoint.X - _dragStartPoint.X) * scale);
            int diffY = (int)((currentPoint.Y - _dragStartPoint.Y) * scale);

            var newPos = new Windows.Graphics.PointInt32(
                _dragStartWindowPos.X + diffX,
                _dragStartWindowPos.Y + diffY
            );

            _appWindow.Move(newPos);
            e.Handled = true;
        }

        private void TitleBar_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                var element = sender as Border;
                element?.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            }
        }

        // --- Event Handlers to Prevent XAML Compilation Errors ---
        private void Reload_Click(object sender, RoutedEventArgs e) { }
        private void UserAgent_Click(object sender, RoutedEventArgs e) { }
        private void Star_Click(object sender, RoutedEventArgs e) { }
        private void CopyUrl_Click(object sender, RoutedEventArgs e) { }
        private void OpenExternal_Click(object sender, RoutedEventArgs e) { }
        private void ResetToInitialPage_Click(object sender, RoutedEventArgs e) { }
        private void CloseActivePane_Click(object sender, RoutedEventArgs e) { }
        private void AddSeparator_Click(object sender, RoutedEventArgs e) { }
        private void ThemeToggle_Click(object sender, RoutedEventArgs e) { }
        private void SidebarPositionToggle_Click(object sender, RoutedEventArgs e) { }
        private void ExternalBrowser_Click(object sender, RoutedEventArgs e) { }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                // AppWindow does not minimize easily when borderless, but we can call Show/Hide
                _appWindow.Hide();
            }
        }
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _appWindow.Hide();
        }

        public void RestoreWindow()
        {
            _appWindow.Show();
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }
            NativeMethods.SetForegroundWindow(_hwnd);
        }
        private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e) { }
        private void HeaderBack_Click(object sender, RoutedEventArgs e) { }
        private void HeaderForward_Click(object sender, RoutedEventArgs e) { }
        private void Tools_Click(object sender, RoutedEventArgs e) { }

        // Bookmark Drag / D&D Pointer Stubs
        private void Bookmark_PointerPressed(object sender, PointerRoutedEventArgs e) { }
        private void Bookmark_PointerMoved(object sender, PointerRoutedEventArgs e) { }
        private void Bookmark_PointerReleased(object sender, PointerRoutedEventArgs e) { }
    }
}
