# Overlay system for visual agent feedback

The UIA Server manages a transparent always-on-top window (the Overlay) that renders Highlights, a Ghost Cursor, and Annotations on top of the desktop. This makes agent actions visible to human observers without interfering with actual UI automation.

The Overlay is part of the server process rather than a separate process because it needs tight coordination with action execution — highlights should appear before clicks, the ghost cursor should animate to targets, and annotations should appear in sync with actions.

## Consequences

- The server must run an STA (single-threaded apartment) thread for the WPF overlay alongside the HTTP API thread.
- Overlay rendering is optional — headless/CI environments can disable it.
- The Ghost Cursor is visual-only and does not move the real system cursor; actual mouse actions use Win32 SendInput.
