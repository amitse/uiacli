using System.Text.Json.Serialization;
using System.Windows.Automation;

static class TreeBuilder
{
    public static ElementNode BuildTree(AutomationElement element, int currentDepth, int maxDepth)
    {
        var rect = element.Current.BoundingRectangle;
        var patterns = new List<string>();
        foreach (var pattern in element.GetSupportedPatterns())
            patterns.Add(pattern.ProgrammaticName.Replace("Identifiers.Pattern", "").Replace("PatternIdentifiers.Pattern", ""));

        var node = new ElementNode
        {
            Name = string.IsNullOrEmpty(element.Current.Name) ? null : element.Current.Name,
            ControlType = element.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
            AutomationId = string.IsNullOrEmpty(element.Current.AutomationId) ? null : element.Current.AutomationId,
            Bounds = rect.IsEmpty ? null : new BoundsInfo
            {
                X = (int)rect.X, Y = (int)rect.Y,
                Width = (int)rect.Width, Height = (int)rect.Height
            },
            IsEnabled = element.Current.IsEnabled,
            Value = GetValue(element),
            Patterns = patterns.Count > 0 ? patterns : null
        };

        if (currentDepth < maxDepth)
        {
            var children = new List<ElementNode>();
            try
            {
                var childElements = element.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in childElements)
                {
                    if (child.Current.IsOffscreen) continue;
                    children.Add(BuildTree(child, currentDepth + 1, maxDepth));
                }
            }
            catch { }

            if (children.Count > 0)
                node.Children = children;
        }

        return node;
    }

    static string? GetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
                return ((ValuePattern)pattern).Current.Value;
        }
        catch { }
        return null;
    }
}

class ElementNode
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("controlType")] public string? ControlType { get; set; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("bounds")] public BoundsInfo? Bounds { get; set; }
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("patterns")] public List<string>? Patterns { get; set; }
    [JsonPropertyName("children")] public List<ElementNode>? Children { get; set; }
}

class BoundsInfo
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}
