using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Screenshot <title-substring> [output.png]");
    return 1;
}

var search = args[0];
var outputPath = args.Length > 1 ? args[1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uia-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");

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
        return false;
    }
    return true;
}, IntPtr.Zero);

if (found == IntPtr.Zero)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = $"No window found matching '{search}'" }));
    return 1;
}

Win32.GetWindowRect(found, out RECT rect);
int width = rect.Right - rect.Left;
int height = rect.Bottom - rect.Top;

if (width <= 0 || height <= 0)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = "Window has zero size (minimized?)" }));
    return 1;
}

using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
{
    g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
}
bmp.Save(outputPath, ImageFormat.Png);

Console.WriteLine(JsonSerializer.Serialize(new
{
    ok = true,
    filePath = outputPath,
    window = foundTitle,
    width,
    height
}));
return 0;
