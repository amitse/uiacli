using System.Windows.Automation;
using Uia.Core.Models;

namespace Uia.Core;

public class ElementInspector
{
    private const int DefaultMaxDepth = 3;
    private const int DefaultMaxElements = 500;

    public ElementNode GetTree(IntPtr windowHandle, int maxDepth = DefaultMaxDepth, 
        int maxElements = DefaultMaxElements, bool includeOffscreen = false)
    {
        var element = AutomationElement.FromHandle(windowHandle);
        int count = 0;
        return BuildNode(element, 0, maxDepth, maxElements, includeOffscreen, ref count);
    }

    public ElementNode GetTree(long windowHandle, int maxDepth = DefaultMaxDepth,
        int maxElements = DefaultMaxElements, bool includeOffscreen = false)
        => GetTree(new IntPtr(windowHandle), maxDepth, maxElements, includeOffscreen);

    public List<ElementNode> FindElements(long windowHandle, ElementQuery query)
    {
        var root = AutomationElement.FromHandle(new IntPtr(windowHandle));
        var condition = BuildCondition(query);
        var found = root.FindAll(TreeScope.Descendants, condition);
        var results = new List<ElementNode>();
        int count = 0;
        foreach (AutomationElement el in found)
        {
            results.Add(BuildNode(el, 0, 0, 1, false, ref count));
        }
        return results;
    }

    public AutomationElement? FindRawElement(long windowHandle, ElementQuery query)
    {
        var root = AutomationElement.FromHandle(new IntPtr(windowHandle));
        var condition = BuildCondition(query);
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    public AutomationElement? FindRawElement(AutomationElement root, ElementQuery query)
    {
        var condition = BuildCondition(query);
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    public ElementNode? GetFocusedElement()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;
            int count = 0;
            return BuildNode(focused, 0, 0, 1, false, ref count);
        }
        catch { return null; }
    }

    public (List<string> names, string? suggestion) GetSimilarNames(long windowHandle, string targetName)
    {
        var root = AutomationElement.FromHandle(new IntPtr(windowHandle));
        var allElements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var names = new List<string>();

        foreach (AutomationElement el in allElements)
        {
            try
            {
                var name = el.Current.Name;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            catch { }
        }

        // Simple fuzzy match: find names containing the target or vice versa
        var similar = names
            .Where(n => n.Contains(targetName, StringComparison.OrdinalIgnoreCase) ||
                       targetName.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                       LevenshteinDistance(n.ToLower(), targetName.ToLower()) <= 3)
            .Distinct()
            .Take(5)
            .ToList();

        string? suggestion = similar.Count > 0
            ? $"Did you mean: {string.Join(", ", similar.Select(s => $"'{s}'"))}?"
            : null;

        return (similar, suggestion);
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length, m = t.Length;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[n, m];
    }

    private Condition BuildCondition(ElementQuery query)
    {
        var conditions = new List<Condition>();

        if (!string.IsNullOrEmpty(query.Name))
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, query.Name));

        if (!string.IsNullOrEmpty(query.AutomationId))
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, query.AutomationId));

        if (!string.IsNullOrEmpty(query.ControlType))
        {
            var ct = MapControlType(query.ControlType);
            if (ct != null)
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
        }

        return conditions.Count switch
        {
            0 => Condition.TrueCondition,
            1 => conditions[0],
            _ => new AndCondition(conditions.ToArray())
        };
    }

    private static ControlType? MapControlType(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "button" => ControlType.Button,
            "calendar" => ControlType.Calendar,
            "checkbox" => ControlType.CheckBox,
            "combobox" => ControlType.ComboBox,
            "custom" => ControlType.Custom,
            "datagrid" => ControlType.DataGrid,
            "dataitem" => ControlType.DataItem,
            "document" => ControlType.Document,
            "edit" => ControlType.Edit,
            "group" => ControlType.Group,
            "header" => ControlType.Header,
            "headeritem" => ControlType.HeaderItem,
            "hyperlink" => ControlType.Hyperlink,
            "image" => ControlType.Image,
            "list" => ControlType.List,
            "listitem" => ControlType.ListItem,
            "menu" => ControlType.Menu,
            "menubar" => ControlType.MenuBar,
            "menuitem" => ControlType.MenuItem,
            "pane" => ControlType.Pane,
            "progressbar" => ControlType.ProgressBar,
            "radiobutton" => ControlType.RadioButton,
            "scrollbar" => ControlType.ScrollBar,
            "separator" => ControlType.Separator,
            "slider" => ControlType.Slider,
            "spinner" => ControlType.Spinner,
            "splitbutton" => ControlType.SplitButton,
            "statusbar" => ControlType.StatusBar,
            "tab" => ControlType.Tab,
            "tabitem" => ControlType.TabItem,
            "table" => ControlType.Table,
            "text" => ControlType.Text,
            "thumb" => ControlType.Thumb,
            "titlebar" => ControlType.TitleBar,
            "toolbar" => ControlType.ToolBar,
            "tooltip" => ControlType.ToolTip,
            "tree" => ControlType.Tree,
            "treeitem" => ControlType.TreeItem,
            "window" => ControlType.Window,
            _ => null
        };
    }

    private ElementNode BuildNode(AutomationElement element, int currentDepth, int maxDepth,
        int maxElements, bool includeOffscreen, ref int count)
    {
        count++;
        var current = element.Current;
        var rect = current.BoundingRectangle;

        var patterns = new List<string>();
        try
        {
            foreach (var pattern in element.GetSupportedPatterns())
            {
                var name = pattern.ProgrammaticName;
                // Clean up pattern name: "InvokePatternIdentifiers.Pattern" → "Invoke"
                name = name.Replace("PatternIdentifiers.Pattern", "")
                           .Replace("Identifiers.Pattern", "");
                patterns.Add(name);
            }
        }
        catch { }

        var node = new ElementNode
        {
            Name = string.IsNullOrEmpty(current.Name) ? null : current.Name,
            ControlType = current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
            AutomationId = string.IsNullOrEmpty(current.AutomationId) ? null : current.AutomationId,
            Bounds = rect.IsEmpty ? null : new BoundsInfo
            {
                X = (int)rect.X, Y = (int)rect.Y,
                Width = (int)rect.Width, Height = (int)rect.Height
            },
            IsEnabled = current.IsEnabled,
            Value = GetValue(element),
            Patterns = patterns.Count > 0 ? patterns : null
        };

        if (currentDepth < maxDepth && count < maxElements)
        {
            var children = new List<ElementNode>();
            try
            {
                var childElements = element.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in childElements)
                {
                    if (count >= maxElements) break;
                    try
                    {
                        if (!includeOffscreen && child.Current.IsOffscreen) continue;
                        children.Add(BuildNode(child, currentDepth + 1, maxDepth, maxElements, includeOffscreen, ref count));
                    }
                    catch { }
                }
            }
            catch { }

            if (children.Count > 0)
                node.Children = children;
        }

        return node;
    }

    private static string? GetValue(AutomationElement element)
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
