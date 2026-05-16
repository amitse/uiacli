# Self-documenting CLI with structured JSON error contract

Every CLI command outputs structured JSON on stdout — never plain text, never mixed stderr/stdout errors. Errors follow a strict envelope: `{"ok": false, "error": {"code": "...", "message": "...", "hint": "..."}}`. Human-only progress messages (like "Starting server...") go to stderr with a `[uia]` prefix so agents can ignore them.

This is surprising because most CLIs mix human-readable text with machine output and rely on exit codes alone. We chose structured errors with hints because the primary consumer is an AI agent that needs to programmatically understand what went wrong AND know what to try next — without parsing prose or running `--help`.

## Consequences

- Every error path in the CLI must go through `WriteError()` or `WriteRaw()` — never `Console.WriteLine()` with ad-hoc formats.
- The error `code` vocabulary (`WINDOW_NOT_FOUND`, `NO_TARGET`, `SERVER_NOT_FOUND`, etc.) is a de facto API contract that agents will depend on.
- The `hint` field means errors are self-documenting — the CLI teaches the agent how to use it correctly, in-band, at the point of failure.
