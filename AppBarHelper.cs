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

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle,
                uEdge = (int)NativeMethods.AppBarEdges.Right
            };

            // 目標とする幅（論理ピクセルから物理ピクセルへ）
            int width = (int)(_window.Width * dpi);
            
            // 物理ピクセル単位での画面解像度を取得
            int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            
            data.rc.Top = 0;
            data.rc.Bottom = screenHeight;
            data.rc.Right = screenWidth;
            data.rc.Left = screenWidth - width;

            // 1. 領域の問い合わせ
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.QueryPos, ref data);

            // 2. 領域の設定（他AppBarとの重複が調整される）
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.SetPos, ref data);

            // 3. Win32 APIによるウィンドウ移動
            NativeMethods.MoveWindow(data.hWnd, data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top, true);

            // 4. WPFプロパティへの反映（復帰後のWPFによる自動位置復元を上書きする）
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
