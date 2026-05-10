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
            if (_isRegistered) return;

            var helper = new WindowInteropHelper(_window);
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
            var helper = new WindowInteropHelper(_window);
            double dpi = GetDpiScale();

            var data = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = helper.Handle,
                uEdge = (int)NativeMethods.AppBarEdges.Right
            };

            // Set the desired width (e.g., 400 pixels)
            int width = (int)(_window.Width * dpi);
            int screenWidth = (int)(SystemParameters.PrimaryScreenWidth * dpi);
            int screenHeight = (int)(SystemParameters.PrimaryScreenHeight * dpi);
            
            data.rc.Top = 0;
            data.rc.Bottom = screenHeight;
            data.rc.Right = screenWidth;
            data.rc.Left = screenWidth - width;

            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.QueryPos, ref data);
            NativeMethods.SHAppBarMessage((int)NativeMethods.AppBarMessages.SetPos, ref data);

            NativeMethods.MoveWindow(data.hWnd, data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top, true);
        }

        private double GetDpiScale()
        {
            var source = PresentationSource.FromVisual(_window);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }
}
