using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Automation;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: InspectTree <title-substring> [--depth N]");
    return 1;
}

var search = args[0];
int maxDepth = 3;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--depth" && int.TryParse(args[i + 1], out int d))
        maxDepth = d;
}

// Find window by title
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

var element = AutomationElement.FromHandle(found);
var tree = TreeBuilder.BuildTree(element, 0, maxDepth);

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
Console.WriteLine(JsonSerializer.Serialize(tree, options));
return 0;
