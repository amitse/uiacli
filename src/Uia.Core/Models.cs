using System.Text.Json.Serialization;

namespace Uia.Core.Models;

public class WindowInfo
{
    [JsonPropertyName("handle")] public long Handle { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("processId")] public int ProcessId { get; set; }
    [JsonPropertyName("bounds")] public BoundsInfo Bounds { get; set; } = new();
    [JsonPropertyName("isMinimized")] public bool IsMinimized { get; set; }
    [JsonPropertyName("isMaximized")] public bool IsMaximized { get; set; }
    [JsonPropertyName("hasFocus")] public bool HasFocus { get; set; }
}

public class BoundsInfo
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

public class ElementNode
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("controlType")] public string ControlType { get; set; } = "";

    [JsonPropertyName("automationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationId { get; set; }

    [JsonPropertyName("bounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundsInfo? Bounds { get; set; }

    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("patterns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Patterns { get; set; }

    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ElementNode>? Children { get; set; }
}

public class ElementQuery
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("controlType")] public string? ControlType { get; set; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
}

public class ActionResult
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorInfo? Error { get; set; }

    [JsonPropertyName("element")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElementNode? Element { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }
}

public class ErrorInfo
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("suggestion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Suggestion { get; set; }
}

public class CaptureResult
{
    [JsonPropertyName("filePath")] public string FilePath { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

public class DesktopState
{
    [JsonPropertyName("windows")] public List<WindowInfo> Windows { get; set; } = new();
    [JsonPropertyName("focusedWindow")] public WindowInfo? FocusedWindow { get; set; }
    [JsonPropertyName("focusedElement")] public ElementNode? FocusedElement { get; set; }
    [JsonPropertyName("cursorPosition")] public PointInfo CursorPosition { get; set; } = new();
    [JsonPropertyName("screens")] public List<ScreenInfo> Screens { get; set; } = new();
}

public class PointInfo
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
}

public class ScreenInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("bounds")] public BoundsInfo Bounds { get; set; } = new();
    [JsonPropertyName("isPrimary")] public bool IsPrimary { get; set; }
}
