# UIA CLI — User Stories & Tracking

## Decisions Log

All 52 design decisions made during the grilling session:

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | Automation scope | Any Windows app with fallbacks | UIA for accessible apps, coordinate-based fallback for others |
| 2 | Remote/multi-monitor | Local only, multi-monitor aware | Remote is v2, coordinates must be absolute across monitors |
| 3 | Technology | C# .NET 8 | Native UIA, WPF overlay, fast runtime |
| 4 | Protocol | HTTP REST (JSON) on localhost | Simple, debuggable, any client can call it |
| 5 | Window identification | All methods (title, handle, process, automationId) | Title substring as default, handle for precision |
| 6 | Element addressing | Query filters (name, controlType, automationId) | Simple, no brittle paths |
| 7 | Input simulation | Hybrid: UIA patterns first, SendInput fallback | UIA patterns are faster, don't steal focus |
| 8 | Batch error handling | Configurable (stop/continue), default stop | Sequential deps common in UI automation |
| 9 | Batch results | Standard: ok/error + timing + element state | Agent needs enough to verify, not per-action screenshots |
| 10 | Screenshot delivery | PNG file + metadata JSON | Agent reads file separately |
| 11 | Server lifecycle | Port 9721, single instance, 30min idle shutdown | Predictable, auto-cleanup |
| 12 | Tree depth/cache | Depth 3, no cache, filter invisible | Always fresh, manageable responses |
| 13 | Ghost cursor | Smooth animation, separate visual, during actions | Shows intent without moving real cursor |
| 14 | Highlights | Named border styles, 2s auto-fade, multiple | focus=cyan, action=yellow, error=red, success=green |
| 15 | Annotations | Styled text, auto-positioned, click-through | Visual communication of agent reasoning |
| 16 | Self-improving | Rich observability for agent reasoning | Tool=hands+eyes, agent=brain |
| 17 | Output format | JSON always | Agent is primary user |
| 18 | Security | No restrictions, localhost boundary | Local dev tool, agent is trusted |
| 19 | Coordinates | Physical pixels everywhere | Matches UIA and SendInput natively |
| 20 | Keyboard format | Modifier+Key strings (ctrl+c) | Readable, standard key names |
| 21 | Wait/sync | No implicit wait + /wait endpoint + inline batch waits | Fast execution, explicit sync |
| 22 | Tree format | Nested JSON hierarchy | Natural UI structure |
| 23 | Element properties | name, controlType, automationId, bounds, isEnabled, value, patterns | Lean but actionable |
| 24 | Overlay-batch | Configurable: auto cursor, explicit highlights/annotations | Separates what from why |
| 25 | UI watching | No watching in v1 | /wait covers 90% of cases |
| 26 | Text input | Hybrid: ValuePattern > SendInput > clipboard | Server auto-selects best |
| 27 | Errors | Structured: code + message + suggestion | Agent self-corrects |
| 28 | State endpoint | GET /state for desktop snapshot | One call to bootstrap |
| 29 | Naming | Binary: `uia`, flat verbs | Short, agent-friendly |
| 30 | Primary interface | CLI-first, agent invokes uia.exe | Simpler skill integration |
| 31 | Inline waits | Yes, wait actions inside batches | Reduces round-trips |
| 32 | Inline reads | Yes, read actions mid-batch | Agent clicks then reads in one call |
| 33 | Multiple matches | Act on first, warn with count | Agent refines if needed |
| 34 | Max tree elements | 500 per response | Prevents huge payloads |
| 35 | Focused element | `uia focused` command | Agent knows keyboard target |
| 36 | Clipboard | `uia clipboard get/set` | Data transfer between apps |
| 37 | UIA patterns | Toggle, Select, ExpandCollapse, Scroll, Value, Invoke | Rich interaction |
| 38 | Dropdowns | `select` action, server handles expand→find→click | Abstracted complexity |
| 39 | Context menus | Right-click + tree in batch | No special support needed |
| 40 | Drag and drop | drag action with from/to | SendInput mouse events |
| 41 | Modal dialogs | Tree shows them, no special handling | Agent decides |
| 42 | UAC | Can't access, clear error | Documented limitation |
| 43 | Record mode | Not in v1 | Future feature |
| 44 | Multi-monitor overlay | Spans all monitors | Consistent with absolute coords |
| 45 | A11y testing | Not a goal | Bonus from tree inspection |
| 46 | North-star test | Calculator 25×25=625 | End-to-end validation |
| 47 | Skill structure | CLI commands = skill tools | Direct mapping |
| 48 | Action logging | Log with timestamps | Debugging agent behavior |
| 49 | Process listing | `uia processes` | Find apps before automating |
| 50 | App launching | `uia launch <path>` | Open apps to automate |
| 51 | Window ops | minimize, maximize, restore, close, move, resize | Full window management |
| 52 | Scroll | ScrollPattern + SendInput wheel fallback | Hybrid approach |

---

## User Stories

### Phase 0: Project Skeleton

- [x] **p0-solution** — Create .NET solution and projects
  - Create UiaCli.sln with Uia.Core, Uia.Server, Uia.Cli, Uia.Overlay
  - ✅ Verify: `dotnet build` succeeds

### Phase 1: Standalone Verification Scripts

- [x] **p1-list-windows** — List all desktop windows
  - Console app using UIAutomation COM → JSON array of windows
  - ✅ Verify: Script outputs valid JSON with real windows

- [x] **p1-focus-window** — Bring window to foreground
  - Takes title substring, brings matching window to front
  - ✅ Verify: Minimized Notepad comes to foreground

- [x] **p1-screenshot** — Capture window screenshot
  - Captures window to PNG file
  - ✅ Verify: PNG file created with correct dimensions

- [x] **p1-inspect-tree** — Print automation tree
  - Prints UIA tree as nested JSON to depth 3
  - ✅ Verify: Calculator buttons visible in output

### Phase 2: Core Library (Uia.Core)

- [x] **p2a-window-manager** — WindowManager class
  - ListWindows, FocusWindow, FindWindow, Minimize/Maximize/Restore/Close/Move
  - ✅ Verify: Integration tests pass against real windows

- [x] **p2b-element-inspector** — ElementInspector class
  - GetTree, FindElements, GetFocusedElement, filter invisible, depth/max limits
  - ✅ Verify: Calculator tree shows buttons, FindElements locates "Equals"

- [x] **p2c-input-simulator** — InputSimulator class
  - Click, Type, KeyPress, MouseMove, Drag, Scroll, Select, Toggle — hybrid UIA/SendInput
  - ✅ Verify: Click Calculator buttons, type in Notepad, ctrl+a selects all

- [x] **p2d-screen-capture** — ScreenCapture class
  - CaptureWindow, CaptureRegion, CaptureElement → PNG + metadata
  - ✅ Verify: PNG files valid with correct dimensions

- [x] **p2e-batch-executor** — BatchExecutor and action types
  - ActionRequest union, BatchExecutor with stop/continue, inline waits/reads, delays
  - ✅ Verify: Calculator batch clicks work, stop-on-error works, inline read returns value

- [x] **p2f-process-manager** — ProcessManager class
  - ListProcesses, LaunchApp
  - ✅ Verify: Launch calc.exe, get handle back

### Phase 3: HTTP Server (Uia.Server)

- [x] **p3-server-core** — All HTTP API endpoints
  - /health, /state, /windows, /batch, /screenshot, /clipboard, etc.
  - ✅ Verify: curl requests return correct JSON

- [x] **p3-server-lifecycle** — Server lifecycle management
  - PID file, port file, idle timeout, graceful shutdown, logging
  - ✅ Verify: PID file created/removed, idle shutdown works

### Phase 4: CLI Client (Uia.Cli)

- [x] **p4-cli-core** — All CLI commands with auto-start
  - All `uia` commands, auto-start server, JSON output, exit codes
  - ✅ Verify: `uia windows` outputs JSON, auto-start works

### Phase 5: Visual Overlay

- [x] **p5-overlay-window** — Transparent click-through overlay
  - WPF, all monitors, STA thread, enable/disable
  - ✅ Verify: Click-through works, spans monitors

- [x] **p5-highlights** — Element highlighting
  - Named styles, auto-fade, multiple simultaneous
  - ✅ Verify: Highlight visible around Calculator button

- [x] **p5-ghost-cursor** — Ghost cursor animation
  - Bezier animation, separate visual, during actions only
  - ✅ Verify: Smooth movement to target, doesn't move real cursor

- [x] **p5-annotations** — Text annotations
  - Styled, auto-positioned, click-through, auto-fade
  - ✅ Verify: Annotation appears near element with correct style

- [x] **p5-overlay-cli** — CLI overlay commands
  - highlight, annotate, overlay clear
  - ✅ Verify: CLI commands trigger overlay visuals

### Milestone

- [x] **e2e-calculator** — Calculator 25×25=625 end-to-end test
  - Launch → foreground → batch (click 2,5,×,2,5,=) → read result → verify 625
  - ✅ Verify: Script exits 0, result is 625

