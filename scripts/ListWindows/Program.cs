using System.Diagnostics;
using System.Text;
using System.Text.Json;

// List all visible top-level windows using Win32 EnumWindows
var windows = new List<Dictionary<string, object>>();

Win32.EnumWindows((hWnd, lParam) =>
{
    if (!Win32.IsWindowVisible(hWnd)) return true;

    var sb = new StringBuilder(256);
    Win32.GetWindowText(hWnd, sb, sb.Capacity);
    var title = sb.ToString();
    if (string.IsNullOrWhiteSpace(title)) return true;

    Win32.GetWindowThreadProcessId(hWnd, out uint pid);
    string processName = "";
    try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { }

    Win32.GetWindowRect(hWnd, out RECT rect);
    bool isMinimized = Win32.IsIconic(hWnd);
    bool isMaximized = Win32.IsZoomed(hWnd);

    windows.Add(new Dictionary<string, object>
    {
        ["handle"] = hWnd.ToInt64(),
        ["title"] = title,
        ["processName"] = processName,
        ["processId"] = pid,
        ["bounds"] = new { x = rect.Left, y = rect.Top, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top },
        ["isMinimized"] = isMinimized,
        ["isMaximized"] = isMaximized
    });
    return true;
}, IntPtr.Zero);

var json = JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
