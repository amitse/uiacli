# UIA CLI

A client-server system that exposes Windows UI Automation (UIA) APIs through a CLI, enabling agents and humans to programmatically observe and control any Windows desktop application.

## Language

### Core Concepts

**Element**:
A single node in the Windows UI Automation tree — a button, text field, menu item, window, or any other UI component.
_Avoid_: Control, widget, component

**Automation Tree**:
The hierarchical structure of all **Elements** on the desktop, as exposed by Windows UIA.
_Avoid_: DOM, element tree, UI tree

**Window**:
A top-level **Element**, typically an application's main frame.
_Avoid_: App, frame, form

**Action**:
A single UI operation performed against an **Element** or coordinate — click, type, key press, drag, scroll.
_Avoid_: Command, operation, event

**Action Batch**:
An ordered sequence of **Actions** sent to the **UIA Server** for execution without per-action round-trips.
_Avoid_: Macro, script, sequence

### Architecture

**UIA Server**:
The long-running process that holds the UIA automation context, executes **Actions**, and manages the **Overlay**.
_Avoid_: Backend, daemon, service

**UIA CLI**:
The command-line client that sends requests to the **UIA Server** over HTTP.
_Avoid_: Client, frontend, shell

### Visual Feedback

**Overlay**:
A transparent always-on-top window managed by the **UIA Server** that renders visual feedback on top of the desktop.
_Avoid_: HUD, OSD, layer

**Highlight**:
A colored rectangle drawn by the **Overlay** around a target **Element** to indicate agent focus.
_Avoid_: Selection, outline, border

**Ghost Cursor**:
A synthetic mouse pointer rendered by the **Overlay** that visually mirrors agent mouse movements without moving the real cursor.
_Avoid_: Fake cursor, virtual cursor, phantom cursor

**Annotation**:
A tooltip, label, or comment rendered by the **Overlay** near an **Element** to communicate agent intent or reasoning.
_Avoid_: Tooltip, bubble, comment

**Verbose Mode**:
A batch execution mode where the **Overlay** automatically renders a **Highlight**, **Ghost Cursor** movement, and **Annotation** for each **Action**, using the action's optional `description` field or an auto-generated label.
_Avoid_: Debug mode, trace mode

**Error Envelope**:
The structured JSON format used for all **UIA CLI** output: `{"ok": true/false, ...}`. Errors always include `code` (machine-readable), `message` (what went wrong), and `hint` (what to do next).
_Avoid_: Error response, error object

## Relationships

- A **Window** is a top-level **Element** in the **Automation Tree**
- An **Action** targets either an **Element** or a screen coordinate
- An **Action Batch** contains one or more ordered **Actions**
- The **UIA Server** executes **Actions** and controls the **Overlay**
- The **UIA CLI** sends **Action Batches** to the **UIA Server**
- The **Overlay** renders **Highlights**, **Ghost Cursor**, and **Annotations**

## Example dialogue

> **Dev:** "When the agent wants to click a button, does it send an **Action** to the **UIA Server**?"
> **Domain expert:** "Yes — the agent uses the **UIA CLI** to send a click **Action**. For speed, it should batch multiple **Actions** into an **Action Batch** so the server executes them without waiting."

> **Dev:** "How does a human watching know what the agent is doing?"
> **Domain expert:** "The **UIA Server** drives the **Overlay** — it shows a **Ghost Cursor** moving to the target, a **Highlight** around the **Element**, and an **Annotation** explaining what it's about to do."

## Flagged ambiguities

- "cursor" could mean the real system cursor or the **Ghost Cursor** — resolved: the **Ghost Cursor** is a visual-only indicator rendered by the **Overlay**; the real cursor is moved only when performing actual mouse **Actions**.
- "screenshot" is not an **Action** — it is a read-only observation. Kept as a separate capability, not part of the action vocabulary.
