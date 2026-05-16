using System.Diagnostics;
using System.Text;
using Uia.Core.Models;

namespace Uia.Core;

public class WindowManager
{
    public List<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        var foreground = Win32.GetForegroundWindow();

        Win32.EnumWindows((hWnd, _) =>
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

            windows.Add(new WindowInfo
            {
                Handle = hWnd.ToInt64(),
                Title = title,
                ProcessName = processName,
                ProcessId = (int)pid,
                Bounds = new BoundsInfo
                {
                    X = rect.Left, Y = rect.Top,
                    Width = rect.Right - rect.Left,
                    Height = rect.Bottom - rect.Top
                },
                IsMinimized = Win32.IsIconic(hWnd),
                IsMaximized = Win32.IsZoomed(hWnd),
                HasFocus = hWnd == foreground
            });
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public WindowInfo? FindWindow(string query)
    {
        // Try handle first
        if (long.TryParse(query, out long handle) || 
            (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && 
             long.TryParse(query[2..], System.Globalization.NumberStyles.HexNumber, null, out handle)))
        {
            return ListWindows().FirstOrDefault(w => w.Handle == handle);
        }

        // Try process name exact match, then title substring
        var all = ListWindows();
        var byProcess = all.FirstOrDefault(w => 
            w.ProcessName.Equals(query, StringComparison.OrdinalIgnoreCase));
        if (byProcess != null) return byProcess;

        return all.FirstOrDefault(w => 
            w.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public List<WindowInfo> FindWindows(string query)
    {
        return ListWindows().Where(w =>
            w.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            w.ProcessName.Equals(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    public bool FocusWindow(IntPtr hWnd)
    {
        if (Win32.IsIconic(hWnd))
            Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
        return Win32.SetForegroundWindow(hWnd);
    }

    public bool FocusWindow(long handle) => FocusWindow(new IntPtr(handle));

    public void MinimizeWindow(long handle) =>
        Win32.ShowWindow(new IntPtr(handle), Win32.SW_MINIMIZE);

    public void MaximizeWindow(long handle) =>
        Win32.ShowWindow(new IntPtr(handle), Win32.SW_MAXIMIZE);

    public void RestoreWindow(long handle) =>
        Win32.ShowWindow(new IntPtr(handle), Win32.SW_RESTORE);

    public void CloseWindow(long handle) =>
        Win32.PostMessage(new IntPtr(handle), Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public void MoveWindow(long handle, int x, int y, int width, int height) =>
        Win32.MoveWindow(new IntPtr(handle), x, y, width, height, true);

    public WindowInfo? GetForegroundWindow()
    {
        var hWnd = Win32.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;
        return ListWindows().FirstOrDefault(w => w.Handle == hWnd.ToInt64());
    }
}
