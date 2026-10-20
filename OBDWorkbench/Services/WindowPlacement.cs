using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OBDWorkbench.Services;

/// <summary>Place native windows using the selected monitor's physical work area.
/// WPF's SystemParameters.WorkArea describes the primary display, which is not
/// sufficient when launching on a secondary monitor or using mixed DPI scales.</summary>
internal static class WindowPlacement
{
    public static void Attach(Window window, double preferredWidth, double preferredHeight,
        bool maximize, Func<Window?>? reference = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            Window? anchor = reference?.Invoke();
            IntPtr anchorHandle = anchor is null ? IntPtr.Zero : new WindowInteropHelper(anchor).Handle;
            IntPtr monitor;
            if (anchorHandle != IntPtr.Zero)
                monitor = MonitorFromWindow(anchorHandle, 2);
            else if (GetCursorPos(out POINT cursor))
                monitor = MonitorFromPoint(cursor, 2);
            else monitor = MonitorFromWindow(hwnd, 2);
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return;

            uint dpiX = 96, dpiY = 96;
            try { if (GetDpiForMonitor(monitor, 0, out uint x, out uint y) == 0) { dpiX = x; dpiY = y; } }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            int availableWidth = info.Work.Right - info.Work.Left;
            int availableHeight = info.Work.Bottom - info.Work.Top;
            int margin = Math.Max(4, (int)Math.Round(8 * dpiX / 96.0));
            int width = Math.Max(1, Math.Min((int)Math.Round(preferredWidth * dpiX / 96.0), availableWidth - margin * 2));
            int height = Math.Max(1, Math.Min((int)Math.Round(preferredHeight * dpiY / 96.0), availableHeight - margin * 2));
            // Do not impose MaxWidth/MaxHeight: they would constrain a later maximize
            // on a larger display. Native coordinates also preserve negative origins.
            window.MinWidth = Math.Min(window.MinWidth, width * 96.0 / dpiX);
            window.MinHeight = Math.Min(window.MinHeight, height * 96.0 / dpiY);
            SetWindowPos(hwnd, IntPtr.Zero,
                info.Work.Left + (availableWidth - width) / 2,
                info.Work.Top + (availableHeight - height) / 2,
                width, height, 0x0004 | 0x0010);
            if (maximize) window.WindowState = WindowState.Maximized;
        };
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
