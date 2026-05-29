# SeelessUIA

Windows UI Automation CLI for AI agents. Native .NET tool that controls desktop applications through Microsoft UI Automation (UIA).

Uses the same snapshot + ref model as agent-browser: take a snapshot to discover elements (`@e1`, `@e2`), then interact by ref. Designed for AI agent workflows where context tokens are expensive.

## Installation

### NPM (recommended)

```bash
npm install -g seeless-uia
```

Requires .NET 10 Runtime. Prebuilt Windows binary included.

### From Source

```bash
git clone https://github.com/yaki1210/Seeless-UIA
cd Seeless-UIA
dotnet build SeelessUIA.slnx -c Release
```

### Requirements

- **Windows** -- UI Automation is a Windows-only API
- **.NET 10 Runtime** -- Required to run the prebuilt binary

## Quick Start

```bash
seeless-uia windows
  -> w1   Notepad     *Untitled - Notepad

seeless-uia snapshot w1 -i
  -> - button "OK" [ref=e1] clickable
     - button "Cancel" [ref=e2] clickable

seeless-uia click @e1
seeless-uia close
```

## Core Commands

| Command | Description |
|---------|-------------|
| `snapshot [wN]` | Take accessibility tree snapshot, return refs |
| `windows` | List visible windows with w1/w2 refs |
| `window wN` | Switch active window |
| `app launch <name>` | Launch app, register as wN |
| `daemon [--port <port>]` | Start daemon server (auto-starts on first command) |
| `close [wN]` | Close active window |
| `ping` | Check daemon health |

**Apps:** `notepad`, `calc`, `cmd`, `code`, `opencode`

## Interaction Commands

| Command | Description |
|---------|-------------|
| `click [wN] <sel>` | Click by ref or selector |
| `dblclick [wN] <sel>` | Double-click |
| `fill [wN] <sel> <text>` | Clear and fill an input |
| `type [wN] <sel> <text>` | Type text into an element |
| `press <key>` | Press a key or chord (`"Enter"`, `"Control+a"`) |
| `hover [wN] <sel>` | Hover mouse over element |
| `scroll [wN] <dir>` | Scroll up/down/left/right |
| `check [wN] <sel>` | Check a checkbox (state-aware, verifies after toggle) |
| `uncheck [wN] <sel>` | Uncheck a checkbox |
| `focus [wN] <sel>` | Set focus on element |
| `expand [wN] <sel>` | Expand via UIA ExpandCollapsePattern |
| `collapse [wN] <sel>` | Collapse via UIA ExpandCollapsePattern |
| `select [wN] <sel>` | Select via SelectionItemPattern |
| `scrollintoview [wN] <sel>` | Scroll element into view |
| `scroll_amount [wN] <sel>` | Native ScrollAmount (LargeIncrement) |
| `drag <src> <tgt>` | Drag and drop (10-step interpolation) |
| `screenshot [wN] [path] [--full]` | Capture window screenshot (silent via DWM; auto-restores minimized windows) |

Click supports `--button left|right|middle` and `--click-count 1|2`.

## Get & Is

| Command | Description |
|---------|-------------|
| `get text <sel>` | Read visible text (labels, titles, content) |
| `get value <sel>` | Read current input value |
| `get box <sel>` | Get bounding rectangle |
| `get count <sel>` | Count matching elements |
| `get attr <sel> <attr>` | Read UIA attribute (name, classname, automationid, controltype, etc.) |
| `is visible <sel>` | Check if element is visible |
| `is enabled <sel>` | Check if element is enabled |
| `is checked <sel>` | Check toggle state |

**Difference**: `get text` reads visible text from labels/titles/content. `get value` reads the current value of an input control.

## Find & Wait

| Command | Description |
|---------|-------------|
| `find role <role> <action> [--name <filter>]` | Find element by role and execute action |
| `find text <text> <action>` | Find element by text content |
| `find label <label> <action>` | Find input by associated label |
| `find placeholder <text> <action>` | Find input by placeholder text |
| `wait <sel>` | Wait for element to appear (default 30s) |
| `wait <ms>` | Wait for milliseconds |
| `wait --text <text>` | Wait for text to appear in window tree |

## Raw Input

| Command | Description |
|---------|-------------|
| `keydown <key>` | Press and hold a key |
| `keyup <key>` | Release a held key |
| `keyboard type <text>` | Type text at current focus (no selector) |
| `mouse move <x> <y>` | Move mouse to absolute coordinates |
| `mouse down/up [button]` | Mouse button press/release |
| `mouse wheel <dy>` | Mouse wheel scroll |

## Clipboard

| Command | Description |
|---------|-------------|
| `clipboard read` | Read text from clipboard |
| `clipboard write <text>` | Write text to clipboard |
| `clipboard copy` | Ctrl+C (copy selection) |
| `clipboard paste` | Ctrl+V (paste) |

## Window Management

Windows are identified by stable `w1`, `w2`, ... references assigned by `windows`. wN numbers are never reused: when a window closes, its wN is retired permanently.

```bash
seeless-uia windows           # List windows, discover wN refs
seeless-uia window w2         # Switch active window to w2
seeless-uia app launch calc  # Launch Calculator, auto-assign wN
seeless-uia snapshot -i       # Snapshot active window (no wN needed)
seeless-uia click @e3         # Click on active window
```

## Snapshot Options

| Flag | Description |
|------|-------------|
| `-i` | Interactive mode (flat, ref-only) |
| `-c` | Compact mode (remove empty structural elements) |
| `-d <n>` | Limit tree depth |
| `--raw` | Use UIA RawViewCondition (unfiltered tree) |
| `--no-clean` | Skip TreeCleaner — raw UIA tree for diagnostics |
| `--json` | Machine-readable JSON output |

## Global Options

| Flag | Description |
|------|-------------|
| `--json` | JSON output for all commands |
| `--port <port>` | Daemon port (default 9222) |
| `--timeout <ms>` | Operation timeout (default 30000ms) |

## Usage with AI Agents

The optimal AI workflow:

```bash
# 1. Discover windows and take snapshot
seeless-uia windows
seeless-uia snapshot w1 -i --json

# 2. AI parses the tree and refs from JSON output
# 3. Interact using refs from the snapshot
seeless-uia click @e2
seeless-uia fill @e3 "input text"

# 4. After interaction, get fresh snapshot
seeless-uia snapshot -i --json
```

Refs from a snapshot are valid until the window state changes (dialog opens, tab switches, window closes). Always take a fresh snapshot before interacting after a state change.

**Prefer semantic data over screenshots.** UIA exposes controls as structured text — buttons, inputs, labels with names and states — so AI agents can "read" the interface without seeing pixels. Use `snapshot` + `get text` / `get value` for most tasks. Reserve `screenshot` for visual verification by local VLM models.

## Architecture

SeelessUIA follows a CLI + Daemon architecture. The daemon auto-starts on the first command and persists between commands for fast subsequent operations. The CLI sends NDJSON requests over TCP to the daemon, which executes UIA operations directly against Windows automation APIs.

Inspired by agent-browser's snapshot + ref model, adapted for Windows native desktop applications via Microsoft UI Automation.
