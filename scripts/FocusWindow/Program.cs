using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: FocusWindow <title-substring>");
    return 1;
}

var search = args[0];
IntPtr found = IntPtr.Zero;
string foundTitle = "";

Win32.EnumWindows((hWnd, lParam) =>
{
    if (!Win32.IsWindowVisible(hWnd)) return true;
    var sb = new StringBuilder(256);
    Win32.GetWindowText(hWnd, sb, sb.Capacity);
    var title = sb.ToString();
    if (title.Contains(search, StringComparison.OrdinalIgnoreCase))
    {
        found = hWnd;
        foundTitle = title;
        return false; // stop enumeration
    }
    return true;
}, IntPtr.Zero);

if (found == IntPtr.Zero)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = $"No window found matching '{search}'" }));
    return 1;
}

// Restore if minimized
if (Win32.IsIconic(found))
    Win32.ShowWindow(found, Win32.SW_RESTORE);

Win32.SetForegroundWindow(found);

Console.WriteLine(JsonSerializer.Serialize(new { ok = true, handle = found.ToInt64(), title = foundTitle }));
return 0;
