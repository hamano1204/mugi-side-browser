using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace MugiSideBrowser.WinUI3
{
    public class AppBarHelper
    {
        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly AppWindow _appWindow;
        private bool _isRegistered;
        public bool IsRegistered => _isRegistered;
        private readonly int _uCallbackMessage;
        public NativeMethods.AppBarEdges Edge { get; set; } = NativeMethods.AppBarEdges.Right;

        public AppBarHelper(Window window)
        {
            _window = window;
            _hwnd = WindowNative.GetWindowHandle(_window);
            
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            _uCallbackMessage = (int)NativeMethods.RegisterWindowMessage("AppBarMessage");
        }

        public void Register()
        {
            // 強制的に一度解除を試みる（多重予約を確実に防ぐため、フラグを無視して実行）
            var removeData = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd
            };
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.Remove, ref removeData);
            _isRegistered = false;

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd,
                uCallbackMessage = _uCallbackMessage
            };

            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.New, ref data);
            _isRegistered = true;

            SetPosition();
        }

        public void Unregister()
        {
            if (!_isRegistered) return;

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd
            };

            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.Remove, ref data);
            _isRegistered = false;
        }

        private NativeMethods.RECT _currentMonitorRect;
        private NativeMethods.RECT _currentWorkAreaRect;
        private bool _hasMonitorInfo = false;

        public NativeMethods.RECT CurrentMonitorRect
        {
            get
            {
                if (!_hasMonitorInfo)
                {
                    QueryMonitorInfo();
                }
                return _currentMonitorRect;
            }
        }

        public NativeMethods.RECT CurrentWorkAreaRect
        {
            get
            {
                if (!_hasMonitorInfo)
                {
                    QueryMonitorInfo();
                }
                return _currentWorkAreaRect;
            }
        }

        private void QueryMonitorInfo()
        {
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                _currentMonitorRect = mi.rcMonitor;
                _currentWorkAreaRect = mi.rcWork;
                _hasMonitorInfo = true;
            }
        }

        public void SetPosition()
        {
            if (!_isRegistered) return;

            double dpi = GetDpiScale();

            // まだモニター情報がない場合、または最新の情報を取得したい場合に取得
            if (!_hasMonitorInfo)
            {
                QueryMonitorInfo();
                if (!_hasMonitorInfo)
                {
                    return;
                }
            }

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd,
                uEdge = (int)Edge
            };

            // 目標とする幅（ピクセル単位）
            // WinUI 3 Window does not expose logical Width directly in a standard property, 
            // so we track it via a helper property on MainWindow or _appWindow.Size.Width
            int width = (int)(_appWindow.Size.Width); // AppWindow.Size.Width is already in physical pixels
            
            // 保持している作業領域（タスクバー除外）を基準にする
            data.rc.Top = _currentWorkAreaRect.Top;
            data.rc.Bottom = _currentWorkAreaRect.Bottom;

            if (Edge == NativeMethods.AppBarEdges.Left)
            {
                data.rc.Left = _currentWorkAreaRect.Left;
                data.rc.Right = _currentWorkAreaRect.Left + width;
            }
            else
            {
                data.rc.Right = _currentWorkAreaRect.Right;
                data.rc.Left = _currentWorkAreaRect.Right - width;
            }

            // 1. 領域の問い合わせ
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.QueryPos, ref data);

            // 2. 領域の設定
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.SetPos, ref data);

            // 3. WinUI 3 AppWindowによる移動とリサイズ
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                data.rc.Left, 
                data.rc.Top, 
                data.rc.Right - data.rc.Left, 
                data.rc.Bottom - data.rc.Top));
        }

        // モニター情報をリセット（モニターを跨いだ移動時などに呼ぶ）
        public void ResetMonitorInfo()
        {
            _hasMonitorInfo = false;
        }

        public double GetDpiScale()
        {
            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            return dpi / 96.0;
        }
    }
}
