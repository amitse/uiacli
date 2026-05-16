using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Uia.Overlay;

/// <summary>
/// Manages a transparent, always-on-top, click-through overlay window
/// that renders highlights, ghost cursor, and annotations.
/// </summary>
public class OverlayManager : IDisposable
{
    private Window? _window;
    private Canvas? _canvas;
    private Dispatcher? _dispatcher;
    private Thread? _staThread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Dictionary<string, UIElement> _elements = new();
    private Ellipse? _ghostCursor;
    private bool _autoCursor = true;
    private bool _enabled = true;

    public bool IsEnabled => _enabled;
    public bool AutoCursor { get => _autoCursor; set => _autoCursor = value; }

    public void Start()
    {
        _staThread = new Thread(() =>
        {
            _window = new Window
            {
                Title = "UIA Overlay",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight
            };

            _canvas = new Canvas();
            _window.Content = _canvas;

            _window.SourceInitialized += (s, e) =>
            {
                // Make click-through
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                MakeClickThrough(hwnd);
            };

            _window.Show();
            _dispatcher = Dispatcher.CurrentDispatcher;
            _ready.Set();
            Dispatcher.Run();
        });

        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.IsBackground = true;
        _staThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));

        // Create ghost cursor (initially hidden)
        InvokeOnUI(() =>
        {
            var stroke = new SolidColorBrush(Color.FromArgb(240, 0, 150, 255));
            stroke.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(80, 0, 150, 255));
            fill.Freeze();
            _ghostCursor = new Ellipse
            {
                Width = 40,
                Height = 40,
                Stroke = stroke,
                StrokeThickness = 4,
                Fill = fill,
                Visibility = Visibility.Collapsed
            };
            _canvas!.Children.Add(_ghostCursor);
        });
    }

    public void Stop()
    {
        _dispatcher?.InvokeShutdown();
        _staThread?.Join(2000);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        InvokeOnUI(() =>
        {
            if (_window != null)
                _window.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // --- Highlights ---

    public string AddHighlight(int x, int y, int width, int height, string style = "focus", int fadeMs = 2000, string? label = null)
    {
        if (!_enabled) return "";
        var id = $"hl-{Guid.NewGuid():N}";
        var (color, thickness) = GetStyleBrush(style);

        InvokeOnUI(() =>
        {
            // Convert screen coords to overlay canvas coords
            var ox = x - SystemParameters.VirtualScreenLeft;
            var oy = y - SystemParameters.VirtualScreenTop;

            var rect = new Rectangle
            {
                Width = width,
                Height = height,
                Stroke = color,
                StrokeThickness = thickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rect, ox);
            Canvas.SetTop(rect, oy);
            _canvas!.Children.Add(rect);
            _elements[id] = rect;

            if (label != null)
            {
                var text = CreateLabel(label, color, ox, oy - 20);
                _canvas.Children.Add(text);
                _elements[$"{id}-label"] = text;
            }

            if (fadeMs > 0)
            {
                ScheduleFade(id, fadeMs);
            }
        });

        return id;
    }

    public void RemoveHighlight(string id)
    {
        InvokeOnUI(() =>
        {
            if (_elements.TryGetValue(id, out var el))
            {
                _canvas!.Children.Remove(el);
                _elements.Remove(id);
            }
            if (_elements.TryGetValue($"{id}-label", out var lbl))
            {
                _canvas!.Children.Remove(lbl);
                _elements.Remove($"{id}-label");
            }
        });
    }

    // --- Ghost Cursor ---

    public void MoveGhostCursor(int x, int y, int animateMs = 200)
    {
        if (!_enabled) return;

        InvokeOnUI(() =>
        {
            if (_ghostCursor == null) return;
            _ghostCursor.Visibility = Visibility.Visible;

            var ox = x - SystemParameters.VirtualScreenLeft - 20;
            var oy = y - SystemParameters.VirtualScreenTop - 20;

            // Ensure initial position is set (NaN can't be animated)
            var currentLeft = Canvas.GetLeft(_ghostCursor);
            var currentTop = Canvas.GetTop(_ghostCursor);
            if (double.IsNaN(currentLeft)) Canvas.SetLeft(_ghostCursor, ox);
            if (double.IsNaN(currentTop)) Canvas.SetTop(_ghostCursor, oy);

            if (animateMs > 0 && !double.IsNaN(currentLeft) && !double.IsNaN(currentTop))
            {
                var xAnim = new DoubleAnimation(ox, TimeSpan.FromMilliseconds(animateMs))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
                var yAnim = new DoubleAnimation(oy, TimeSpan.FromMilliseconds(animateMs))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };

                _ghostCursor.BeginAnimation(Canvas.LeftProperty, xAnim);
                _ghostCursor.BeginAnimation(Canvas.TopProperty, yAnim);
            }
            else
            {
                Canvas.SetLeft(_ghostCursor, ox);
                Canvas.SetTop(_ghostCursor, oy);
            }
        });
    }

    public void HideGhostCursor()
    {
        InvokeOnUI(() =>
        {
            if (_ghostCursor != null)
                _ghostCursor.Visibility = Visibility.Collapsed;
        });
    }

    // --- Annotations ---

    public string AddAnnotation(string text, int x, int y, string style = "info", int fadeMs = 3000)
    {
        if (!_enabled) return "";
        var id = $"ann-{Guid.NewGuid():N}";
        var (bgColor, fgColor) = GetAnnotationColors(style);

        InvokeOnUI(() =>
        {
            var ox = x - SystemParameters.VirtualScreenLeft;
            var oy = y - SystemParameters.VirtualScreenTop;

            // Auto-position: offset to avoid covering the element
            ox += 10;
            oy -= 30;

            // Keep on screen
            if (ox + 200 > _canvas!.ActualWidth) ox = Math.Max(0, ox - 220);
            if (oy < 0) oy = y - SystemParameters.VirtualScreenTop + 30;

            var border = new Border
            {
                Background = bgColor,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = fgColor,
                    FontSize = 15,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.SemiBold,
                    MaxWidth = 400,
                    TextWrapping = TextWrapping.Wrap
                },
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 12,
                    Opacity = 0.4,
                    ShadowDepth = 3
                }
            };

            Canvas.SetLeft(border, ox);
            Canvas.SetTop(border, oy);
            _canvas.Children.Add(border);
            _elements[id] = border;

            if (fadeMs > 0)
            {
                ScheduleFade(id, fadeMs);
            }
        });

        return id;
    }

    public void RemoveAnnotation(string id) => RemoveHighlight(id); // same mechanism

    // --- Clear All ---

    public void ClearAll()
    {
        InvokeOnUI(() =>
        {
            // Keep ghost cursor, remove everything else
            _canvas!.Children.Clear();
            _elements.Clear();
            if (_ghostCursor != null)
            {
                _canvas.Children.Add(_ghostCursor);
                _ghostCursor.Visibility = Visibility.Collapsed;
            }
        });
    }

    // --- Internals ---

    private void InvokeOnUI(Action action)
    {
        if (_dispatcher == null || !_dispatcher.CheckAccess())
            _dispatcher?.Invoke(action);
        else
            action();
    }

    private void ScheduleFade(string id, int delayMs)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_elements.TryGetValue(id, out var el))
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (_, _) =>
                {
                    _canvas!.Children.Remove(el);
                    _elements.Remove(id);
                    if (_elements.TryGetValue($"{id}-label", out var lbl))
                    {
                        _canvas.Children.Remove(lbl);
                        _elements.Remove($"{id}-label");
                    }
                };
                el.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        };
        timer.Start();
    }

    private static TextBlock CreateLabel(string text, Brush color, double x, double y)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = color,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        return tb;
    }

    private static (SolidColorBrush color, double thickness) GetStyleBrush(string style)
    {
        var (brush, thickness) = style.ToLowerInvariant() switch
        {
            "focus" => (new SolidColorBrush(Color.FromRgb(0, 200, 255)), 3.0),
            "action" => (new SolidColorBrush(Color.FromRgb(255, 200, 0)), 3.0),
            "error" => (new SolidColorBrush(Color.FromRgb(255, 60, 60)), 3.0),
            "success" => (new SolidColorBrush(Color.FromRgb(60, 220, 60)), 3.0),
            _ => (new SolidColorBrush(Color.FromRgb(0, 200, 255)), 3.0)
        };
        brush.Freeze();
        return (brush, thickness);
    }

    private static (SolidColorBrush bg, SolidColorBrush fg) GetAnnotationColors(string style)
    {
        var (bg, fg) = style.ToLowerInvariant() switch
        {
            "info" => (new SolidColorBrush(Color.FromArgb(230, 30, 60, 120)), new SolidColorBrush(Colors.White)),
            "action" => (new SolidColorBrush(Color.FromArgb(230, 140, 110, 10)), new SolidColorBrush(Colors.White)),
            "reasoning" => (new SolidColorBrush(Color.FromArgb(230, 90, 40, 140)), new SolidColorBrush(Colors.White)),
            "warning" => (new SolidColorBrush(Color.FromArgb(230, 160, 40, 40)), new SolidColorBrush(Colors.White)),
            _ => (new SolidColorBrush(Color.FromArgb(230, 30, 60, 120)), new SolidColorBrush(Colors.White))
        };
        bg.Freeze();
        fg.Freeze();
        return (bg, fg);
    }

    private static void MakeClickThrough(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_LAYERED = 0x00080000;

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
