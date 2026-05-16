using System.Diagnostics;
using System.Windows.Automation;
using Uia.Core.Models;

namespace Uia.Core;

public class BatchExecutor
{
    private readonly WindowManager _windowManager = new();
    private readonly ElementInspector _inspector = new();
    private readonly InputSimulator _input = new();
    private readonly ScreenCapture _capture = new();

    public BatchResponse Execute(BatchRequest request, Action<int, ActionRequest, long?>? onBeforeAction = null)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<ActionResult>();

        // Resolve window context if specified at batch level
        long? windowHandle = null;
        if (!string.IsNullOrEmpty(request.Window))
        {
            var win = _windowManager.FindWindow(request.Window);
            if (win != null)
            {
                windowHandle = win.Handle;
            }
            else
            {
                // Window not found — fail fast with a clear error
                sw.Stop();
                var allWindows = _windowManager.ListWindows();
                var similar = allWindows
                    .Where(w => w.Title.Contains(request.Window, StringComparison.OrdinalIgnoreCase) ||
                               w.ProcessName.Contains(request.Window, StringComparison.OrdinalIgnoreCase))
                    .Select(w => w.Title)
                    .Take(5)
                    .ToList();
                var suggestion = similar.Count > 0
                    ? $"Similar windows: {string.Join(", ", similar.Select(s => $"'{s}'"))}"
                    : $"No windows match '{request.Window}'. {allWindows.Count} windows are open.";

                results.Add(new ActionResult
                {
                    Index = -1,
                    Ok = false,
                    DurationMs = sw.ElapsedMilliseconds,
                    Error = new ErrorInfo
                    {
                        Code = "WINDOW_NOT_FOUND",
                        Message = $"Window '{request.Window}' not found. Is it running?",
                        Suggestion = suggestion
                    }
                });
                return new BatchResponse { Results = results, TotalDurationMs = sw.ElapsedMilliseconds };
            }
        }

        for (int i = 0; i < request.Actions.Count; i++)
        {
            var action = request.Actions[i];
            var actionSw = Stopwatch.StartNew();

            // Per-action window override
            long? actionWindow = windowHandle;
            if (!string.IsNullOrEmpty(action.Window))
            {
                var win = _windowManager.FindWindow(action.Window);
                if (win != null) actionWindow = win.Handle;
            }

            try
            {
                onBeforeAction?.Invoke(i, action, actionWindow);
                var result = ExecuteAction(action, actionWindow, i);
                actionSw.Stop();
                result.DurationMs = actionSw.ElapsedMilliseconds;
                results.Add(result);

                if (!result.Ok && request.OnError == "stop")
                    break;
            }
            catch (Exception ex)
            {
                actionSw.Stop();
                var errorResult = new ActionResult
                {
                    Index = i,
                    Ok = false,
                    DurationMs = actionSw.ElapsedMilliseconds,
                    Error = new ErrorInfo
                    {
                        Code = "EXECUTION_ERROR",
                        Message = ex.Message
                    }
                };
                results.Add(errorResult);

                if (request.OnError == "stop")
                    break;
            }

            // Per-action delay
            if (action.DelayMs > 0)
                Thread.Sleep(action.DelayMs.Value);
        }

        sw.Stop();
        return new BatchResponse { Results = results, TotalDurationMs = sw.ElapsedMilliseconds };
    }

    private ActionResult ExecuteAction(ActionRequest action, long? windowHandle, int index)
    {
        return action.Type.ToLowerInvariant() switch
        {
            "click" => DoClick(action, windowHandle, index),
            "rightclick" => DoRightClick(action, windowHandle, index),
            "doubleclick" => DoDoubleClick(action, windowHandle, index),
            "type" => DoType(action, windowHandle, index),
            "key" or "keypress" => DoKeyPress(action, index),
            "mousemove" => DoMouseMove(action, index),
            "drag" => DoDrag(action, index),
            "scroll" => DoScroll(action, windowHandle, index),
            "scrollintoview" or "scroll_into_view" => DoScrollIntoView(action, windowHandle, index),
            "select" => DoSelect(action, windowHandle, index),
            "toggle" => DoToggle(action, windowHandle, index),
            "focus" => DoFocus(action, index),
            "wait" => DoWait(action, windowHandle, index),
            "read" => DoRead(action, windowHandle, index),
            "screenshot" => DoScreenshot(action, windowHandle, index),
            _ => new ActionResult
            {
                Index = index, Ok = false,
                Error = new ErrorInfo { Code = "UNKNOWN_ACTION", Message = $"Unknown action type: {action.Type}" }
            }
        };
    }

    private ActionResult DoClick(ActionRequest action, long? windowHandle, int index)
    {
        if (action.Element != null && windowHandle.HasValue)
        {
            var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
            if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
            _input.ClickElement(el);
            return OkResult(index, el);
        }
        if (action.X.HasValue && action.Y.HasValue)
        {
            _input.Click(action.X.Value, action.Y.Value);
            return OkResult(index);
        }
        return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Click requires element or x,y coordinates" } };
    }

    private ActionResult DoRightClick(ActionRequest action, long? windowHandle, int index)
    {
        if (action.X.HasValue && action.Y.HasValue)
        {
            _input.RightClick(action.X.Value, action.Y.Value);
            return OkResult(index);
        }
        if (action.Element != null && windowHandle.HasValue)
        {
            var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
            if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
            var rect = el.Current.BoundingRectangle;
            _input.RightClick((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
            return OkResult(index, el);
        }
        return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "RightClick requires element or x,y" } };
    }

    private ActionResult DoDoubleClick(ActionRequest action, long? windowHandle, int index)
    {
        if (action.X.HasValue && action.Y.HasValue)
        {
            _input.DoubleClick(action.X.Value, action.Y.Value);
            return OkResult(index);
        }
        if (action.Element != null && windowHandle.HasValue)
        {
            var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
            if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
            var rect = el.Current.BoundingRectangle;
            _input.DoubleClick((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
            return OkResult(index, el);
        }
        return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "DoubleClick requires element or x,y" } };
    }

    private ActionResult DoType(ActionRequest action, long? windowHandle, int index)
    {
        if (string.IsNullOrEmpty(action.Text))
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Type requires text" } };

        if (action.Element != null && windowHandle.HasValue)
        {
            var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
            if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
            _input.TypeIntoElement(el, action.Text, action.Method ?? "auto");
            return OkResult(index, el);
        }

        _input.Type(action.Text, action.Method ?? "auto");
        return OkResult(index);
    }

    private ActionResult DoKeyPress(ActionRequest action, int index)
    {
        var combo = action.Combo ?? action.Text ?? "";
        if (string.IsNullOrEmpty(combo))
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "KeyPress requires combo" } };
        _input.KeyPress(combo);
        return OkResult(index);
    }

    private ActionResult DoMouseMove(ActionRequest action, int index)
    {
        if (!action.X.HasValue || !action.Y.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "MouseMove requires x,y" } };
        _input.MouseMove(action.X.Value, action.Y.Value);
        return OkResult(index);
    }

    private ActionResult DoDrag(ActionRequest action, int index)
    {
        if (!action.X.HasValue || !action.Y.HasValue || !action.ToX.HasValue || !action.ToY.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Drag requires x,y,toX,toY" } };
        _input.Drag(action.X.Value, action.Y.Value, action.ToX.Value, action.ToY.Value);
        return OkResult(index);
    }

    private ActionResult DoScroll(ActionRequest action, long? windowHandle, int index)
    {
        int clicks = action.Clicks ?? 3;
        if (action.Element != null && windowHandle.HasValue)
        {
            var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
            if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
            _input.ScrollElement(el, clicks);
            return OkResult(index, el);
        }
        if (action.X.HasValue && action.Y.HasValue)
        {
            _input.Scroll(action.X.Value, action.Y.Value, clicks);
            return OkResult(index);
        }
        return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Scroll requires element or x,y" } };
    }

    private ActionResult DoScrollIntoView(ActionRequest action, long? windowHandle, int index)
    {
        if (action.Element == null || !windowHandle.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "ScrollIntoView requires element and window" } };

        var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
        if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);

        try
        {
            if (el.TryGetCurrentPattern(ScrollItemPattern.Pattern, out object? scrollItemObj))
            {
                ((ScrollItemPattern)scrollItemObj).ScrollIntoView();
                Thread.Sleep(200); // let UI settle
                return OkResult(index, el);
            }

            // Fallback: check if element is off-screen and scroll parent
            var rect = el.Current.BoundingRectangle;
            if (!rect.IsEmpty)
            {
                // Element exists but may be clipped. Try scrolling parent containers.
                var walker = TreeWalker.ControlViewWalker;
                var parent = walker.GetParent(el);
                while (parent != null)
                {
                    if (parent.TryGetCurrentPattern(ScrollPattern.Pattern, out object? scrollObj))
                    {
                        var scroll = (ScrollPattern)scrollObj;
                        var parentRect = parent.Current.BoundingRectangle;
                        // Scroll until element is within parent's visible area
                        int attempts = 0;
                        while (attempts < 20)
                        {
                            var currentRect = el.Current.BoundingRectangle;
                            if (currentRect.Y >= parentRect.Y && currentRect.Bottom <= parentRect.Bottom)
                                break; // visible
                            if (currentRect.Y < parentRect.Y)
                                scroll.ScrollVertical(ScrollAmount.SmallDecrement);
                            else
                                scroll.ScrollVertical(ScrollAmount.SmallIncrement);
                            Thread.Sleep(50);
                            attempts++;
                        }
                        Thread.Sleep(100);
                        return OkResult(index, el);
                    }
                    parent = walker.GetParent(parent);
                }
            }

            return new ActionResult { Index = index, Ok = true, Error = new ErrorInfo { Code = "NO_SCROLL_PATTERN", Message = "Element found but no scrollable parent. It may already be visible." } };
        }
        catch (Exception ex)
        {
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "SCROLL_ERROR", Message = ex.Message } };
        }
    }

    private ActionResult DoSelect(ActionRequest action, long? windowHandle, int index)
    {
        if (action.Element == null || string.IsNullOrEmpty(action.Value) || !windowHandle.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Select requires element, value, and window" } };
        var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
        if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
        _input.SelectItem(el, action.Value);
        return OkResult(index, el);
    }

    private ActionResult DoToggle(ActionRequest action, long? windowHandle, int index)
    {
        if (action.Element == null || !windowHandle.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Toggle requires element and window" } };
        var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
        if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);
        _input.Toggle(el);
        return OkResult(index, el);
    }

    private ActionResult DoFocus(ActionRequest action, int index)
    {
        var target = action.Window ?? action.Text ?? "";
        if (string.IsNullOrEmpty(target))
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Focus requires window identifier" } };
        var win = _windowManager.FindWindow(target);
        if (win == null)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "WINDOW_NOT_FOUND", Message = $"Window not found: {target}" } };
        _windowManager.FocusWindow(win.Handle);
        return new ActionResult { Index = index, Ok = true };
    }

    private ActionResult DoWait(ActionRequest action, long? windowHandle, int index)
    {
        var timeout = action.TimeoutMs ?? 5000;
        var until = action.Until;
        if (until == null)
        {
            Thread.Sleep(timeout);
            return OkResult(index);
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeout)
        {
            if (until.ElementExists != null && windowHandle.HasValue)
            {
                var el = _inspector.FindRawElement(windowHandle.Value, until.ElementExists);
                if (el != null) return OkResult(index, el);
            }
            if (until.ElementGone != null && windowHandle.HasValue)
            {
                var el = _inspector.FindRawElement(windowHandle.Value, until.ElementGone);
                if (el == null) return OkResult(index);
            }
            if (until.ValueEquals != null && windowHandle.HasValue)
            {
                var el = _inspector.FindRawElement(windowHandle.Value, until.ValueEquals.Element);
                if (el != null)
                {
                    try
                    {
                        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? valObj))
                        {
                            if (((ValuePattern)valObj).Current.Value == until.ValueEquals.Value)
                                return OkResult(index, el);
                        }
                    }
                    catch { }
                }
            }
            Thread.Sleep(100);
        }
        return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "TIMEOUT", Message = $"Wait condition not met within {timeout}ms" } };
    }

    private ActionResult DoRead(ActionRequest action, long? windowHandle, int index)
    {
        if (action.Element == null || !windowHandle.HasValue)
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Read requires element and window" } };
        var el = _inspector.FindRawElement(windowHandle.Value, action.Element);
        if (el == null) return ElementNotFound(action.Element, windowHandle.Value, index);

        string? value = null;
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? valObj))
                value = ((ValuePattern)valObj).Current.Value;
            else
                value = el.Current.Name;
        }
        catch { value = el.Current.Name; }

        var node = BuildElementNode(el);
        return new ActionResult { Index = index, Ok = true, Value = value, Element = node };
    }

    private ActionResult DoScreenshot(ActionRequest action, long? windowHandle, int index)
    {
        CaptureResult result;
        if (windowHandle.HasValue)
            result = _capture.CaptureWindow(windowHandle.Value);
        else if (action.X.HasValue && action.Y.HasValue && action.ToX.HasValue && action.ToY.HasValue)
            result = _capture.CaptureRegion(action.X.Value, action.Y.Value, action.ToX.Value, action.ToY.Value);
        else
            return new ActionResult { Index = index, Ok = false, Error = new ErrorInfo { Code = "INVALID_ARGS", Message = "Screenshot requires window or region coordinates" } };

        return new ActionResult { Index = index, Ok = true, Value = result.FilePath };
    }

    private ActionResult OkResult(int index, AutomationElement? el = null)
    {
        var result = new ActionResult { Index = index, Ok = true };
        if (el != null) result.Element = BuildElementNode(el);
        return result;
    }

    private ActionResult ElementNotFound(ElementQuery query, long windowHandle, int index)
    {
        var error = new ErrorInfo
        {
            Code = "ELEMENT_NOT_FOUND",
            Message = $"No element matching query"
        };

        // Try to suggest similar elements
        if (!string.IsNullOrEmpty(query.Name))
        {
            var (_, suggestion) = _inspector.GetSimilarNames(windowHandle, query.Name);
            error.Suggestion = suggestion;
        }

        return new ActionResult { Index = index, Ok = false, Error = error };
    }

    private static ElementNode BuildElementNode(AutomationElement el)
    {
        var rect = el.Current.BoundingRectangle;
        return new ElementNode
        {
            Name = string.IsNullOrEmpty(el.Current.Name) ? null : el.Current.Name,
            ControlType = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
            AutomationId = string.IsNullOrEmpty(el.Current.AutomationId) ? null : el.Current.AutomationId,
            Bounds = rect.IsEmpty ? null : new BoundsInfo
            {
                X = (int)rect.X, Y = (int)rect.Y,
                Width = (int)rect.Width, Height = (int)rect.Height
            },
            IsEnabled = el.Current.IsEnabled
        };
    }
}
