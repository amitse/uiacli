using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var jsonOptions = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};

// --- Output helpers: ALL output goes through these ---
static void WriteSuccess(object data, JsonSerializerOptions opts)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, opts));
    Environment.ExitCode = 0;
}

static void WriteError(string code, string message, string? hint, JsonSerializerOptions opts, int exitCode = 1)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ok = false,
        error = new { code, message, hint }
    }, opts));
    Environment.ExitCode = exitCode;
}

static void WriteRaw(string json)
{
    // Pass through server JSON responses (already structured)
    Console.WriteLine(json);
}

static void Info(string message)
{
    // Human-only progress info on stderr (agents ignore stderr)
    Console.Error.WriteLine($"[uia] {message}");
}

var portOption = new Option<int>("--port", () =>
{
    var envPort = Environment.GetEnvironmentVariable("UIACLI_PORT");
    return int.TryParse(envPort, out int p) ? p : 9721;
}, "Server port (default: 9721, env: UIACLI_PORT)");

var rootCommand = new RootCommand(
@"UIA CLI — Windows UI Automation tool for agents

Automate any Windows desktop application: list windows, inspect elements,
click, type, drag, screenshot, and more. All output is JSON.

Quick start:
  uia windows                              List all open windows
  uia tree Calculator --depth 2            Inspect Calculator's UI tree
  uia click --window Calculator --name Five Click the ""5"" button
  uia batch actions.json --verbose          Run a batch with overlay visuals

The server auto-starts on first use. To start it manually:
  uia serve --background

Exit codes: 0=success, 1=action failed, 2=setup error, 3=timeout")
{
    portOption
};

// ==================== SERVE ====================
var serveCommand = new Command("serve",
@"Start the UIA server (HTTP API on localhost).
The server hosts the UIA automation context and overlay window.
Other commands auto-start it, so you rarely need this.

Examples:
  uia serve                    Start in foreground (Ctrl+C to stop)
  uia serve --background       Start detached, return immediately
  uia serve --port 8080        Use custom port");
var bgOption = new Option<bool>("--background", "Start server in background and return immediately");
serveCommand.AddOption(bgOption);
serveCommand.SetHandler(async (int port, bool background) =>
{
    var serverPath = FindServerExe();
    if (serverPath == null)
    {
        WriteError("SERVER_NOT_FOUND", "Cannot find Uia.Server project.",
            "Ensure you're running from the uiacli repo root, or set UIACLI_SERVER_PATH.", jsonOptions, 2);
        return;
    }

    if (background)
    {
        Info($"Starting server on port {port} in background...");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{serverPath}\" -- --port {port}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Process.Start(psi);
        if (!await WaitForServer(port))
        {
            WriteError("SERVER_START_FAILED", $"Server did not become healthy within 10s on port {port}.",
                "Check if another process is using the port. Try: uia serve (foreground) to see errors.", jsonOptions, 3);
            return;
        }
        var health = await CallGet(port, "/health");
        WriteRaw(health);
    }
    else
    {
        Info($"Starting server on port {port} (foreground)...");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{serverPath}\" -- --port {port}",
            UseShellExecute = false
        };
        var proc = Process.Start(psi);
        proc?.WaitForExit();
        Environment.ExitCode = proc?.ExitCode ?? 1;
    }
}, portOption, bgOption);

// ==================== STOP ====================
var stopCommand = new Command("stop",
@"Stop the UIA server.

Examples:
  uia stop                     Stop server on default port
  uia stop --port 8080         Stop server on custom port");
stopCommand.SetHandler(async (int port) =>
{
    var pidFile = Path.Combine(Path.GetTempPath(), "uia-server.pid");
    if (!File.Exists(pidFile))
    {
        WriteError("SERVER_NOT_RUNNING", "No server PID file found. Server may not be running.",
            "Use 'uia serve' to start it.", jsonOptions);
        return;
    }

    var pidStr = await File.ReadAllTextAsync(pidFile);
    if (int.TryParse(pidStr.Trim(), out int pid))
    {
        try
        {
            Process.GetProcessById(pid).Kill();
            Info($"Server (PID {pid}) stopped.");
        }
        catch
        {
            Info($"Server process {pid} was not running.");
        }
    }
    try { File.Delete(pidFile); } catch { }
    try { File.Delete(Path.Combine(Path.GetTempPath(), "uia-server.port")); } catch { }
    WriteSuccess(new { stopped = true, pid }, jsonOptions);
}, portOption);

// ==================== STATE ====================
var stateCommand = new Command("state",
@"Get a complete desktop state snapshot in one call.
Returns: all windows, focused window, focused element, cursor position, screens.

Examples:
  uia state                    Full desktop snapshot");
stateCommand.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallGet(port, "/state"));
}, portOption);

// ==================== WINDOWS ====================
var windowsCommand = new Command("windows",
@"List all visible top-level windows.
Returns: handle, title, processName, processId, bounds, isMinimized, isMaximized, hasFocus.

Examples:
  uia windows                  List all windows");
windowsCommand.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallGet(port, "/windows"));
}, portOption);

// ==================== FOCUS ====================
var focusCommand = new Command("focus",
@"Bring a window to the foreground.
Accepts: window title (substring match), process name, or handle.
If the window is minimized, it will be restored first.

Examples:
  uia focus Calculator         Focus by title substring
  uia focus msedge             Focus by process name
  uia focus 12345678           Focus by window handle");
var windowArg = new Argument<string>("window", "Window title (substring), process name, or handle");
focusCommand.AddArgument(windowArg);
focusCommand.SetHandler(async (string window, int port) =>
{
    if (!await EnsureServer(port)) return;
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return; // error already written
    WriteRaw(await CallPost(port, $"/windows/{handle}/focus", "{}"));
}, windowArg, portOption);

// ==================== TREE ====================
var treeCommand = new Command("tree",
@"Print the UI Automation tree of a window.
Returns a nested JSON hierarchy of elements with: name, controlType,
automationId, bounds, isEnabled, value, and supported patterns.

Examples:
  uia tree Calculator                    Default depth (3)
  uia tree Calculator --depth 5         Deeper tree
  uia tree ""Visual Studio Code""         Quote titles with spaces");
var treeWindowArg = new Argument<string>("window", "Window title (substring), process name, or handle");
var depthOption = new Option<int>("--depth", () => 3, "Maximum tree depth (default: 3)");
treeCommand.AddArgument(treeWindowArg);
treeCommand.AddOption(depthOption);
treeCommand.SetHandler(async (string window, int depth, int port) =>
{
    if (!await EnsureServer(port)) return;
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return;
    WriteRaw(await CallGet(port, $"/windows/{handle}/tree?depth={depth}"));
}, treeWindowArg, depthOption, portOption);

// ==================== FIND ====================
var findCommand = new Command("find",
@"Find elements in a window by name, control type, or automation ID.
Returns matching elements with their properties and bounds.
At least one filter (--name, --type, or --id) is recommended.

Examples:
  uia find Calculator --name Equals                Find by name
  uia find Calculator --type Button                All buttons
  uia find Calculator --name Five --type Button    Combined filters
  uia find Calculator --id num5Button              By automation ID");
var findWindowArg = new Argument<string>("window", "Window title (substring), process name, or handle");
var nameOpt = new Option<string?>("--name", "Element name to search for");
var typeOpt = new Option<string?>("--type", "Control type (Button, Edit, Text, Hyperlink, etc.)");
var idOpt = new Option<string?>("--id", "Automation ID (developer-assigned, most stable)");
findCommand.AddArgument(findWindowArg);
findCommand.AddOption(nameOpt);
findCommand.AddOption(typeOpt);
findCommand.AddOption(idOpt);
findCommand.SetHandler(async (string window, string? name, string? type, string? id, int port) =>
{
    if (!await EnsureServer(port)) return;
    if (name == null && type == null && id == null)
    {
        WriteError("NO_FILTER", "Provide at least one filter: --name, --type, or --id.",
            "Example: uia find Calculator --name Equals --type Button", jsonOptions);
        return;
    }
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return;
    var qs = new List<string>();
    if (name != null) qs.Add($"name={Uri.EscapeDataString(name)}");
    if (type != null) qs.Add($"type={Uri.EscapeDataString(type)}");
    if (id != null) qs.Add($"id={Uri.EscapeDataString(id)}");
    WriteRaw(await CallGet(port, $"/windows/{handle}/find?{string.Join('&', qs)}"));
}, findWindowArg, nameOpt, typeOpt, idOpt, portOption);

// ==================== FOCUSED ====================
var focusedCommand = new Command("focused",
@"Get the currently focused (keyboard-active) element.
Useful for knowing where keyboard input will go.

Examples:
  uia focused                  Get focused element info");
focusedCommand.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallGet(port, "/focused"));
}, portOption);

// ==================== CLICK ====================
var clickCommand = new Command("click",
@"Click at screen coordinates or on a named element.
Prefers UIA InvokePattern (fast, no cursor move) when available,
falls back to SendInput (moves real cursor) for custom controls.

Examples:
  uia click --x 500 --y 300                        Click at coordinates
  uia click --window Calculator --name Five         Click element by name
  uia click --window Calculator --id num5Button     Click by automation ID
  uia click --window Calculator --name Equals --type Button");
var clickXOpt = new Option<int?>("--x", "Screen X coordinate");
var clickYOpt = new Option<int?>("--y", "Screen Y coordinate");
var clickNameOpt = new Option<string?>("--name", "Element name to click");
var clickTypeOpt = new Option<string?>("--type", "Element control type");
var clickIdOpt = new Option<string?>("--id", "Element automation ID");
var clickWindowOpt = new Option<string?>("--window", "Window context (required when using --name/--type/--id)");
clickCommand.AddOption(clickXOpt); clickCommand.AddOption(clickYOpt);
clickCommand.AddOption(clickNameOpt); clickCommand.AddOption(clickTypeOpt);
clickCommand.AddOption(clickIdOpt); clickCommand.AddOption(clickWindowOpt);
clickCommand.SetHandler(async (int? x, int? y, string? name, string? type, string? id, string? window, int port) =>
{
    if (!await EnsureServer(port)) return;
    bool hasCoords = x.HasValue && y.HasValue;
    bool hasElement = name != null || type != null || id != null;

    if (!hasCoords && !hasElement)
    {
        WriteError("NO_TARGET", "Click requires either coordinates (--x, --y) or an element query (--name, --type, --id).",
            "Examples:\n  uia click --x 500 --y 300\n  uia click --window Calculator --name Five", jsonOptions);
        return;
    }
    if (hasElement && window == null)
    {
        WriteError("NO_WINDOW", "When clicking by element name/type/id, --window is required.",
            "Example: uia click --window Calculator --name Five", jsonOptions);
        return;
    }

    var action = new Dictionary<string, object?> { ["type"] = "click" };
    if (x.HasValue) action["x"] = x.Value;
    if (y.HasValue) action["y"] = y.Value;
    if (hasElement)
        action["element"] = new { name, controlType = type, automationId = id };
    if (window != null) action["window"] = window;
    WriteRaw(await CallPost(port, "/action", JsonSerializer.Serialize(action, jsonOptions)));
}, clickXOpt, clickYOpt, clickNameOpt, clickTypeOpt, clickIdOpt, clickWindowOpt, portOption);

// ==================== TYPE ====================
var typeCommand = new Command("type",
@"Type text into the focused element.
Methods: auto (default), keys (character-by-character), paste (clipboard), value (UIA ValuePattern).

Examples:
  uia type ""Hello World""                  Type using auto-detected method
  uia type ""Long text here"" --method paste  Use clipboard paste (faster for long text)
  uia type ""password123"" --method keys      Force keystroke simulation");
var typeTextArg = new Argument<string>("text", "Text to type");
var typeMethodOpt = new Option<string>("--method", () => "auto", "Input method: auto, keys, paste, value");
typeCommand.AddArgument(typeTextArg);
typeCommand.AddOption(typeMethodOpt);
typeCommand.SetHandler(async (string text, string method, int port) =>
{
    if (!await EnsureServer(port)) return;
    var action = new { type = "type", text, method };
    WriteRaw(await CallPost(port, "/action", JsonSerializer.Serialize(action, jsonOptions)));
}, typeTextArg, typeMethodOpt, portOption);

// ==================== KEY ====================
var keyCommand = new Command("key",
@"Press a key combination.
Format: modifier+key (e.g., ctrl+c, alt+f4, shift+tab).
Supported modifiers: ctrl, alt, shift, win.
Supported keys: enter, tab, escape, space, backspace, delete,
  up, down, left, right, home, end, pageup, pagedown,
  f1-f12, a-z, 0-9.

Examples:
  uia key enter                Press Enter
  uia key ctrl+c               Copy
  uia key alt+f4               Close window
  uia key ctrl+shift+s         Save As");
var keyComboArg = new Argument<string>("combo", "Key combination (e.g., ctrl+c, alt+f4, enter)");
keyCommand.AddArgument(keyComboArg);
keyCommand.SetHandler(async (string combo, int port) =>
{
    if (!await EnsureServer(port)) return;
    var action = new { type = "key", combo };
    WriteRaw(await CallPost(port, "/action", JsonSerializer.Serialize(action, jsonOptions)));
}, keyComboArg, portOption);

// ==================== BATCH ====================
var batchCommand = new Command("batch",
@"Execute multiple actions in one call (fastest way to automate).
Input: JSON file path, inline JSON, or stdin.
Supports: click, type, key, scroll, drag, wait, read, focus, screenshot.

Batch JSON format:
  {""actions"": [{""type"": ""click"", ""element"": {""name"": ""OK""}, ""description"": ""...""}],
   ""window"": ""AppName"", ""onError"": ""stop"", ""verbose"": false}

Or just an array (window must be in each action):
  [{""type"": ""click"", ""element"": {""name"": ""OK""}, ""window"": ""App""}]

Examples:
  uia batch actions.json                 From file
  uia batch actions.json --verbose       With overlay visuals
  cat actions.json | uia batch           From stdin");
var batchArg = new Argument<string?>("json", () => null, "JSON file path or inline JSON");
var verboseOpt = new Option<bool>("--verbose", "Show overlay annotations for each action");
batchCommand.AddArgument(batchArg);
batchCommand.AddOption(verboseOpt);
batchCommand.SetHandler(async (string? json, bool verbose, int port) =>
{
    if (!await EnsureServer(port)) return;
    string body;
    if (json != null && File.Exists(json))
        body = await File.ReadAllTextAsync(json);
    else if (json != null)
        body = json;
    else
        body = await Console.In.ReadToEndAsync();

    body = body.Trim();
    if (string.IsNullOrEmpty(body))
    {
        WriteError("EMPTY_INPUT", "No batch JSON provided.",
            "Provide a JSON file path, inline JSON, or pipe via stdin.\nExample: uia batch actions.json", jsonOptions);
        return;
    }

    if (body.StartsWith("["))
        body = JsonSerializer.Serialize(new { actions = JsonSerializer.Deserialize<JsonElement>(body), onError = "stop", verbose });
    else if (verbose && !body.Contains("\"verbose\""))
        body = body.TrimEnd('}') + ",\"verbose\":true}";

    WriteRaw(await CallPost(port, "/batch", body));
}, batchArg, verboseOpt, portOption);

// ==================== SCREENSHOT ====================
var screenshotCommand = new Command("screenshot",
@"Capture a window screenshot as PNG.
Returns: file path, width, height.

Examples:
  uia screenshot Calculator              Capture Calculator window
  uia screenshot ""Visual Studio Code""    Capture VS Code");
var ssWindowArg = new Argument<string>("window", "Window title (substring), process name, or handle");
var ssOutputOpt = new Option<string?>("-o", "Output file path (default: temp directory)");
screenshotCommand.AddArgument(ssWindowArg);
screenshotCommand.AddOption(ssOutputOpt);
screenshotCommand.SetHandler(async (string window, string? output, int port) =>
{
    if (!await EnsureServer(port)) return;
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return;
    WriteRaw(await CallPost(port, "/screenshot", JsonSerializer.Serialize(new { handle }, jsonOptions)));
}, ssWindowArg, ssOutputOpt, portOption);

// ==================== WAIT ====================
var waitCommand = new Command("wait",
@"Wait until a UI condition is met (or timeout).
Blocks until the specified element appears or disappears.

Examples:
  uia wait --window App --name ""Loading"" --timeout 10000   Wait for Loading element
  uia wait --window App --id resultPanel                     Wait for element by ID");
var waitNameOpt = new Option<string?>("--name", "Wait for element with this name to appear");
var waitTypeOpt = new Option<string?>("--type", "Element control type filter");
var waitIdOpt = new Option<string?>("--id", "Wait for element with this automation ID");
var waitTimeoutOpt = new Option<int>("--timeout", () => 5000, "Timeout in milliseconds (default: 5000)");
var waitWindowOpt = new Option<string?>("--window", "Window to watch");
waitCommand.AddOption(waitNameOpt); waitCommand.AddOption(waitTypeOpt);
waitCommand.AddOption(waitIdOpt); waitCommand.AddOption(waitTimeoutOpt);
waitCommand.AddOption(waitWindowOpt);
waitCommand.SetHandler(async (string? name, string? type, string? id, int timeout, string? window, int port) =>
{
    if (!await EnsureServer(port)) return;
    if (name == null && type == null && id == null)
    {
        WriteError("NO_CONDITION", "Wait requires at least one condition: --name, --type, or --id.",
            "Example: uia wait --window Calculator --name Display --timeout 5000", jsonOptions);
        return;
    }
    var action = new
    {
        type = "wait",
        window,
        timeoutMs = timeout,
        until = new
        {
            elementExists = new { name, controlType = type, automationId = id }
        }
    };
    WriteRaw(await CallPost(port, "/action", JsonSerializer.Serialize(action, jsonOptions)));
}, waitNameOpt, waitTypeOpt, waitIdOpt, waitTimeoutOpt, waitWindowOpt, portOption);

// ==================== PROCESSES ====================
var processesCommand = new Command("processes",
@"List running processes that have visible windows.
Useful for finding which apps to automate.

Examples:
  uia processes                List all windowed processes");
processesCommand.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallGet(port, "/processes"));
}, portOption);

// ==================== LAUNCH ====================
var launchCommand = new Command("launch",
@"Launch an application and wait for its window to appear.
Returns: processId, processName, mainWindowTitle, mainWindowHandle.

Examples:
  uia launch calc.exe           Open Calculator
  uia launch notepad.exe        Open Notepad
  uia launch msedge             Open Edge");
var launchPathArg = new Argument<string>("path", "Application path or executable name");
launchCommand.AddArgument(launchPathArg);
launchCommand.SetHandler(async (string path, int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallPost(port, "/launch", JsonSerializer.Serialize(new { path }, jsonOptions)));
}, launchPathArg, portOption);

// ==================== CLIPBOARD ====================
var clipboardCommand = new Command("clipboard",
@"Read or write clipboard text.

Examples:
  uia clipboard get             Read current clipboard text
  uia clipboard set ""Hello""     Set clipboard text");
var clipGetCommand = new Command("get", "Read clipboard text");
clipGetCommand.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallGet(port, "/clipboard"));
}, portOption);
var clipSetCommand = new Command("set", "Set clipboard text");
var clipTextArg = new Argument<string>("text", "Text to put on clipboard");
clipSetCommand.AddArgument(clipTextArg);
clipSetCommand.SetHandler(async (string text, int port) =>
{
    if (!await EnsureServer(port)) return;
    WriteRaw(await CallPost(port, "/clipboard", JsonSerializer.Serialize(new { text }, jsonOptions)));
}, clipTextArg, portOption);
clipboardCommand.AddCommand(clipGetCommand);
clipboardCommand.AddCommand(clipSetCommand);

// ==================== HIGHLIGHT ====================
var highlightCommand = new Command("highlight",
@"Draw a highlight rectangle around a UI element.
Styles: focus (cyan), action (yellow), error (red), success (green).

Examples:
  uia highlight Calculator --name Equals --style action
  uia highlight Calculator --id num5Button --fade 5000");
var hlWindowArg = new Argument<string>("window", "Window context");
var hlNameOpt = new Option<string?>("--name", "Element name to highlight");
var hlTypeOpt = new Option<string?>("--type", "Element control type");
var hlIdOpt = new Option<string?>("--id", "Element automation ID");
var hlStyleOpt = new Option<string>("--style", () => "focus", "Style: focus, action, error, success");
var hlFadeOpt = new Option<int>("--fade", () => 2000, "Auto-fade time in ms (0 = persist)");
highlightCommand.AddArgument(hlWindowArg);
highlightCommand.AddOption(hlNameOpt); highlightCommand.AddOption(hlTypeOpt);
highlightCommand.AddOption(hlIdOpt); highlightCommand.AddOption(hlStyleOpt); highlightCommand.AddOption(hlFadeOpt);
highlightCommand.SetHandler(async (string window, string? name, string? type, string? id, string style, int fade, int port) =>
{
    if (!await EnsureServer(port)) return;
    if (name == null && type == null && id == null)
    {
        WriteError("NO_ELEMENT", "Highlight requires --name, --type, or --id to identify the element.",
            "Example: uia highlight Calculator --name Equals --style action", jsonOptions);
        return;
    }
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return;
    var qs = new List<string>();
    if (name != null) qs.Add($"name={Uri.EscapeDataString(name)}");
    if (type != null) qs.Add($"type={Uri.EscapeDataString(type)}");
    if (id != null) qs.Add($"id={Uri.EscapeDataString(id)}");
    var findJson = await CallGet(port, $"/windows/{handle}/find?{string.Join('&', qs)}");
    var findResult = JsonSerializer.Deserialize<JsonElement>(findJson);
    if (findResult.GetProperty("count").GetInt32() == 0)
    {
        WriteError("ELEMENT_NOT_FOUND", $"No element found matching the query in '{window}'.",
            "Run 'uia find <window> --type Button' to see available elements.", jsonOptions);
        return;
    }
    var bounds = findResult.GetProperty("elements")[0].GetProperty("bounds");
    var hlBody = JsonSerializer.Serialize(new
    {
        x = bounds.GetProperty("x").GetInt32(),
        y = bounds.GetProperty("y").GetInt32(),
        width = bounds.GetProperty("width").GetInt32(),
        height = bounds.GetProperty("height").GetInt32(),
        style, fadeMs = fade
    }, jsonOptions);
    WriteRaw(await CallPost(port, "/overlay/highlight", hlBody));
}, hlWindowArg, hlNameOpt, hlTypeOpt, hlIdOpt, hlStyleOpt, hlFadeOpt, portOption);

// ==================== ANNOTATE ====================
var annotateCommand = new Command("annotate",
@"Show a text annotation near a UI element.
Styles: info (blue), action (yellow), reasoning (purple), warning (red).

Examples:
  uia annotate Calculator --name Equals --text ""About to compute""
  uia annotate Calculator --text ""Step 3"" --style reasoning");
var annWindowArg = new Argument<string>("window", "Window context");
var annTextOpt = new Option<string>("--text", "Annotation text") { IsRequired = true };
var annNameOpt = new Option<string?>("--name", "Element name to annotate near");
var annStyleOpt = new Option<string>("--style", () => "info", "Style: info, action, reasoning, warning");
annotateCommand.AddArgument(annWindowArg);
annotateCommand.AddOption(annTextOpt); annotateCommand.AddOption(annNameOpt); annotateCommand.AddOption(annStyleOpt);
annotateCommand.SetHandler(async (string window, string text, string? name, string style, int port) =>
{
    if (!await EnsureServer(port)) return;
    var handle = await ResolveWindowHandle(port, window);
    if (handle == null) return;
    int x = 100, y = 100;
    if (name != null)
    {
        var findJson = await CallGet(port, $"/windows/{handle}/find?name={Uri.EscapeDataString(name)}");
        var findResult = JsonSerializer.Deserialize<JsonElement>(findJson);
        if (findResult.GetProperty("count").GetInt32() > 0)
        {
            var bounds = findResult.GetProperty("elements")[0].GetProperty("bounds");
            x = bounds.GetProperty("x").GetInt32() + bounds.GetProperty("width").GetInt32();
            y = bounds.GetProperty("y").GetInt32();
        }
    }
    WriteRaw(await CallPost(port, "/overlay/annotate", JsonSerializer.Serialize(new { text, x, y, style }, jsonOptions)));
}, annWindowArg, annTextOpt, annNameOpt, annStyleOpt, portOption);

// ==================== OVERLAY ====================
var overlayClearCommand = new Command("overlay",
@"Manage the visual overlay (highlights, cursor, annotations).

Examples:
  uia overlay clear             Remove all overlay elements");
var clearSubCmd = new Command("clear", "Clear all overlay elements");
clearSubCmd.SetHandler(async (int port) =>
{
    if (!await EnsureServer(port)) return;
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var resp = await client.DeleteAsync($"http://localhost:{port}/overlay");
    WriteRaw(await resp.Content.ReadAsStringAsync());
}, portOption);
overlayClearCommand.AddCommand(clearSubCmd);

// ==================== REGISTER COMMANDS ====================
rootCommand.AddCommand(serveCommand);
rootCommand.AddCommand(stopCommand);
rootCommand.AddCommand(stateCommand);
rootCommand.AddCommand(windowsCommand);
rootCommand.AddCommand(focusCommand);
rootCommand.AddCommand(treeCommand);
rootCommand.AddCommand(findCommand);
rootCommand.AddCommand(focusedCommand);
rootCommand.AddCommand(clickCommand);
rootCommand.AddCommand(typeCommand);
rootCommand.AddCommand(keyCommand);
rootCommand.AddCommand(batchCommand);
rootCommand.AddCommand(screenshotCommand);
rootCommand.AddCommand(waitCommand);
rootCommand.AddCommand(processesCommand);
rootCommand.AddCommand(launchCommand);
rootCommand.AddCommand(clipboardCommand);
rootCommand.AddCommand(highlightCommand);
rootCommand.AddCommand(annotateCommand);
rootCommand.AddCommand(overlayClearCommand);

return await rootCommand.InvokeAsync(args);

// ==================== HELPER FUNCTIONS ====================

static string? FindServerExe()
{
    // Check env var first
    var envPath = Environment.GetEnvironmentVariable("UIACLI_SERVER_PATH");
    if (envPath != null && Directory.Exists(envPath)) return envPath;

    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Uia.Server"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Uia.Server"),
    };
    foreach (var c in candidates)
    {
        var csproj = Path.Combine(c, "Uia.Server.csproj");
        if (File.Exists(csproj)) return c;
    }
    var localPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Uia.Server");
    if (Directory.Exists(localPath)) return localPath;
    return null;
}

static async Task<bool> IsServerRunning(int port)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var resp = await client.GetAsync($"http://localhost:{port}/health");
        return resp.IsSuccessStatusCode;
    }
    catch { return false; }
}

static async Task<bool> EnsureServer(int port)
{
    if (await IsServerRunning(port)) return true;

    Info("Server not running. Auto-starting...");
    var serverPath = FindServerExe();
    if (serverPath == null)
    {
        WriteError("SERVER_NOT_FOUND",
            "Server is not running and the Uia.Server project was not found.",
            "Start the server manually with 'uia serve', or set UIACLI_SERVER_PATH environment variable.",
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, 2);
        return false;
    }

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"run --project \"{serverPath}\" -- --port {port}",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    Process.Start(psi);
    Info($"Server starting on port {port}...");

    if (!await WaitForServer(port))
    {
        WriteError("SERVER_START_FAILED",
            $"Server did not become healthy within 10s on port {port}.",
            "Try starting manually with 'uia serve' to see error details.",
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, 3);
        return false;
    }
    Info("Server started successfully.");
    return true;
}

static async Task<bool> WaitForServer(int port, int timeoutMs = 10000)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs)
    {
        if (await IsServerRunning(port)) return true;
        await Task.Delay(200);
    }
    return false;
}

static async Task<string> CallGet(int port, string path)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var resp = await client.GetAsync($"http://localhost:{port}{path}");
        return await resp.Content.ReadAsStringAsync();
    }
    catch (Exception ex)
    {
        return JsonSerializer.Serialize(new
        {
            ok = false,
            error = new { code = "HTTP_ERROR", message = $"Failed to call GET {path}: {ex.Message}",
                hint = "Is the server running? Try 'uia serve' to check." }
        });
    }
}

static async Task<string> CallPost(int port, string path, string body)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync($"http://localhost:{port}{path}", content);
        return await resp.Content.ReadAsStringAsync();
    }
    catch (Exception ex)
    {
        return JsonSerializer.Serialize(new
        {
            ok = false,
            error = new { code = "HTTP_ERROR", message = $"Failed to call POST {path}: {ex.Message}",
                hint = "Is the server running? Try 'uia serve' to check." }
        });
    }
}

static async Task<long?> ResolveWindowHandle(int port, string windowQuery)
{
    var windowsJson = await CallGet(port, "/windows");
    List<JsonElement>? windows;
    try { windows = JsonSerializer.Deserialize<List<JsonElement>>(windowsJson); }
    catch
    {
        WriteError("PARSE_ERROR", "Failed to parse window list from server.",
            "The server may be in a bad state. Try 'uia stop' then 'uia serve'.",
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return null;
    }
    if (windows == null || windows.Count == 0)
    {
        WriteError("NO_WINDOWS", "No windows found on the desktop.",
            "This is unusual. Is the desktop accessible?",
            new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return null;
    }

    // Try handle
    if (long.TryParse(windowQuery, out long handle))
    {
        var match = windows.FirstOrDefault(w => w.GetProperty("handle").GetInt64() == handle);
        if (match.ValueKind != JsonValueKind.Undefined) return handle;
    }

    // Try title or process name
    foreach (var w in windows)
    {
        if (w.TryGetProperty("title", out var t) && t.GetString()?.Contains(windowQuery, StringComparison.OrdinalIgnoreCase) == true)
            return w.GetProperty("handle").GetInt64();
        if (w.TryGetProperty("processName", out var p) && p.GetString()?.Equals(windowQuery, StringComparison.OrdinalIgnoreCase) == true)
            return w.GetProperty("handle").GetInt64();
    }

    // Not found — build helpful error
    var titles = windows
        .Where(w => w.TryGetProperty("title", out _))
        .Select(w => w.GetProperty("title").GetString() ?? "")
        .Where(t => t.Length > 0)
        .Take(10)
        .ToList();

    var similar = titles
        .Where(t => t.Contains(windowQuery, StringComparison.OrdinalIgnoreCase))
        .ToList();

    string hint;
    if (similar.Count > 0)
        hint = $"Partial matches: {string.Join(", ", similar.Select(s => $"'{Truncate(s, 50)}'"))}";
    else
        hint = $"Open windows: {string.Join(", ", titles.Select(s => $"'{Truncate(s, 40)}'"))}.\nRun 'uia windows' to see all.";

    WriteError("WINDOW_NOT_FOUND",
        $"No window found matching '{windowQuery}'. Is the application running?",
        hint,
        new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    return null;
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";
