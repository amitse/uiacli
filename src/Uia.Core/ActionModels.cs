using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uia.Core.Models;

[JsonConverter(typeof(ActionRequestConverter))]
public class ActionRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("x")] public int? X { get; set; }
    [JsonPropertyName("y")] public int? Y { get; set; }
    [JsonPropertyName("element")] public ElementQuery? Element { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("combo")] public string? Combo { get; set; }
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("toX")] public int? ToX { get; set; }
    [JsonPropertyName("toY")] public int? ToY { get; set; }
    [JsonPropertyName("clicks")] public int? Clicks { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("delayMs")] public int? DelayMs { get; set; }
    [JsonPropertyName("timeoutMs")] public int? TimeoutMs { get; set; }
    [JsonPropertyName("until")] public WaitCondition? Until { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("points")] public List<PointPair>? Points { get; set; }

    // Overlay actions
    [JsonPropertyName("style")] public string? Style { get; set; }
    [JsonPropertyName("fadeMs")] public int? FadeMs { get; set; }
    [JsonPropertyName("animateMs")] public int? AnimateMs { get; set; }
}

public class WaitCondition
{
    [JsonPropertyName("elementExists")] public ElementQuery? ElementExists { get; set; }
    [JsonPropertyName("elementGone")] public ElementQuery? ElementGone { get; set; }
    [JsonPropertyName("valueEquals")] public ValueEqualsCondition? ValueEquals { get; set; }
}

public class ValueEqualsCondition
{
    [JsonPropertyName("element")] public ElementQuery Element { get; set; } = new();
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public class BatchRequest
{
    [JsonPropertyName("actions")] public List<ActionRequest> Actions { get; set; } = new();
    [JsonPropertyName("onError")] public string OnError { get; set; } = "stop"; // "stop" or "continue"
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("verbose")] public bool Verbose { get; set; } = false;
}

public class BatchResponse
{
    [JsonPropertyName("results")] public List<ActionResult> Results { get; set; } = new();
    [JsonPropertyName("totalDurationMs")] public long TotalDurationMs { get; set; }
}

public class PointPair
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
}

/// <summary>
/// Passthrough converter so ActionRequest deserializes all properties from a flat JSON object.
/// </summary>
public class ActionRequestConverter : JsonConverter<ActionRequest>
{
    public override ActionRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var action = new ActionRequest();

        if (root.TryGetProperty("type", out var t)) action.Type = t.GetString() ?? "";
        if (root.TryGetProperty("x", out var x)) action.X = x.GetInt32();
        if (root.TryGetProperty("y", out var y)) action.Y = y.GetInt32();
        if (root.TryGetProperty("toX", out var tx)) action.ToX = tx.GetInt32();
        if (root.TryGetProperty("toY", out var ty)) action.ToY = ty.GetInt32();
        if (root.TryGetProperty("text", out var txt)) action.Text = txt.GetString();
        if (root.TryGetProperty("combo", out var c)) action.Combo = c.GetString();
        if (root.TryGetProperty("method", out var m)) action.Method = m.GetString();
        if (root.TryGetProperty("value", out var v)) action.Value = v.GetString();
        if (root.TryGetProperty("clicks", out var cl)) action.Clicks = cl.GetInt32();
        if (root.TryGetProperty("window", out var w)) action.Window = w.GetString();
        if (root.TryGetProperty("delayMs", out var d)) action.DelayMs = d.GetInt32();
        if (root.TryGetProperty("timeoutMs", out var to)) action.TimeoutMs = to.GetInt32();
        if (root.TryGetProperty("description", out var desc)) action.Description = desc.GetString();
        if (root.TryGetProperty("style", out var s)) action.Style = s.GetString();
        if (root.TryGetProperty("fadeMs", out var f)) action.FadeMs = f.GetInt32();
        if (root.TryGetProperty("animateMs", out var a)) action.AnimateMs = a.GetInt32();

        if (root.TryGetProperty("element", out var el))
            action.Element = JsonSerializer.Deserialize<ElementQuery>(el.GetRawText(), options);
        if (root.TryGetProperty("until", out var u))
            action.Until = JsonSerializer.Deserialize<WaitCondition>(u.GetRawText(), options);
        if (root.TryGetProperty("points", out var pts))
            action.Points = JsonSerializer.Deserialize<List<PointPair>>(pts.GetRawText(), options);

        return action;
    }

    public override void Write(Utf8JsonWriter writer, ActionRequest value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
