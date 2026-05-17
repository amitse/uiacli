using System.Runtime.InteropServices;
using System.Windows.Automation;
using Uia.Core.Models;

namespace Uia.Core;

public class InputSimulator
{
    private readonly ElementInspector _inspector = new();

    public void Click(int x, int y)
    {
        SetCursorAndClick(x, y, Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP);
    }

    public void RightClick(int x, int y)
    {
        SetCursorAndClick(x, y, Win32.MOUSEEVENTF_RIGHTDOWN, Win32.MOUSEEVENTF_RIGHTUP);
    }

    public void DoubleClick(int x, int y)
    {
        SetCursorAndClick(x, y, Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(50);
        SetCursorAndClick(x, y, Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP);
    }

    public void ClickElement(AutomationElement element)
    {
        // Prefer InvokePattern
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj))
        {
            ((InvokePattern)invokeObj).Invoke();
            return;
        }

        // Fallback: click at element center
        var rect = element.Current.BoundingRectangle;
        if (!rect.IsEmpty)
        {
            Click((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
        }
    }

    public void Type(string text, string method = "auto")
    {
        switch (method)
        {
            case "keys":
                TypeViaKeystrokes(text);
                break;
            case "paste":
                TypeViaClipboard(text);
                break;
            case "auto":
            default:
                // Short text: keystrokes. Long text: clipboard.
                if (text.Length > 50)
                    TypeViaClipboard(text);
                else
                    TypeViaKeystrokes(text);
                break;
        }
    }

    public void TypeIntoElement(AutomationElement element, string text, string method = "auto")
    {
        // Try ValuePattern first
        if (method == "auto" || method == "value")
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valObj))
            {
                ((ValuePattern)valObj).SetValue(text);
                return;
            }
        }

        // Focus the element first, then type
        try { element.SetFocus(); } catch { }
        Thread.Sleep(50);
        Type(text, method == "auto" ? "auto" : method);
    }

    public void KeyPress(string combo)
    {
        var parts = combo.ToLowerInvariant().Split('+');
        var modifiers = new List<ushort>();
        ushort mainKey = 0;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed)
            {
                case "ctrl" or "control": modifiers.Add(0x11); break; // VK_CONTROL
                case "alt": modifiers.Add(0x12); break; // VK_MENU
                case "shift": modifiers.Add(0x10); break; // VK_SHIFT
                case "win": modifiers.Add(0x5B); break; // VK_LWIN
                default: mainKey = MapKeyName(trimmed); break;
            }
        }

        var inputs = new List<INPUT>();

        // Press modifiers
        foreach (var mod in modifiers)
            inputs.Add(MakeKeyInput(mod, 0));

        // Press main key
        if (mainKey != 0)
            inputs.Add(MakeKeyInput(mainKey, 0));

        // Release main key
        if (mainKey != 0)
            inputs.Add(MakeKeyInput(mainKey, Win32.KEYEVENTF_KEYUP));

        // Release modifiers (reverse order)
        for (int i = modifiers.Count - 1; i >= 0; i--)
            inputs.Add(MakeKeyInput(modifiers[i], Win32.KEYEVENTF_KEYUP));

        var arr = inputs.ToArray();
        Win32.SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    public void MouseMove(int x, int y)
    {
        SendMouseMoveAbsolute(x, y);
    }

    public void Drag(int fromX, int fromY, int toX, int toY, int steps = 40)
    {
        // Move to start
        SendMouseMoveAbsolute(fromX, fromY);
        Thread.Sleep(30);

        // Mouse down
        var downInput = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTDOWN } }
        };
        Win32.SendInput(1, new[] { downInput }, Marshal.SizeOf<INPUT>());
        Thread.Sleep(20);

        // Move in fine steps using SendInput (generates WM_MOUSEMOVE)
        for (int i = 1; i <= steps; i++)
        {
            int x = fromX + (toX - fromX) * i / steps;
            int y = fromY + (toY - fromY) * i / steps;
            SendMouseMoveAbsolute(x, y);
            Thread.Sleep(5);
        }

        Thread.Sleep(20);

        // Mouse up
        var upInput = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTUP } }
        };
        Win32.SendInput(1, new[] { upInput }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Draw a freehand path through a list of screen coordinate points.
    /// Mouse down at first point, move through all points, mouse up at last.
    /// Uses SendInput for each move to generate proper WM_MOUSEMOVE events.
    /// </summary>
    public void Freehand(int[] xs, int[] ys)
    {
        if (xs.Length == 0 || xs.Length != ys.Length) return;

        // Move to start
        SendMouseMoveAbsolute(xs[0], ys[0]);
        Thread.Sleep(20);

        // Mouse down
        var downInput = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTDOWN } }
        };
        Win32.SendInput(1, new[] { downInput }, Marshal.SizeOf<INPUT>());
        Thread.Sleep(10);

        // Move through all points
        for (int i = 1; i < xs.Length; i++)
        {
            SendMouseMoveAbsolute(xs[i], ys[i]);
            Thread.Sleep(2); // minimal delay — fast but generates events
        }

        Thread.Sleep(10);

        // Mouse up
        var upInput = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTUP } }
        };
        Win32.SendInput(1, new[] { upInput }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Use SendInput MOUSEEVENTF_MOVE|ABSOLUTE to generate proper mouse events.</summary>
    private static void SendMouseMoveAbsolute(int x, int y)
    {
        // Convert screen coords to normalized 0-65535 range
        int screenW = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
        int screenH = Win32.GetSystemMetrics(Win32.SM_CYSCREEN);
        int normX = (int)((x * 65535L) / screenW);
        int normY = (int)((y * 65535L) / screenH);

        var input = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    dwFlags = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE
                }
            }
        };
        Win32.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Scroll(int x, int y, int clicks)
    {
        Win32.SetCursorPos(x, y);
        Thread.Sleep(50);
        var input = new INPUT
        {
            Type = Win32.INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = Win32.MOUSEEVENTF_WHEEL,
                    mouseData = (uint)(clicks * 120) // 120 = WHEEL_DELTA
                }
            }
        };
        Win32.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void ScrollElement(AutomationElement element, int amount)
    {
        if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out object? scrollObj))
        {
            var scroll = (ScrollPattern)scrollObj;
            if (amount > 0)
                scroll.ScrollVertical(ScrollAmount.SmallIncrement);
            else
                scroll.ScrollVertical(ScrollAmount.SmallDecrement);
            return;
        }

        // Fallback: mouse wheel at element center
        var rect = element.Current.BoundingRectangle;
        if (!rect.IsEmpty)
            Scroll((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2), amount);
    }

    public void SelectItem(AutomationElement element, string value)
    {
        // Try ExpandCollapse first to open dropdown
        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? expandObj))
        {
            ((ExpandCollapsePattern)expandObj).Expand();
            Thread.Sleep(200);
        }

        // Find the item by name and select it
        var item = element.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, value));

        if (item != null)
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectObj))
            {
                ((SelectionItemPattern)selectObj).Select();
                return;
            }
            // Fallback: invoke/click the item
            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out object? invokeObj))
            {
                ((InvokePattern)invokeObj).Invoke();
                return;
            }
        }
    }

    public void Toggle(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggleObj))
        {
            ((TogglePattern)toggleObj).Toggle();
        }
    }

    private void SetCursorAndClick(int x, int y, uint downFlag, uint upFlag)
    {
        Win32.SetCursorPos(x, y);
        Thread.Sleep(10);

        var inputs = new INPUT[]
        {
            new() { Type = Win32.INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = downFlag } } },
            new() { Type = Win32.INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = upFlag } } }
        };
        Win32.SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private void TypeViaKeystrokes(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            inputs.Add(new INPUT
            {
                Type = Win32.INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = Win32.KEYEVENTF_UNICODE
                    }
                }
            });
            inputs.Add(new INPUT
            {
                Type = Win32.INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP
                    }
                }
            });
        }
        var arr = inputs.ToArray();
        Win32.SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    private void TypeViaClipboard(string text)
    {
        SetClipboardText(text);
        Thread.Sleep(50);
        KeyPress("ctrl+v");
    }

    private static void SetClipboardText(string text)
    {
        Win32.OpenClipboard(IntPtr.Zero);
        Win32.EmptyClipboard();
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
        var hGlobal = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        var ptr = Win32.GlobalLock(hGlobal);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Win32.GlobalUnlock(hGlobal);
        Win32.SetClipboardData(Win32.CF_UNICODETEXT, hGlobal);
        Win32.CloseClipboard();
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            Type = Win32.INPUT_KEYBOARD,
            U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
        };
    }

    private static ushort MapKeyName(string name) => name switch
    {
        "enter" or "return" => 0x0D,
        "tab" => 0x09,
        "escape" or "esc" => 0x1B,
        "space" => 0x20,
        "backspace" or "back" => 0x08,
        "delete" or "del" => 0x2E,
        "insert" or "ins" => 0x2D,
        "home" => 0x24,
        "end" => 0x23,
        "pageup" or "pgup" => 0x21,
        "pagedown" or "pgdn" => 0x22,
        "up" => 0x26,
        "down" => 0x28,
        "left" => 0x25,
        "right" => 0x27,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
        "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
        "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        "a" => 0x41, "b" => 0x42, "c" => 0x43, "d" => 0x44,
        "e" => 0x45, "f" => 0x46, "g" => 0x47, "h" => 0x48,
        "i" => 0x49, "j" => 0x4A, "k" => 0x4B, "l" => 0x4C,
        "m" => 0x4D, "n" => 0x4E, "o" => 0x4F, "p" => 0x50,
        "q" => 0x51, "r" => 0x52, "s" => 0x53, "t" => 0x54,
        "u" => 0x55, "v" => 0x56, "w" => 0x57, "x" => 0x58,
        "y" => 0x59, "z" => 0x5A,
        "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
        "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
        "8" => 0x38, "9" => 0x39,
        _ => 0
    };
}
