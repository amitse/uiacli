# Client-server architecture with auto-start

The UIA CLI uses a client-server architecture where a long-running UIA Server holds the Windows UI Automation context and exposes it over HTTP on localhost. The UIA CLI is a thin client that sends HTTP requests. If the server isn't running when a CLI command is issued, the CLI automatically starts it in the background.

## Considered Options

- **Direct UIA calls from CLI**: Each CLI invocation would initialize UIA, perform the operation, and exit. Rejected because UIA initialization is slow (~200-500ms), and the Overlay (transparent window for visual feedback) requires a persistent process with a message loop.
- **Named pipes / Unix sockets**: Lower overhead than HTTP but harder to debug, no browser-based tooling, and no automatic API documentation. HTTP on localhost is fast enough for local automation.
- **gRPC**: Faster serialization but adds complexity. JSON over HTTP is simpler, debuggable with curl, and sufficient for UI automation latencies.

## Consequences

- The server must be startable both explicitly (`uiacli serve`) and implicitly (auto-start on first CLI command).
- A health-check endpoint is needed so the CLI can detect whether the server is already running.
- The server process must manage its own lifecycle — clean shutdown, port conflict detection.
