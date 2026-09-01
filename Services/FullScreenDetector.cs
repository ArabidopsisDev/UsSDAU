using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UsSDAU.Services;

public static class FullScreenDetector
{
    public static bool IsForegroundFullScreen()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == GetShellWindow()) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;
        if (!GetWindowRect(window, out var rect)) return false;
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        const int tolerance = 2;
        return rect.Left <= info.Monitor.Left + tolerance && rect.Top <= info.Monitor.Top + tolerance &&
               rect.Right >= info.Monitor.Right - tolerance && rect.Bottom >= info.Monitor.Bottom - tolerance;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
