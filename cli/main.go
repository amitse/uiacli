package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"
)

var port = 9721
var baseURL string

func main() {
	// Parse --port from args or env
	if envPort := os.Getenv("UIACLI_PORT"); envPort != "" {
		if p, err := strconv.Atoi(envPort); err == nil {
			port = p
		}
	}

	args := os.Args[1:]
	args = extractPort(args)
	baseURL = fmt.Sprintf("http://localhost:%d", port)

	if len(args) == 0 {
		printHelp()
		return
	}

	cmd := strings.ToLower(args[0])
	cmdArgs := args[1:]

	// Handle --help for any command
	if hasFlag(cmdArgs, "--help") || hasFlag(cmdArgs, "-h") {
		printCommandHelp(cmd)
		return
	}

	switch cmd {
	case "serve":
		cmdServe(cmdArgs)
	case "stop":
		cmdStop()
	case "state":
		ensureAndGet("/state")
	case "windows":
		ensureAndGet("/windows")
	case "focus":
		cmdFocus(cmdArgs)
	case "tree":
		cmdTree(cmdArgs)
	case "find":
		cmdFind(cmdArgs)
	case "focused":
		ensureAndGet("/focused")
	case "click":
		cmdClick(cmdArgs)
	case "type":
		cmdType(cmdArgs)
	case "key":
		cmdKey(cmdArgs)
	case "batch":
		cmdBatch(cmdArgs)
	case "screenshot":
		cmdScreenshot(cmdArgs)
	case "wait":
		cmdWait(cmdArgs)
	case "processes":
		ensureAndGet("/processes")
	case "launch":
		cmdLaunch(cmdArgs)
	case "clipboard":
		cmdClipboard(cmdArgs)
	case "highlight":
		cmdHighlight(cmdArgs)
	case "annotate":
		cmdAnnotate(cmdArgs)
	case "overlay":
		cmdOverlay(cmdArgs)
	case "--help", "-h", "help":
		printHelp()
	default:
		writeError("UNKNOWN_COMMAND", fmt.Sprintf("Unknown command: %s", cmd),
			"Run 'uia --help' to see available commands.")
		os.Exit(1)
	}
}

// ==================== COMMANDS ====================

func cmdServe(args []string) {
	bg := hasFlag(args, "--background")
	serverPath := findServerProject()
	if serverPath == "" {
		writeError("SERVER_NOT_FOUND", "Cannot find Uia.Server project.",
			"Set UIACLI_SERVER_PATH or run from repo root.")
		os.Exit(2)
		return
	}
	if bg {
		info("Starting server in background on port %d...", port)
		startServerProcess(serverPath)
		if !waitForServer(10 * time.Second) {
			writeError("SERVER_START_FAILED", "Server did not start within 10s.",
				"Try 'uia serve' (foreground) to see errors.")
			os.Exit(3)
			return
		}
		fmt.Println(httpGet("/health"))
	} else {
		var cmd *exec.Cmd
		if strings.HasPrefix(serverPath, "dotnet:") {
			projectPath := strings.TrimPrefix(serverPath, "dotnet:")
			cmd = exec.Command("dotnet", "run", "--project", projectPath, "--", "--port", strconv.Itoa(port))
		} else {
			cmd = exec.Command(serverPath, "--port", strconv.Itoa(port))
		}
		cmd.Stdout = os.Stdout
		cmd.Stderr = os.Stderr
		cmd.Run()
	}
}

func cmdStop() {
	pidFile := filepath.Join(os.TempDir(), "uia-server.pid")
	data, err := os.ReadFile(pidFile)
	if err != nil {
		writeError("SERVER_NOT_RUNNING", "No server PID file found.",
			"Use 'uia serve' to start the server.")
		os.Exit(1)
		return
	}
	pid, _ := strconv.Atoi(strings.TrimSpace(string(data)))
	if pid > 0 {
		if p, err := os.FindProcess(pid); err == nil {
			p.Kill()
		}
	}
	os.Remove(pidFile)
	os.Remove(filepath.Join(os.TempDir(), "uia-server.port"))
	fmt.Printf(`{"ok":true,"data":{"stopped":true,"pid":%d}}%s`, pid, "\n")
}

func cmdFocus(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Focus requires a window argument.",
			"Example: uia focus Calculator")
		os.Exit(1)
		return
	}
	ensureServer()
	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}
	fmt.Println(httpPost(fmt.Sprintf("/windows/%s/focus", handle), "{}"))
}

func cmdTree(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Tree requires a window argument.",
			"Example: uia tree Calculator --depth 2")
		os.Exit(1)
		return
	}
	ensureServer()
	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}
	depth := flagValue(args, "--depth", "3")
	fmt.Println(httpGet(fmt.Sprintf("/windows/%s/tree?depth=%s", handle, depth)))
}

func cmdFind(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Find requires a window argument.",
			"Example: uia find Calculator --name Equals --type Button")
		os.Exit(1)
		return
	}
	ensureServer()
	name := flagValue(args, "--name", "")
	typ := flagValue(args, "--type", "")
	id := flagValue(args, "--id", "")
	if name == "" && typ == "" && id == "" {
		writeError("NO_FILTER", "Provide at least one filter: --name, --type, or --id.",
			"Example: uia find Calculator --name Equals --type Button")
		os.Exit(1)
		return
	}
	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}
	qs := url.Values{}
	if name != "" {
		qs.Set("name", name)
	}
	if typ != "" {
		qs.Set("type", typ)
	}
	if id != "" {
		qs.Set("id", id)
	}
	fmt.Println(httpGet(fmt.Sprintf("/windows/%s/find?%s", handle, qs.Encode())))
}

func cmdClick(args []string) {
	ensureServer()
	x := flagValue(args, "--x", "")
	y := flagValue(args, "--y", "")
	name := flagValue(args, "--name", "")
	typ := flagValue(args, "--type", "")
	id := flagValue(args, "--id", "")
	window := flagValue(args, "--window", "")

	hasCoords := x != "" && y != ""
	hasElement := name != "" || typ != "" || id != ""

	if !hasCoords && !hasElement {
		writeError("NO_TARGET", "Click requires coordinates (--x, --y) or element query (--name, --type, --id).",
			"Examples:\n  uia click --x 500 --y 300\n  uia click --window Calculator --name Five")
		os.Exit(1)
		return
	}
	if hasElement && window == "" {
		writeError("NO_WINDOW", "When clicking by element, --window is required.",
			"Example: uia click --window Calculator --name Five")
		os.Exit(1)
		return
	}

	action := map[string]interface{}{"type": "click"}
	if hasCoords {
		xi, _ := strconv.Atoi(x)
		yi, _ := strconv.Atoi(y)
		action["x"] = xi
		action["y"] = yi
	}
	if hasElement {
		elem := map[string]interface{}{}
		if name != "" {
			elem["name"] = name
		}
		if typ != "" {
			elem["controlType"] = typ
		}
		if id != "" {
			elem["automationId"] = id
		}
		action["element"] = elem
	}
	if window != "" {
		action["window"] = window
	}
	body, _ := json.Marshal(action)
	fmt.Println(httpPost("/action", string(body)))
}

func cmdType(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Type requires text argument.",
			"Example: uia type \"Hello World\"")
		os.Exit(1)
		return
	}
	ensureServer()
	method := flagValue(args, "--method", "auto")
	text := args[0]
	body, _ := json.Marshal(map[string]string{"type": "type", "text": text, "method": method})
	fmt.Println(httpPost("/action", string(body)))
}

func cmdKey(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Key requires a combo argument.",
			"Example: uia key ctrl+c")
		os.Exit(1)
		return
	}
	ensureServer()
	body, _ := json.Marshal(map[string]string{"type": "key", "combo": args[0]})
	fmt.Println(httpPost("/action", string(body)))
}

func cmdBatch(args []string) {
	ensureServer()
	verbose := hasFlag(args, "--verbose")
	var body string

	// Get JSON from file, arg, or stdin
	jsonArg := ""
	for _, a := range args {
		if a != "--verbose" && !strings.HasPrefix(a, "--") {
			jsonArg = a
			break
		}
	}

	if jsonArg != "" {
		if data, err := os.ReadFile(jsonArg); err == nil {
			body = string(data)
		} else {
			body = jsonArg
		}
	} else {
		data, _ := io.ReadAll(os.Stdin)
		body = string(data)
	}

	body = strings.TrimSpace(body)
	if body == "" {
		writeError("EMPTY_INPUT", "No batch JSON provided.",
			"Provide a JSON file, inline JSON, or pipe via stdin.\nExample: uia batch actions.json")
		os.Exit(1)
		return
	}

	if strings.HasPrefix(body, "[") {
		wrapper := map[string]interface{}{
			"actions": json.RawMessage(body),
			"onError": "stop",
			"verbose": verbose,
		}
		b, _ := json.Marshal(wrapper)
		body = string(b)
	} else if verbose && !strings.Contains(body, "\"verbose\"") {
		body = strings.TrimRight(body, "}") + ",\"verbose\":true}"
	}

	fmt.Println(httpPost("/batch", body))
}

func cmdScreenshot(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Screenshot requires a window argument.",
			"Example: uia screenshot Calculator")
		os.Exit(1)
		return
	}
	ensureServer()
	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}
	h, _ := strconv.ParseInt(handle, 10, 64)
	body, _ := json.Marshal(map[string]int64{"handle": h})
	fmt.Println(httpPost("/screenshot", string(body)))
}

func cmdWait(args []string) {
	ensureServer()
	name := flagValue(args, "--name", "")
	typ := flagValue(args, "--type", "")
	id := flagValue(args, "--id", "")
	window := flagValue(args, "--window", "")
	timeout := flagValue(args, "--timeout", "5000")

	if name == "" && typ == "" && id == "" {
		writeError("NO_CONDITION", "Wait requires a condition: --name, --type, or --id.",
			"Example: uia wait --window Calculator --name Equals --timeout 5000")
		os.Exit(1)
		return
	}

	timeoutMs, _ := strconv.Atoi(timeout)
	elem := map[string]interface{}{}
	if name != "" {
		elem["name"] = name
	}
	if typ != "" {
		elem["controlType"] = typ
	}
	if id != "" {
		elem["automationId"] = id
	}
	action := map[string]interface{}{
		"type":      "wait",
		"window":    window,
		"timeoutMs": timeoutMs,
		"until":     map[string]interface{}{"elementExists": elem},
	}
	body, _ := json.Marshal(action)
	fmt.Println(httpPost("/action", string(body)))
}

func cmdLaunch(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Launch requires an app path.",
			"Example: uia launch calc.exe")
		os.Exit(1)
		return
	}
	ensureServer()
	body, _ := json.Marshal(map[string]string{"path": args[0]})
	fmt.Println(httpPost("/launch", string(body)))
}

func cmdClipboard(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Usage: uia clipboard get | uia clipboard set <text>",
			"Examples:\n  uia clipboard get\n  uia clipboard set \"Hello\"")
		os.Exit(1)
		return
	}
	ensureServer()
	switch strings.ToLower(args[0]) {
	case "get":
		fmt.Println(httpGet("/clipboard"))
	case "set":
		if len(args) < 2 {
			writeError("MISSING_ARG", "Clipboard set requires text.", "Example: uia clipboard set \"Hello\"")
			os.Exit(1)
			return
		}
		body, _ := json.Marshal(map[string]string{"text": args[1]})
		fmt.Println(httpPost("/clipboard", string(body)))
	default:
		writeError("UNKNOWN_SUBCOMMAND", fmt.Sprintf("Unknown clipboard subcommand: %s", args[0]),
			"Use 'uia clipboard get' or 'uia clipboard set <text>'")
		os.Exit(1)
	}
}

func cmdHighlight(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Highlight requires a window argument.",
			"Example: uia highlight Calculator --name Equals --style action")
		os.Exit(1)
		return
	}
	ensureServer()
	name := flagValue(args, "--name", "")
	typ := flagValue(args, "--type", "")
	id := flagValue(args, "--id", "")
	style := flagValue(args, "--style", "focus")
	fade := flagValue(args, "--fade", "2000")

	if name == "" && typ == "" && id == "" {
		writeError("NO_ELEMENT", "Highlight requires --name, --type, or --id.",
			"Example: uia highlight Calculator --name Equals --style action")
		os.Exit(1)
		return
	}
	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}

	qs := url.Values{}
	if name != "" {
		qs.Set("name", name)
	}
	if typ != "" {
		qs.Set("type", typ)
	}
	if id != "" {
		qs.Set("id", id)
	}
	findResp := httpGet(fmt.Sprintf("/windows/%s/find?%s", handle, qs.Encode()))
	var findResult map[string]interface{}
	json.Unmarshal([]byte(findResp), &findResult)
	count, _ := findResult["count"].(float64)
	if count == 0 {
		writeError("ELEMENT_NOT_FOUND", "No element found matching the query.",
			"Run 'uia find <window> --type Button' to see available elements.")
		os.Exit(1)
		return
	}
	elements := findResult["elements"].([]interface{})
	bounds := elements[0].(map[string]interface{})["bounds"].(map[string]interface{})
	fadeMs, _ := strconv.Atoi(fade)
	hlBody, _ := json.Marshal(map[string]interface{}{
		"x": bounds["x"], "y": bounds["y"],
		"width": bounds["width"], "height": bounds["height"],
		"style": style, "fadeMs": fadeMs,
	})
	fmt.Println(httpPost("/overlay/highlight", string(hlBody)))
}

func cmdAnnotate(args []string) {
	if len(args) == 0 {
		writeError("MISSING_ARG", "Annotate requires a window argument.",
			"Example: uia annotate Calculator --name Equals --text \"About to click\"")
		os.Exit(1)
		return
	}
	ensureServer()
	text := flagValue(args, "--text", "")
	name := flagValue(args, "--name", "")
	style := flagValue(args, "--style", "info")

	if text == "" {
		writeError("MISSING_ARG", "Annotate requires --text.",
			"Example: uia annotate Calculator --text \"Step 1\" --style reasoning")
		os.Exit(1)
		return
	}

	handle := resolveWindow(args[0])
	if handle == "" {
		return
	}

	x, y := 100, 100
	if name != "" {
		findResp := httpGet(fmt.Sprintf("/windows/%s/find?name=%s", handle, url.QueryEscape(name)))
		var fr map[string]interface{}
		json.Unmarshal([]byte(findResp), &fr)
		if c, _ := fr["count"].(float64); c > 0 {
			els := fr["elements"].([]interface{})
			b := els[0].(map[string]interface{})["bounds"].(map[string]interface{})
			x = int(b["x"].(float64)) + int(b["width"].(float64))
			y = int(b["y"].(float64))
		}
	}

	body, _ := json.Marshal(map[string]interface{}{"text": text, "x": x, "y": y, "style": style})
	fmt.Println(httpPost("/overlay/annotate", string(body)))
}

func cmdOverlay(args []string) {
	if len(args) == 0 || strings.ToLower(args[0]) != "clear" {
		writeError("MISSING_ARG", "Usage: uia overlay clear",
			"Removes all highlights, annotations, and ghost cursor.")
		os.Exit(1)
		return
	}
	ensureServer()
	fmt.Println(httpDelete("/overlay"))
}

// ==================== HTTP HELPERS ====================

func httpGet(path string) string {
	resp, err := http.Get(baseURL + path)
	if err != nil {
		return fmt.Sprintf(`{"ok":false,"error":{"code":"HTTP_ERROR","message":"%s","hint":"Is the server running? Try 'uia serve'."}}`, escapeJSON(err.Error()))
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	return string(body)
}

func httpPost(path, jsonBody string) string {
	resp, err := http.Post(baseURL+path, "application/json", bytes.NewBufferString(jsonBody))
	if err != nil {
		return fmt.Sprintf(`{"ok":false,"error":{"code":"HTTP_ERROR","message":"%s","hint":"Is the server running? Try 'uia serve'."}}`, escapeJSON(err.Error()))
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	return string(body)
}

func httpDelete(path string) string {
	req, _ := http.NewRequest("DELETE", baseURL+path, nil)
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return fmt.Sprintf(`{"ok":false,"error":{"code":"HTTP_ERROR","message":"%s"}}`, escapeJSON(err.Error()))
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	return string(body)
}

func ensureAndGet(path string) {
	ensureServer()
	fmt.Println(httpGet(path))
}

// ==================== SERVER MANAGEMENT ====================

func isServerRunning() bool {
	client := &http.Client{Timeout: 2 * time.Second}
	resp, err := client.Get(baseURL + "/health")
	if err != nil {
		return false
	}
	resp.Body.Close()
	return resp.StatusCode == 200
}

func ensureServer() {
	if isServerRunning() {
		return
	}
	info("Server not running. Auto-starting...")
	serverPath := findServerProject()
	if serverPath == "" {
		writeError("SERVER_NOT_FOUND", "Server not running and Uia.Server project not found.",
			"Start manually with 'uia serve', or set UIACLI_SERVER_PATH.")
		os.Exit(2)
	}
	startServerProcess(serverPath)
	if !waitForServer(10 * time.Second) {
		writeError("SERVER_START_FAILED", "Server did not start within 10s.",
			"Try 'uia serve' (foreground) to see errors.")
		os.Exit(3)
	}
	info("Server started.")
}

func startServerProcess(serverPath string) {
	var cmd *exec.Cmd
	if strings.HasPrefix(serverPath, "dotnet:") {
		projectPath := strings.TrimPrefix(serverPath, "dotnet:")
		info("Using dotnet run (slower). Publish the server for faster startup: dotnet publish src\\Uia.Server -c Release -o publish\\server-fd")
		cmd = exec.Command("dotnet", "run", "--project", projectPath, "--", "--port", strconv.Itoa(port))
	} else {
		cmd = exec.Command(serverPath, "--port", strconv.Itoa(port))
	}
	cmd.Stdout = nil
	cmd.Stderr = nil
	cmd.Start()
}

func waitForServer(timeout time.Duration) bool {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if isServerRunning() {
			return true
		}
		time.Sleep(200 * time.Millisecond)
	}
	return false
}

func findServerProject() string {
	if p := os.Getenv("UIACLI_SERVER_PATH"); p != "" {
		if _, err := os.Stat(p); err == nil {
			return p
		}
	}

	exe, _ := os.Executable()
	dir := filepath.Dir(exe)

	// Look for the published server exe first (fastest)
	exeCandidates := []string{
		filepath.Join(dir, "Uia.Server.exe"),
		filepath.Join(dir, "..", "server-fd", "Uia.Server.exe"),
		filepath.Join(dir, "..", "publish", "server-fd", "Uia.Server.exe"),
	}
	cwd, _ := os.Getwd()
	exeCandidates = append(exeCandidates,
		filepath.Join(cwd, "publish", "server-fd", "Uia.Server.exe"),
		filepath.Join(cwd, "Uia.Server.exe"),
	)

	for _, c := range exeCandidates {
		if _, err := os.Stat(c); err == nil {
			return c
		}
	}

	// Fallback: find the .csproj and use dotnet run
	projCandidates := []string{
		filepath.Join(dir, "..", "Uia.Server"),
		filepath.Join(dir, "..", "..", "src", "Uia.Server"),
		filepath.Join(dir, "..", "..", "..", "..", "src", "Uia.Server"),
	}
	projCandidates = append(projCandidates, filepath.Join(cwd, "src", "Uia.Server"))

	for _, c := range projCandidates {
		if _, err := os.Stat(filepath.Join(c, "Uia.Server.csproj")); err == nil {
			// Return as "dotnet:path" to signal we need dotnet run
			return "dotnet:" + c
		}
	}
	return ""
}

// ==================== WINDOW RESOLUTION ====================

func resolveWindow(query string) string {
	resp := httpGet("/windows")
	var windows []map[string]interface{}
	if err := json.Unmarshal([]byte(resp), &windows); err != nil {
		writeError("PARSE_ERROR", "Failed to parse window list.", "Server may be in a bad state.")
		os.Exit(1)
		return ""
	}

	// Try handle
	if _, err := strconv.ParseInt(query, 10, 64); err == nil {
		for _, w := range windows {
			if fmt.Sprintf("%.0f", w["handle"]) == query {
				return query
			}
		}
	}

	// Try title substring or process name
	queryLower := strings.ToLower(query)
	for _, w := range windows {
		title, _ := w["title"].(string)
		proc, _ := w["processName"].(string)
		if strings.Contains(strings.ToLower(title), queryLower) {
			return fmt.Sprintf("%.0f", w["handle"])
		}
		if strings.EqualFold(proc, query) {
			return fmt.Sprintf("%.0f", w["handle"])
		}
	}

	// Not found — build helpful error
	var titles []string
	for _, w := range windows {
		if t, ok := w["title"].(string); ok && t != "" {
			if len(t) > 45 {
				t = t[:45] + "..."
			}
			titles = append(titles, "'"+t+"'")
			if len(titles) >= 8 {
				break
			}
		}
	}
	hint := fmt.Sprintf("Open windows: %s\nRun 'uia windows' to see all.", strings.Join(titles, ", "))
	writeError("WINDOW_NOT_FOUND",
		fmt.Sprintf("No window matching '%s'. Is it running?", query), hint)
	os.Exit(1)
	return ""
}

// ==================== OUTPUT HELPERS ====================

func writeError(code, message, hint string) {
	fmt.Printf(`{"ok":false,"error":{"code":"%s","message":"%s","hint":"%s"}}%s`,
		escapeJSON(code), escapeJSON(message), escapeJSON(hint), "\n")
}

func info(format string, args ...interface{}) {
	fmt.Fprintf(os.Stderr, "[uia] "+format+"\n", args...)
}

func escapeJSON(s string) string {
	s = strings.ReplaceAll(s, `\`, `\\`)
	s = strings.ReplaceAll(s, `"`, `\"`)
	s = strings.ReplaceAll(s, "\n", `\n`)
	s = strings.ReplaceAll(s, "\t", `\t`)
	return s
}

// ==================== FLAG HELPERS ====================

func flagValue(args []string, flag, defaultVal string) string {
	for i, a := range args {
		if a == flag && i+1 < len(args) {
			return args[i+1]
		}
	}
	return defaultVal
}

func hasFlag(args []string, flag string) bool {
	for _, a := range args {
		if a == flag {
			return true
		}
	}
	return false
}

func extractPort(args []string) []string {
	var result []string
	for i := 0; i < len(args); i++ {
		if args[i] == "--port" && i+1 < len(args) {
			if p, err := strconv.Atoi(args[i+1]); err == nil {
				port = p
			}
			i++ // skip value
		} else {
			result = append(result, args[i])
		}
	}
	return result
}

// ==================== HELP ====================

func printHelp() {
	fmt.Println(`UIA CLI — Windows UI Automation tool for agents

Automate any Windows desktop application. All output is JSON.
Server auto-starts on first use.

Commands:
  serve [--background]              Start the UIA server
  stop                              Stop the UIA server
  state                             Full desktop snapshot
  windows                           List all windows
  focus <window>                    Bring window to foreground
  tree <window> [--depth N]         Print UI automation tree
  find <window> --name/--type/--id  Find elements
  focused                           Get focused element
  click --x N --y N                 Click at coordinates
  click --window W --name/--id E    Click element
  type <text> [--method auto]       Type text
  key <combo>                       Press key (ctrl+c, enter, alt+f4)
  batch <file.json> [--verbose]     Execute action batch
  screenshot <window>               Capture window to PNG
  wait --window W --name E          Wait for element to appear
  processes                         List windowed processes
  launch <app>                      Launch application
  clipboard get|set <text>          Read/write clipboard
  highlight <window> --name E       Highlight element
  annotate <window> --text T        Show annotation
  overlay clear                     Clear all overlays

Examples:
  uia windows
  uia tree Calculator --depth 2
  uia click --window Calculator --name Five
  uia batch actions.json --verbose
  uia key ctrl+c
  uia launch calc.exe

Exit codes: 0=success, 1=action failed, 2=setup error, 3=timeout
Platform: ` + runtime.GOOS + "/" + runtime.GOARCH)
}

func printCommandHelp(cmd string) {
	helps := map[string]string{
		"serve": `uia serve [--background] [--port N]

Start the UIA server. Other commands auto-start it.

Options:
  --background  Start detached, return immediately
  --port N      Server port (default: 9721, env: UIACLI_PORT)

Examples:
  uia serve                    Start in foreground
  uia serve --background       Start detached`,

		"click": `uia click [--x N --y N | --window W --name/--type/--id E]

Click at screen coordinates or on a named element.
Uses UIA InvokePattern when available, SendInput fallback.

Options:
  --x N            Screen X coordinate
  --y N            Screen Y coordinate
  --window W       Window context (required for element clicks)
  --name NAME      Element name
  --type TYPE      Control type (Button, Edit, Text, etc.)
  --id ID          Automation ID

Examples:
  uia click --x 500 --y 300
  uia click --window Calculator --name Five
  uia click --window Calculator --id num5Button --type Button`,

		"batch": `uia batch <file.json> [--verbose]

Execute multiple actions in one call. Fastest way to automate.

Options:
  --verbose     Show overlay annotations for each action

Batch JSON format:
  {"actions": [{"type": "click", "element": {"name": "OK"}}],
   "window": "App", "onError": "stop", "verbose": false}

Action types: click, type, key, scroll, drag, wait, read, focus, screenshot

Examples:
  uia batch actions.json
  uia batch actions.json --verbose`,

		"key": `uia key <combo>

Press a key combination.

Format: modifier+key (e.g., ctrl+c, alt+f4, shift+tab)
Modifiers: ctrl, alt, shift, win
Keys: enter, tab, escape, space, backspace, delete,
  up, down, left, right, home, end, pageup, pagedown,
  f1-f12, a-z, 0-9

Examples:
  uia key enter
  uia key ctrl+c
  uia key alt+f4`,

		"find": `uia find <window> [--name N] [--type T] [--id I]

Find elements in a window. At least one filter required.

Options:
  --name NAME    Element name
  --type TYPE    Control type (Button, Edit, Text, Hyperlink, etc.)
  --id ID        Automation ID (most stable identifier)

Examples:
  uia find Calculator --name Equals --type Button
  uia find Calculator --id num5Button`,

		"tree": `uia tree <window> [--depth N]

Print the UI automation tree of a window as nested JSON.
Returns: name, controlType, automationId, bounds, isEnabled, value, patterns.

Options:
  --depth N      Maximum depth (default: 3)

Examples:
  uia tree Calculator --depth 2
  uia tree "Visual Studio Code"`,

		"type": `uia type <text> [--method auto|keys|paste|value]

Type text into the focused element.

Options:
  --method M     Input method: auto (default), keys, paste, value

Examples:
  uia type "Hello World"
  uia type "long text" --method paste`,

		"wait": `uia wait --window W [--name N] [--type T] [--id I] [--timeout MS]

Wait until a UI element appears (or timeout).

Options:
  --window W     Window to watch
  --name NAME    Element name to wait for
  --type TYPE    Element control type
  --id ID        Element automation ID
  --timeout MS   Timeout in ms (default: 5000)

Examples:
  uia wait --window App --name "Loading" --timeout 10000`,
	}

	if h, ok := helps[cmd]; ok {
		fmt.Println(h)
	} else {
		fmt.Printf("No detailed help for '%s'. Run 'uia --help' for command list.\n", cmd)
	}
}
