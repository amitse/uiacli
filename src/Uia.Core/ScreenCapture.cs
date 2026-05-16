using System.Drawing;
using System.Drawing.Imaging;
using Uia.Core.Models;

namespace Uia.Core;

public class ScreenCapture
{
    private readonly string _outputDir;

    public ScreenCapture(string? outputDir = null)
    {
        _outputDir = outputDir ?? System.IO.Path.GetTempPath();
    }

    public CaptureResult CaptureWindow(long handle)
    {
        Win32.GetWindowRect(new IntPtr(handle), out RECT rect);
        return CaptureRegion(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public CaptureResult CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Region has zero or negative size");

        var filePath = System.IO.Path.Combine(_outputDir, $"uia-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        bmp.Save(filePath, ImageFormat.Png);

        return new CaptureResult { FilePath = filePath, Width = width, Height = height };
    }

    public CaptureResult CaptureElement(System.Windows.Automation.AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty)
            throw new InvalidOperationException("Element has no bounds");

        return CaptureRegion((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
    }
}
