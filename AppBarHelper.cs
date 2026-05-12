using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MugiSideBrowser
{
    public class AppBarHelper
    {
        private readonly Window _window;
        private bool _isRegistered;
        private readonly int _uCallbackMessage;
        public NativeMethods.AppBarEdges Edge { get; set; } = NativeMethods.AppBarEdges.Right;


        public AppBarHelper(Window window)
        {
            _window = window;
            _uCallbackMessage = (int)NativeMethods.RegisterWindowMessage("AppBarMessage");
        }

        public void Register()
        {
            // 強制的に一度解除を試みる（多重予約を確実に防ぐため、フラグを無視して実行）
            var helper = new WindowInteropHelper(_window);
            var removeData = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle
            };
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.Remove, ref removeData);
            _isRegistered = false;

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle,
                uCallbackMessage = _uCallbackMessage
            };

            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.New, ref data);
            _isRegistered = true;

            SetPosition();
        }

        public void Unregister()
        {
            if (!_isRegistered) return;

            var helper = new WindowInteropHelper(_window);
            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle
            };

            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.Remove, ref data);
            _isRegistered = false;
        }

        public void SetPosition()
        {
            if (!_isRegistered) return;

            var helper = new WindowInteropHelper(_window);
            double dpi = GetDpiScale();

            // ウィンドウが現在いるモニター、または最適なモニターを取得
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle,
                uEdge = (int)Edge
            };

            // 目標とする幅（ピクセル単位）
            int width = (int)(_window.Width * dpi);
            
            // 選択されたモニターの矩形（rcMonitor = 全体, rcWork = タスクバー等を除いた領域）
            // AppBarは画面全体の端に予約するため rcMonitor を基準にする
            data.rc.Top = mi.rcMonitor.Top;
            data.rc.Bottom = mi.rcMonitor.Bottom;

            if (Edge == NativeMethods.AppBarEdges.Left)
            {
                data.rc.Left = mi.rcMonitor.Left;
                data.rc.Right = mi.rcMonitor.Left + width;
            }
            else
            {
                data.rc.Right = mi.rcMonitor.Right;
                data.rc.Left = mi.rcMonitor.Right - width;
            }

            // 1. 領域の問い合わせ
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.QueryPos, ref data);

            // 2. 領域の設定（他AppBarとの重複が調整される）
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.SetPos, ref data);

            // 3. Win32 APIによるウィンドウ移動
            NativeMethods.MoveWindow(data.hWnd, data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top, true);

            // 4. WPFプロパティへの反映
            _window.Left = data.rc.Left / dpi;
            _window.Top = data.rc.Top / dpi;
            _window.Width = (data.rc.Right - data.rc.Left) / dpi;
            _window.Height = (data.rc.Bottom - data.rc.Top) / dpi;
        }

        private double GetDpiScale()
        {
            var source = PresentationSource.FromVisual(_window);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }
}
