using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Uia.Core;
using Uia.Core.Models;
using Uia.Overlay;
using File = System.IO.File;
using Path = System.IO.Path;
using Timer = System.Threading.Timer;

var defaultPort = 9721;
var port = int.TryParse(Environment.GetEnvironmentVariable("UIACLI_PORT"), out int envPort) ? envPort : defaultPort;

// Parse --port from command line
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out int cliPort))
        port = cliPort;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://localhost:{port}");

builder.Services.AddSingleton<WindowManager>();
builder.Services.AddSingleton<ElementInspector>();
builder.Services.AddSingleton<InputSimulator>();
builder.Services.AddSingleton<ScreenCapture>();
builder.Services.AddSingleton<BatchExecutor>();
builder.Services.AddSingleton<ProcessManager>();

var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};

var app = builder.Build();
var startTime = DateTime.UtcNow;
var lastActivity = DateTime.UtcNow;
var idleTimeoutMinutes = 30;

// Start overlay
var overlay = new OverlayManager();
overlay.Start();

// Write PID and port files
var pidFile = Path.Combine(Path.GetTempPath(), "uia-server.pid");
var portFile = Path.Combine(Path.GetTempPath(), "uia-server.port");
File.WriteAllText(pidFile, Environment.ProcessId.ToString());
File.WriteAllText(portFile, port.ToString());

// Idle timeout checker
var idleTimer = new Timer(_ =>
{
    if ((DateTime.UtcNow - lastActivity).TotalMinutes > idleTimeoutMinutes)
    {
        Console.Error.WriteLine($"[uia-server] Idle timeout ({idleTimeoutMinutes}m) reached. Shutting down.");
        Environment.Exit(0);
    }
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

// Middleware to track activity
app.Use(async (ctx, next) =>
{
    lastActivity = DateTime.UtcNow;
    await next();
});

// --- Health ---
app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    pid = Environment.ProcessId,
    port,
    uptimeSeconds = (int)(DateTime.UtcNow - startTime).TotalSeconds,
    version = "0.1.0"
}, jsonOptions));

// --- State ---
app.MapGet("/state", (WindowManager wm, ElementInspector inspector) =>
{
    var windows = wm.ListWindows();
    var focused = wm.GetForegroundWindow();
    Win32Interop.GetCursorPos(out var cursorPos);

    ElementNode? focusedElement = null;
    try { focusedElement = inspector.GetFocusedElement(); } catch { }

    var screens = System.Windows.Forms.Screen.AllScreens.Select(s => new ScreenInfo
    {
        Name = s.DeviceName,
        Bounds = new BoundsInfo { X = s.Bounds.X, Y = s.Bounds.Y, Width = s.Bounds.Width, Height = s.Bounds.Height },
        IsPrimary = s.Primary
    }).ToList();

    return Results.Json(new DesktopState
    {
        Windows = windows,
        FocusedWindow = focused,
        FocusedElement = focusedElement,
        CursorPosition = new PointInfo { X = cursorPos.X, Y = cursorPos.Y },
        Screens = screens
    }, jsonOptions);
});

// --- Windows ---
app.MapGet("/windows", (WindowManager wm) =>
    Results.Json(wm.ListWindows(), jsonOptions));

app.MapGet("/windows/{handle:long}", (long handle, WindowManager wm) =>
{
    var win = wm.ListWindows().FirstOrDefault(w => w.Handle == handle);
    return win != null
        ? Results.Json(win, jsonOptions)
        : Results.Json(new { error = new ErrorInfo { Code = "WINDOW_NOT_FOUND", Message = $"Window handle {handle} not found" } }, jsonOptions, statusCode: 404);
});

app.MapPost("/windows/{handle:long}/focus", (long handle, WindowManager wm) =>
{
    var success = wm.FocusWindow(handle);
    return Results.Json(new { ok = success }, jsonOptions);
});

app.MapPost("/windows/{handle:long}/minimize", (long handle, WindowManager wm) =>
{
    wm.MinimizeWindow(handle);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapPost("/windows/{handle:long}/maximize", (long handle, WindowManager wm) =>
{
    wm.MaximizeWindow(handle);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapPost("/windows/{handle:long}/restore", (long handle, WindowManager wm) =>
{
    wm.RestoreWindow(handle);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapPost("/windows/{handle:long}/close", (long handle, WindowManager wm) =>
{
    wm.CloseWindow(handle);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapPost("/windows/{handle:long}/move", async (long handle, HttpRequest req, WindowManager wm) =>
{
    var body = await JsonSerializer.DeserializeAsync<MoveRequest>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid body");
    wm.MoveWindow(handle, body.X, body.Y, body.Width, body.Height);
    return Results.Json(new { ok = true }, jsonOptions);
});

// --- Tree & Elements ---
app.MapGet("/windows/{handle:long}/tree", (long handle, int? depth, int? maxElements, bool? includeOffscreen, ElementInspector inspector) =>
{
    try
    {
        var tree = inspector.GetTree(handle, depth ?? 3, maxElements ?? 500, includeOffscreen ?? false);
        return Results.Json(tree, jsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = new ErrorInfo { Code = "TREE_ERROR", Message = ex.Message } }, jsonOptions, statusCode: 500);
    }
});

app.MapGet("/windows/{handle:long}/find", (long handle, string? name, string? type, string? id, ElementInspector inspector) =>
{
    var query = new ElementQuery { Name = name, ControlType = type, AutomationId = id };
    var elements = inspector.FindElements(handle, query);
    return Results.Json(new { count = elements.Count, elements }, jsonOptions);
});

app.MapGet("/focused", (ElementInspector inspector) =>
{
    var el = inspector.GetFocusedElement();
    return el != null
        ? Results.Json(el, jsonOptions)
        : Results.Json(new { error = new ErrorInfo { Code = "NO_FOCUS", Message = "No focused element" } }, jsonOptions, statusCode: 404);
});

// --- Actions ---
app.MapPost("/action", async (HttpRequest req, BatchExecutor executor) =>
{
    var action = await JsonSerializer.DeserializeAsync<ActionRequest>(req.Body, jsonOptions);
    if (action == null) return Results.BadRequest("Invalid action");
    var batch = new BatchRequest { Actions = new List<ActionRequest> { action }, Window = action.Window };
    var result = executor.Execute(batch);
    return Results.Json(result.Results.FirstOrDefault(), jsonOptions);
});

app.MapPost("/batch", async (HttpRequest req, BatchExecutor executor, ElementInspector inspector) =>
{
    var batch = await JsonSerializer.DeserializeAsync<BatchRequest>(req.Body, jsonOptions);
    if (batch == null) return Results.BadRequest("Invalid batch");

    // Verbose mode: show overlay annotations before each action
    var isVerbose = batch.Verbose || overlay.AutoCursor;
    Action<int, ActionRequest, long?>? hook = null;

    if (isVerbose && overlay.IsEnabled)
    {
        hook = (index, action, windowHandle) =>
        {
            // Build description text
            var desc = action.Description;
            if (string.IsNullOrEmpty(desc))
                desc = BuildAutoDescription(action);

            // Find target coordinates for cursor + annotation
            int? targetX = action.X, targetY = action.Y;
            int? targetW = null, targetH = null;
            if (action.Element != null && windowHandle.HasValue)
            {
                try
                {
                    var el = inspector.FindRawElement(windowHandle.Value, action.Element);
                    if (el != null)
                    {
                        var rect = el.Current.BoundingRectangle;
                        if (!rect.IsEmpty)
                        {
                            targetX = (int)(rect.X + rect.Width / 2);
                            targetY = (int)(rect.Y + rect.Height / 2);
                            targetW = (int)rect.Width;
                            targetH = (int)rect.Height;
                        }
                    }
                }
                catch { }
            }

            if (targetX.HasValue && targetY.HasValue)
            {
                // Move ghost cursor to target
                if (overlay.AutoCursor)
                    overlay.MoveGhostCursor(targetX.Value, targetY.Value, 300);

                // Show highlight on target element
                if (targetW.HasValue && targetH.HasValue && batch.Verbose)
                {
                    overlay.AddHighlight(
                        targetX.Value - targetW.Value / 2,
                        targetY.Value - targetH.Value / 2,
                        targetW.Value, targetH.Value,
                        "action", 2500);
                }

                // Show description annotation
                if (!string.IsNullOrEmpty(desc) && batch.Verbose)
                {
                    overlay.AddAnnotation(desc,
                        targetX.Value, targetY.Value - (targetH ?? 0) / 2,
                        "action", 3000);
                }

                // Pause so human can see the visual before action executes
                if (batch.Verbose)
                    Thread.Sleep(400);
            }
            else if (!string.IsNullOrEmpty(desc) && batch.Verbose)
            {
                // No target coords — show annotation at top of screen
                overlay.AddAnnotation(desc, 100, 50, "info", 3000);
                Thread.Sleep(300);
            }
        };
    }

    var result = executor.Execute(batch, hook);

    // Hide ghost cursor after batch completes
    if (isVerbose && overlay.IsEnabled)
        overlay.HideGhostCursor();

    return Results.Json(result, jsonOptions);
});

// --- Wait ---
app.MapPost("/wait", async (HttpRequest req, BatchExecutor executor) =>
{
    var action = await JsonSerializer.DeserializeAsync<ActionRequest>(req.Body, jsonOptions);
    if (action == null) return Results.BadRequest("Invalid wait request");
    action.Type = "wait";
    var batch = new BatchRequest { Actions = new List<ActionRequest> { action } };
    var result = executor.Execute(batch);
    return Results.Json(result.Results.FirstOrDefault(), jsonOptions);
});

// --- Screenshot ---
app.MapPost("/screenshot", async (HttpRequest req, ScreenCapture capture) =>
{
    var body = await JsonSerializer.DeserializeAsync<ScreenshotRequest>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid request");
    try
    {
        CaptureResult result;
        if (body.Handle.HasValue)
            result = capture.CaptureWindow(body.Handle.Value);
        else if (body.X.HasValue && body.Y.HasValue && body.Width.HasValue && body.Height.HasValue)
            result = capture.CaptureRegion(body.X.Value, body.Y.Value, body.Width.Value, body.Height.Value);
        else
            return Results.Json(new { error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Provide handle or x,y,width,height" } }, jsonOptions, statusCode: 400);
        return Results.Json(result, jsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = new ErrorInfo { Code = "CAPTURE_ERROR", Message = ex.Message } }, jsonOptions, statusCode: 500);
    }
});

// --- Clipboard ---
app.MapGet("/clipboard", () =>
{
    string? text = null;
    var thread = new Thread(() => { try { text = System.Windows.Forms.Clipboard.GetText(); } catch { } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(2000);
    return Results.Json(new { text }, jsonOptions);
});

app.MapPost("/clipboard", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<ClipboardRequest>(req.Body, jsonOptions);
    if (body?.Text == null) return Results.BadRequest("Provide text");
    var thread = new Thread(() => { try { System.Windows.Forms.Clipboard.SetText(body.Text); } catch { } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(2000);
    return Results.Json(new { ok = true }, jsonOptions);
});

// --- Processes ---
app.MapGet("/processes", (ProcessManager pm) =>
    Results.Json(pm.ListProcesses(), jsonOptions));

app.MapPost("/launch", async (HttpRequest req, ProcessManager pm) =>
{
    var body = await JsonSerializer.DeserializeAsync<LaunchRequest>(req.Body, jsonOptions);
    if (body?.Path == null) return Results.BadRequest("Provide path");
    try
    {
        var info = pm.LaunchApp(body.Path);
        return Results.Json(info, jsonOptions);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = new ErrorInfo { Code = "LAUNCH_ERROR", Message = ex.Message } }, jsonOptions, statusCode: 500);
    }
});

// Cleanup on shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    overlay.Stop();
    try { File.Delete(pidFile); } catch { }
    try { File.Delete(portFile); } catch { }
    idleTimer.Dispose();
});

// --- Overlay Endpoints ---
app.MapPost("/overlay/highlight", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<HighlightRequest>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid request");
    var id = overlay.AddHighlight(body.X, body.Y, body.Width, body.Height,
        body.Style ?? "focus", body.FadeMs ?? 2000, body.Label);
    return Results.Json(new { ok = true, id }, jsonOptions);
});

app.MapDelete("/overlay/highlight/{id}", (string id) =>
{
    overlay.RemoveHighlight(id);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapPost("/overlay/annotate", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<AnnotateRequest>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid request");
    var id = overlay.AddAnnotation(body.Text ?? "", body.X, body.Y,
        body.Style ?? "info", body.FadeMs ?? 3000);
    return Results.Json(new { ok = true, id }, jsonOptions);
});

app.MapPost("/overlay/cursor", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<CursorMoveRequest>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid request");
    overlay.MoveGhostCursor(body.X, body.Y, body.AnimateMs ?? 200);
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapDelete("/overlay", () =>
{
    overlay.ClearAll();
    return Results.Json(new { ok = true }, jsonOptions);
});

app.MapGet("/overlay/config", () => Results.Json(new
{
    enabled = overlay.IsEnabled,
    autoCursor = overlay.AutoCursor
}, jsonOptions));

app.MapPut("/overlay/config", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<OverlayConfig>(req.Body, jsonOptions);
    if (body == null) return Results.BadRequest("Invalid request");
    if (body.Enabled.HasValue) overlay.SetEnabled(body.Enabled.Value);
    if (body.AutoCursor.HasValue) overlay.AutoCursor = body.AutoCursor.Value;
    return Results.Json(new { ok = true, enabled = overlay.IsEnabled, autoCursor = overlay.AutoCursor }, jsonOptions);
});

Console.Error.WriteLine($"[uia-server] Starting on http://localhost:{port} (PID {Environment.ProcessId})");
app.Run();

// Builds a human-readable description from an action when none is provided
static string BuildAutoDescription(ActionRequest action)
{
    var type = action.Type?.ToLowerInvariant() ?? "";
    var elementName = action.Element?.Name ?? action.Element?.AutomationId ?? "";

    return type switch
    {
        "click" when !string.IsNullOrEmpty(elementName) => $"Clicking {elementName}",
        "click" when action.X.HasValue => $"Clicking at ({action.X}, {action.Y})",
        "rightclick" => $"Right-clicking {elementName}",
        "doubleclick" => $"Double-clicking {elementName}",
        "type" => $"Typing \"{Truncate(action.Text, 30)}\"",
        "key" or "keypress" => $"Pressing {action.Combo ?? action.Text}",
        "focus" => $"Focusing {action.Window ?? action.Text}",
        "scroll" => $"Scrolling {(action.Clicks > 0 ? "down" : "up")}",
        "select" => $"Selecting \"{action.Value}\"",
        "toggle" => $"Toggling {elementName}",
        "drag" => $"Dragging to ({action.ToX}, {action.ToY})",
        "wait" => "Waiting...",
        "read" => $"Reading {elementName}",
        "screenshot" => "Taking screenshot",
        "scrollintoview" or "scroll_into_view" => $"Scrolling {elementName} into view",
        "freehand" => $"Drawing freehand ({action.Points?.Count ?? 0} points)",
        _ => type
    };
}

static string Truncate(string? s, int max) =>
    s == null ? "" : s.Length <= max ? s : s[..max] + "...";

// --- Helper types for request bodies ---
record MoveRequest(int X, int Y, int Width, int Height);
record ScreenshotRequest(long? Handle, int? X, int? Y, int? Width, int? Height);
record ClipboardRequest(string? Text);
record LaunchRequest(string? Path);
record HighlightRequest(int X, int Y, int Width, int Height, string? Style, int? FadeMs, string? Label);
record AnnotateRequest(string? Text, int X, int Y, string? Style, int? FadeMs);
record CursorMoveRequest(int X, int Y, int? AnimateMs);
record OverlayConfig(bool? Enabled, bool? AutoCursor);

// P/Invoke for cursor position
static class Win32Interop
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetCursorPos(out CursorPoint lpPoint);

    public struct CursorPoint { public int X, Y; }
}
