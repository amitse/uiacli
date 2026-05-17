import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { joinSession } from "@github/copilot-sdk/extension";

// Find uia.exe — check PATH, then common install locations
function findUia() {
    const candidates = [
        join(process.cwd(), "uia.exe"),
        join(process.env.LOCALAPPDATA || "", "Programs", "uiacli", "uia.exe"),
    ];
    for (const p of candidates) {
        if (existsSync(p)) return p;
    }
    return "uia.exe"; // hope it's on PATH
}

const UIA = findUia();

function runUia(args) {
    return new Promise((resolve) => {
        execFile(UIA, args, { timeout: 30000 }, (err, stdout, stderr) => {
            if (err && !stdout) {
                resolve(JSON.stringify({ ok: false, error: { code: "EXEC_ERROR", message: err.message } }));
            } else {
                resolve(stdout.trim());
            }
        });
    });
}

const session = await joinSession({
    hooks: {
        onSessionStart: async () => {
            if (process.platform !== "win32") return;
            await session.log("UIA CLI extension loaded — Windows desktop automation tools available");
        },
    },
    tools: [
        {
            name: "uia_windows",
            description: "List all open windows on the desktop. Returns JSON array with handle, title, processName, bounds, and state for each window.",
            parameters: { type: "object", properties: {}, required: [] },
            handler: async () => runUia(["windows"]),
        },
        {
            name: "uia_state",
            description: "Get a full desktop snapshot: all windows, focused window, focused element, cursor position, and screen info.",
            parameters: { type: "object", properties: {}, required: [] },
            handler: async () => runUia(["state"]),
        },
        {
            name: "uia_tree",
            description: "Inspect the UI Automation element tree of a window. Returns nested JSON hierarchy of all UI elements with name, controlType, automationId, bounds, and patterns.",
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle" },
                    depth: { type: "number", description: "Tree depth (default: 3)" },
                },
                required: ["window"],
            },
            handler: async (args) => {
                const a = ["tree", args.window];
                if (args.depth) a.push("--depth", String(args.depth));
                return runUia(a);
            },
        },
        {
            name: "uia_find",
            description: "Find UI elements in a window by name, control type, or automation ID.",
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle" },
                    name: { type: "string", description: "Element name to search for" },
                    type: { type: "string", description: "Control type (Button, Edit, Text, etc.)" },
                    id: { type: "string", description: "Automation ID" },
                },
                required: ["window"],
            },
            handler: async (args) => {
                const a = ["find", args.window];
                if (args.name) a.push("--name", args.name);
                if (args.type) a.push("--type", args.type);
                if (args.id) a.push("--id", args.id);
                return runUia(a);
            },
        },
        {
            name: "uia_click",
            description: "Click a UI element by name/type/id, or click at specific screen coordinates.",
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle (required when using element query)" },
                    name: { type: "string", description: "Element name to click" },
                    type: { type: "string", description: "Control type of element" },
                    id: { type: "string", description: "Automation ID of element" },
                    x: { type: "number", description: "Screen X coordinate" },
                    y: { type: "number", description: "Screen Y coordinate" },
                },
                required: [],
            },
            handler: async (args) => {
                const a = ["click"];
                if (args.window) a.push("--window", args.window);
                if (args.name) a.push("--name", args.name);
                if (args.type) a.push("--type", args.type);
                if (args.id) a.push("--id", args.id);
                if (args.x != null) a.push("--x", String(args.x));
                if (args.y != null) a.push("--y", String(args.y));
                return runUia(a);
            },
        },
        {
            name: "uia_type",
            description: "Type text into the focused element or a specified element in a window.",
            parameters: {
                type: "object",
                properties: {
                    text: { type: "string", description: "Text to type" },
                    window: { type: "string", description: "Window title substring or handle" },
                    name: { type: "string", description: "Element name to type into" },
                    id: { type: "string", description: "Automation ID of element" },
                },
                required: ["text"],
            },
            handler: async (args) => {
                const a = ["type", "--text", args.text];
                if (args.window) a.push("--window", args.window);
                if (args.name) a.push("--name", args.name);
                if (args.id) a.push("--id", args.id);
                return runUia(a);
            },
        },
        {
            name: "uia_key",
            description: "Send a key combination (e.g., ctrl+c, alt+f4, enter, tab).",
            parameters: {
                type: "object",
                properties: {
                    combo: { type: "string", description: "Key combo string (e.g., ctrl+c, alt+tab, enter)" },
                    window: { type: "string", description: "Window to target" },
                },
                required: ["combo"],
            },
            handler: async (args) => {
                const a = ["key", "--combo", args.combo];
                if (args.window) a.push("--window", args.window);
                return runUia(a);
            },
        },
        {
            name: "uia_screenshot",
            description: "Take a screenshot of a window. Returns the file path and metadata.",
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle" },
                },
                required: ["window"],
            },
            handler: async (args) => runUia(["screenshot", "--window", args.window]),
        },
        {
            name: "uia_focus",
            description: "Bring a window to the foreground.",
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle" },
                },
                required: ["window"],
            },
            handler: async (args) => runUia(["focus", args.window]),
        },
        {
            name: "uia_launch",
            description: "Launch a Windows application by path or name (e.g., calc.exe, notepad.exe, mspaint.exe).",
            parameters: {
                type: "object",
                properties: {
                    path: { type: "string", description: "Application path or name" },
                },
                required: ["path"],
            },
            handler: async (args) => runUia(["launch", args.path]),
        },
        {
            name: "uia_batch",
            description: `Execute a batch of UI actions in sequence on a window. Minimizes round-trip overhead.
Each action has a type and parameters. Supported types: click, type, key, read, wait, scroll, select, toggle, drag, freehand, screenshot.
Example actions: {"type":"click","element":{"name":"Five"}}, {"type":"key","combo":"ctrl+a"}, {"type":"read","element":{"automationId":"result"}}`,
            parameters: {
                type: "object",
                properties: {
                    window: { type: "string", description: "Window title substring or handle" },
                    actions: {
                        type: "array",
                        description: "Array of action objects",
                        items: { type: "object" },
                    },
                    verbose: { type: "boolean", description: "Show overlay visuals (ghost cursor, highlights, annotations)" },
                },
                required: ["window", "actions"],
            },
            handler: async (args) => {
                const { writeFileSync, unlinkSync } = await import("node:fs");
                const { tmpdir } = await import("node:os");
                const batchFile = join(tmpdir(), `uia-batch-${Date.now()}.json`);
                const batch = {
                    window: args.window,
                    actions: args.actions,
                    verbose: args.verbose || false,
                };
                writeFileSync(batchFile, JSON.stringify(batch));
                try {
                    const result = await runUia(["batch", batchFile]);
                    return result;
                } finally {
                    try { unlinkSync(batchFile); } catch {}
                }
            },
        },
        {
            name: "uia_clipboard",
            description: "Get or set the Windows clipboard text.",
            parameters: {
                type: "object",
                properties: {
                    action: { type: "string", enum: ["get", "set"], description: "Get or set clipboard" },
                    text: { type: "string", description: "Text to set (only for 'set')" },
                },
                required: ["action"],
            },
            handler: async (args) => {
                if (args.action === "set" && args.text) {
                    return runUia(["clipboard", "set", args.text]);
                }
                return runUia(["clipboard", "get"]);
            },
        },
    ],
});
