# UIA CLI

A command-line tool that lets AI agents (and humans) **observe and control any Windows desktop application** through [Windows UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiauto-win32).

List windows, inspect element trees, click buttons, type text, drag, scroll, take screenshots — all from the terminal, all as JSON.

<p align="center">
  <img src="demo.gif" alt="UIA CLI automating Calculator with ghost cursor, highlights, and annotations" width="370">
</p>

<p align="center">
  <em>Ghost cursor, element highlights, and annotations — the agent clicks 25 × 25 = 625 while you watch.</em>
</p>

[![Build](https://github.com/amitse/uiacli/actions/workflows/build.yml/badge.svg)](https://github.com/amitse/uiacli/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/amitse/uiacli)](https://github.com/amitse/uiacli/releases/latest)
![Windows](https://img.shields.io/badge/platform-Windows-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![Go](https://img.shields.io/badge/CLI-Go-00ADD8)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Why

AI agents that control a desktop need a **fast, reliable bridge** between natural-language intent and low-level UI automation. UIA CLI provides that bridge:

- **One binary** (`uia.exe`) — a Go CLI that auto-starts a .NET server on first use
- **JSON everywhere** — structured output designed for LLM consumption, with machine-readable error codes and hints
- **Action batching** — send multiple UI operations in one call to avoid per-command overhead
- **Visual overlay** — ghost cursor, element highlights, and annotations so a human can follow what the agent is doing
- **Hybrid input** — uses UIA patterns when available, falls back to `SendInput` for apps with poor accessibility

## Quick Start

### One-liner install

```powershell
irm https://raw.githubusercontent.com/amitse/uiacli/master/install.ps1 | iex
```

Downloads the latest release to `%LOCALAPPDATA%\Programs\uiacli` and adds it to your PATH. No .NET SDK or Go toolchain required.

### Download manually

1. Grab the latest zip from [**Releases**](https://github.com/amitse/uiacli/releases/latest)
2. Extract to a folder (e.g., `C:\Tools\uiacli`)
3. Add that folder to your `PATH`
4. Run `uia windows`

No .NET SDK or Go toolchain required — the release is fully self-contained.

### Build from source

#### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Go 1.24+](https://go.dev/dl/) (only needed to build the CLI from source)

#### Steps

```bash
# Build the .NET server + core libraries
dotnet build UiaCli.sln

# Build the Go CLI
cd cli
go build -o ../uia.exe .
cd ..
```

### Run

```bash
# List all open windows
uia windows

# Inspect Calculator's UI tree
uia tree Calculator --depth 2

# Click the "5" button
uia click --window Calculator --name Five

# Type text into Notepad
uia type --window Notepad --text "Hello, world!"

# Take a screenshot
uia screenshot --window Calculator

# Run a batch of actions (from a JSON file)
uia batch actions.json --verbose
```

The server auto-starts on the first command. To manage it explicitly:

```bash
uia serve --background   # Start the server
uia stop                 # Stop the server
```

## Architecture

```
┌─────────────┐         HTTP/JSON          ┌──────────────────┐
│             │    localhost:9721           │                  │
│   uia.exe   │ ◄────────────────────────► │   UIA Server     │
│   (Go CLI)  │                            │   (.NET 8)       │
│             │                            │                  │
└─────────────┘                            │  ┌────────────┐  │
                                           │  │ Uia.Core   │  │
     Any agent or script                   │  │ - Windows  │  │
     can call uia.exe                      │  │ - Elements │  │
     or hit the HTTP API                   │  │ - Input    │  │
     directly                              │  │ - Capture  │  │
                                           │  │ - Batch    │  │
                                           │  └────────────┘  │
                                           │  ┌────────────┐  │
                                           │  │ Uia.Overlay│  │
                                           │  │ - Cursor   │  │
                                           │  │ - Highlights│ │
                                           │  │ - Annotations│ │
                                           │  └────────────┘  │
                                           └──────────────────┘
```

- **`uia.exe`** (Go) — thin CLI client. Parses commands, auto-starts the server, sends HTTP requests, prints JSON.
- **Uia.Server** (.NET 8) — long-running HTTP server on `localhost:9721`. Holds the UIA automation context, executes actions, drives the overlay.
- **Uia.Core** (.NET 8) — library for window management, element inspection, input simulation, screen capture, and batch execution.
- **Uia.Overlay** (.NET 8 / WPF) — transparent always-on-top window that renders ghost cursor, highlights, and annotations.

See [ADR-0001](docs/adr/0001-client-server-with-auto-start.md) for why client-server over direct UIA calls.

## Commands

| Command | Description |
|---------|-------------|
| `uia windows` | List all open windows |
| `uia tree <window>` | Inspect the UI automation tree |
| `uia find <window>` | Find elements by name, type, or automation ID |
| `uia focused` | Get the currently focused element |
| `uia focus <window>` | Bring a window to the foreground |
| `uia click` | Click an element or coordinates |
| `uia type` | Type text into the focused element |
| `uia key` | Send a key combination (e.g., `ctrl+c`) |
| `uia batch <file>` | Execute a batch of actions from a JSON file |
| `uia screenshot` | Capture a window or region to PNG |
| `uia clipboard` | Get or set clipboard text |
| `uia highlight` | Draw a highlight rectangle on the overlay |
| `uia annotate` | Show a text annotation on the overlay |
| `uia overlay` | Configure or clear the overlay |
| `uia launch <path>` | Launch an application |
| `uia processes` | List running processes |
| `uia wait` | Wait for an element to appear |
| `uia state` | Get a full desktop snapshot (windows, focus, cursor, screens) |
| `uia serve` | Start the server (auto-started on first command) |
| `uia stop` | Stop the server |

Run `uia <command> --help` for details on any command.

## Output Format

All output is JSON wrapped in an **Error Envelope**:

```json
// Success
{"ok": true, "data": { ... }}

// Error
{"ok": false, "error": {"code": "WINDOW_NOT_FOUND", "message": "...", "hint": "..."}}
```

Exit codes: `0` = success, `1` = action failed, `2` = setup error, `3` = timeout.

## Action Batching

Send multiple actions in one call to minimize round-trip overhead:

```json
{
  "window": "Calculator",
  "actions": [
    {"type": "click", "element": {"name": "Two"}},
    {"type": "click", "element": {"name": "Five"}},
    {"type": "click", "element": {"name": "Multiply by"}},
    {"type": "click", "element": {"name": "Two"}},
    {"type": "click", "element": {"name": "Five"}},
    {"type": "click", "element": {"name": "Equals"}},
    {"type": "read", "element": {"automationId": "CalculatorResults"}}
  ],
  "verbose": true
}
```

With `--verbose`, the overlay shows a ghost cursor, highlights, and annotations for each action as it executes.

## Visual Overlay

The overlay is a transparent, click-through window that spans all monitors:

- **Ghost Cursor** — a synthetic pointer that follows agent actions without moving the real mouse
- **Highlights** — colored rectangles around target elements (`focus`=cyan, `action`=yellow, `error`=red, `success`=green)
- **Annotations** — text labels explaining what the agent is doing and why

This makes agent activity visible and understandable to a human watching the screen.

## HTTP API

The server exposes a REST API on `http://localhost:9721`. You can use it directly from any language:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Server health check |
| GET | `/state` | Full desktop snapshot |
| GET | `/windows` | List windows |
| POST | `/windows/{handle}/focus` | Focus a window |
| GET | `/windows/{handle}/tree` | Get element tree |
| GET | `/windows/{handle}/find` | Find elements |
| GET | `/focused` | Get focused element |
| POST | `/batch` | Execute action batch |
| POST | `/action` | Execute single action |
| POST | `/screenshot` | Capture screenshot |
| GET/POST | `/clipboard` | Get/set clipboard |
| GET | `/processes` | List processes |
| POST | `/launch` | Launch an application |
| POST | `/overlay/highlight` | Add highlight |
| POST | `/overlay/annotate` | Add annotation |
| POST | `/overlay/cursor` | Move ghost cursor |
| DELETE | `/overlay` | Clear all overlays |

## Configuration

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| `UIACLI_PORT` | `9721` | Server port |
| `UIACLI_SERVER_PATH` | (auto-detect) | Path to server binary or project |

The server shuts down automatically after 30 minutes of inactivity.

## Project Structure

```
├── cli/                    # Go CLI client (uia.exe)
│   ├── main.go
│   └── go.mod
├── src/
│   ├── Uia.Core/           # Core automation library
│   ├── Uia.Server/         # HTTP API server
│   ├── Uia.Cli/            # .NET CLI (alternative to Go CLI)
│   └── Uia.Overlay/        # WPF overlay window
├── scripts/                # Standalone verification scripts
├── docs/adr/               # Architecture Decision Records
├── CONTEXT.md              # Domain glossary
└── LEARNINGS.md            # Hard-won automation patterns
```

## Known Limitations

- **Windows only** — requires Windows UI Automation APIs
- **Single monitor DPI** — multi-monitor setups with different DPI scaling may have coordinate offset issues
- **WinUI3/UWP apps** — modern apps with custom rendering expose minimal UIA trees; use coordinate-based fallbacks (see [LEARNINGS.md](LEARNINGS.md))
- **No UAC access** — cannot automate elevated (admin) windows from a non-elevated process
- **Local only** — the server binds to `localhost`; remote automation is not supported

## Contributing

Contributions are welcome! Here's how to get started:

1. Fork the repo and clone it
2. `dotnet build UiaCli.sln` to build the server
3. `cd cli && go build -o ../uia.exe .` to build the CLI
4. Make your changes and test against a real Windows app
5. Open a PR

Check the [open issues](https://github.com/amitse/uiacli/issues) for ideas, or file a new one if you hit a bug or have a feature request.

## License

[MIT](LICENSE)
