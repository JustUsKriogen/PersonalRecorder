using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PersonalRecorder;

public sealed record AppWindow(nint Handle, uint ProcessId, string ProcessName, string Title)
{
    public string Label => $"{Title}  ·  {ProcessName}";
}

public sealed record DisplayInfo(string Device, int Width, int Height, bool Primary)
{
    public string Label => $"{Device.Replace(@"\\.\", "")}  ·  {Width} × {Height}{(Primary ? "  (primary)" : "")}";
}

internal static class NativeDesktop
{
    private delegate bool WindowCallback(nint hwnd, nint parameter);
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    internal static void PlaceInCorner(nint hwnd, int right, int bottom)
    {
        if (GetWindowRect(hwnd, out var rect))
            SetWindowPos(hwnd, 0, right - (rect.Right - rect.Left) - 12, bottom - (rect.Bottom - rect.Top) - 12, 0, 0, 0x15);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    public static List<AppWindow> Windows()
    {
        var result = new List<AppWindow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return true;
            var title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                result.Add(new(hwnd, pid, process.ProcessName, title.ToString()));
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        return result.OrderByDescending(w => w.ProcessName.Contains("League", StringComparison.OrdinalIgnoreCase))
            .ThenBy(w => w.Title).ToList();
    }

    public static bool StillExists(AppWindow window)
    {
        GetWindowThreadProcessId(window.Handle, out var pid);
        return IsWindow(window.Handle) && pid == window.ProcessId;
    }

    public static List<DisplayInfo> Displays()
    {
        var result = new List<DisplayInfo>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new(info.Device, info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top, (info.Flags & 1) != 0));
            return true;
        }, 0);
        return result.OrderByDescending(d => d.Primary).ToList();
    }
}
