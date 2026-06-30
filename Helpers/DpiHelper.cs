using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MugiSideBrowser.Helpers
{
    public static class DpiHelper
    {
        public static double GetDpiScale(Visual visual)
        {
            return VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        }

        public static double GetDpiScale(Window window)
        {
            var source = PresentationSource.FromVisual(window);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }

        public static NativeMethods.MONITORINFO GetMonitorInfo(IntPtr hWnd)
        {
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to get monitor handle from window.");
            }
            
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            
            bool success = NativeMethods.GetMonitorInfo(hMonitor, ref mi);
            if (!success)
            {
                throw new InvalidOperationException("Failed to get monitor info.");
            }
            
            return mi;
        }

        public static NativeMethods.MONITORINFO GetMonitorInfo(Window window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Window handle is not available.");
            }
            return GetMonitorInfo(helper.Handle);
        }

        public static System.Drawing.Point GetCursorPos()
        {
            var point = new System.Drawing.Point();
            NativeMethods.GetCursorPos(ref point);
            return point;
        }

        public static System.Windows.Point GetCursorPosAsWpfPoint(Visual visual)
        {
            var point = GetCursorPos();
            double dpi = GetDpiScale(visual);
            return new System.Windows.Point(point.X / dpi, point.Y / dpi);
        }
    }
}
