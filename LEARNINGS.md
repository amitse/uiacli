# UIA CLI — Learnings & Patterns

Hard-won knowledge from automating real Windows applications. These patterns apply to any agent using UIA CLI to control a desktop.

---

## 1. WinUI3 / UWP Apps Are Opaque to UIA

**Problem**: Modern Windows apps (Paint, Calculator's internal controls, Store apps) use WinUI3 or UWP rendering. Their internal UI elements (toolbar buttons, canvas, color palette) expose **minimal or no UIA tree**. You get a top-level `Window` with one `Pane` child and nothing inside.

**What works**: Calculator exposes buttons because Microsoft specifically added accessibility markup. Most WinUI3 apps don't.

**Pattern**: 
1. Try `uia tree <window> --depth 4` first
2. If the tree is shallow (1-2 levels), fall back to coordinate-based automation
3. Use `uia screenshot` to visually identify element positions
4. Use coordinate clicks (`--x`, `--y`) instead of element queries

**Apps affected**: Paint, Photos, Settings, Store, most UWP/WinUI3 apps  
**Apps that work well**: Calculator, Notepad (classic), Office, Electron apps (VS Code, Edge)

---

## 2. The Screenshot + Tree Correlation Pattern

**Problem**: You can't click what you can't find. For apps with poor UIA trees, you need a different strategy.

**Pattern**:
1. `uia screenshot <window>` → get the visual layout
2. `uia tree <window> --depth 4` → get whatever UIA exposes
3. Cross-reference: match visible elements in screenshot to UIA tree nodes via `bounds` coordinates
4. For elements visible in screenshot but absent from tree → use coordinate clicks
5. For elements in the tree with bounds → use those bounds for precise clicking

**Key insight**: The screenshot gives you the "what" (visual position), the tree gives you the "where" (precise bounds). Use both together. Never guess coordinates from a screenshot alone — if a tree element exists, use its bounds.

---

## 3. `SetCursorPos` vs `SendInput` for Mouse Movement

**Problem**: `SetCursorPos()` moves the cursor but does NOT generate `WM_MOUSEMOVE` messages. Many apps (especially WinUI3) only respond to `SendInput` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE`.

**Impact**: The original `Drag()` function used `SetCursorPos` for intermediate points. Paint saw the cursor teleport but never received move events — so no drawing happened.

**Fix**: Always use `SendInput` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE` for mouse movement. Convert screen coordinates to normalized 0-65535 range:
```
normX = (screenX * 65535) / screenWidth
normY = (screenY * 65535) / screenHeight
```

**Rule**: Use `SetCursorPos` only for positioning before a click. Use `SendInput` for any movement that an app needs to track.

---

## 4. Click-Drawing vs Drag-Drawing

**Problem**: In Paint and similar apps, drag-drawing (mouse down → move → up) should produce a continuous stroke. But if the move events are too sparse or use `SetCursorPos`, the app sees gaps.

**Solutions**:
- **Click-drawing**: Click at many individual points. Produces dots, not lines. Works universally but looks dotted.
- **Freehand action**: Mouse down at first point, `SendInput` move through all points with 2ms delays, mouse up. Produces continuous strokes.
- **Fixed drag**: Uses `SendInput` moves with 40+ steps and 5ms delays.

**Use `freehand`** for drawing apps. It takes an array of `{x,y}` points and draws a continuous path:
```json
{"type": "freehand", "points": [{"x":100,"y":100}, {"x":110,"y":105}, ...]}
```

---

## 5. Window Titles Change During Navigation

**Problem**: Browser window title changes with every page navigation. `FindWindow("Amazon")` works initially, then fails when you navigate to a product page ("PopSockets Phone Grip...").

**Pattern**: 
1. Resolve the window ONCE and capture its **handle** (integer)
2. Use the handle for all subsequent operations
3. If title-based lookup fails, fall back to process name
4. The CLI's `ResolveWindowHandle` tries: handle → title substring → process name

**Rule**: For multi-step browser automation, capture the handle after the first `uia windows` call and use it throughout.

---

## 6. UWP App Launch Returns Dead PIDs

**Problem**: `notepad.exe` on Windows 11 is a UWP app. When you `Process.Start("notepad.exe")`, the process exits immediately and a broker relaunches it as a different process. The returned PID is already dead.

**Workaround**: After launching, wait 2-3 seconds, then `uia windows` to find the actual window by title. Don't rely on the returned PID/handle for UWP apps.

**Classic Win32 apps** (mspaint.exe, cmd.exe, winver.exe) don't have this issue.

---

## 7. Colour Selection in Modern Paint

**Problem**: Paint's colour palette is custom-rendered with no UIA elements. Each colour dot is ~12px diameter. Off by 5 pixels and you select the wrong colour or hit a section boundary.

**Lesson**: For tiny toolbar targets, keyboard shortcuts are more reliable than coordinate clicks. If no keyboard shortcut exists, take a screenshot, analyze pixel colours at the target coordinates, and adjust.

**Pattern for toolbar clicking**:
1. Screenshot the window
2. Identify the target icon's visual center
3. Calculate screen coordinates: `screen_x = window_x + icon_x`, `screen_y = window_y + icon_y`
4. Click and verify (screenshot again, check if the expected state changed)
5. If wrong, adjust by ±5px and retry

---

## 8. Sub-Agent Exploration Pattern

**Problem**: Exploring an unfamiliar app's UI tree and correlating it with screenshots takes many round-trips. The main agent's context fills up with exploration data.

**Pattern**: Use a cheap, fast sub-agent (e.g., Haiku) to explore:

1. **Main agent**: "Explore Paint. Find the pencil tool, canvas bounds, and colour palette positions."
2. **Sub-agent (Haiku)**: 
   - Runs `uia tree Paint --depth 5`
   - Runs `uia screenshot Paint` 
   - Analyzes both and reports: "Canvas at (500,300)-(870,690). Pencil icon not in UIA tree — coordinate-based at screen (604,242). Black colour at (1015,237)."
3. **Main agent**: Uses the exact coordinates to execute actions

**Benefits**: Sub-agent is 10x cheaper and 3x faster. Main agent gets precise coordinates without wasting tokens on exploration.

**Future**: This pattern should become a built-in skill: `uia explore <window>` → returns a structured analysis of the window's UI with annotated screenshot.

---

## 9. Batch Is Always Faster Than Individual Commands

**Performance comparison for 8-action Calculator sequence**:
| Approach | Time |
|----------|------|
| 8 separate `uia click` calls | ~8 × 85ms = 680ms |
| 1 `uia batch` call | ~160ms |
| 1 `uia batch --verbose` | ~3500ms (with overlay) |

**Rule**: Always batch actions when possible. The per-command overhead (Go CLI startup + HTTP round-trip) is 60-85ms. Batching eliminates it.

---

## 10. Ghost Cursor Position vs Real Cursor

**Problem**: The overlay's ghost cursor is rendered at the target position, but the REAL cursor (moved by `SendInput`) may be at a different position if:
- DPI scaling differs between the overlay process and the target app
- The overlay window coordinate system doesn't match screen coordinates
- Multiple monitors with different scaling

**Current status**: Works correctly at 100% DPI on a single monitor. Multi-monitor with different DPI may have offset issues.

---

## 11. The Freehand Drawing Recipe

For drawing shapes in Paint or similar apps:

```
1. Launch Paint:           uia launch mspaint.exe
2. Focus it:               uia focus "Untitled - Paint"  
3. Select brush tool:      Click coordinates in toolbar (no UIA)
4. Select black colour:    Click coordinates in palette (no UIA)
5. Compute shape points:   Generate array of {x,y} coordinates
6. Offset to canvas:       canvas_screen_x = window_x + canvas_offset_x
7. Draw:                   uia batch with freehand action
8. Verify:                 uia screenshot, check result
```

For spirals: `r = a + b*θ`, `x = cx + r*cos(θ)`, `y = cy + r*sin(θ)` with 200+ points.

---

## Patterns Still Needed

- **Annotated screenshot**: Overlay UIA element bounds on screenshot so agent can visually see what's accessible
- **Element scroll-into-view**: For web apps with off-viewport elements (Amazon sort dropdown)
- **App-specific profiles**: Known toolbar positions for common apps (Paint, Notepad, Explorer)
- **Retry with adjustment**: If click doesn't produce expected result, screenshot → analyze → retry with adjusted coordinates
- **Clipboard data transfer**: For moving structured data between apps without typing
