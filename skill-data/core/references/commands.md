# Commands Reference

Complete reference of all SeelessUIA commands with flags, aliases, and JSON output schemas.

## Core Commands

### snapshot

Take an accessibility tree snapshot of a window.

```
seeless-uia snapshot [wN] [-i] [-c] [--raw] [-d <n>] [--json]
```

| Flag | Description |
|------|-------------|
| `wN` | Target window ref (e.g. w1). Uses active window if omitted. |
| `-i` | Interactive mode: flat list, only elements with refs. |
| `-c` | Compact mode: remove empty structural elements. |
| `--raw` | Raw view: use UIA RawViewCondition (includes hidden MSAA elements). |
| `-d <n>` | Limit tree depth to n levels. |
| `--json` | Machine-readable JSON output. |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {
    "snapshot": "- document \"Notepad\" [ref=e1]\n- button \"OK\" [ref=e2, okButton] clickable",
    "refs": {
      "e1": {"role": "document", "name": "Notepad"},
      "e2": {"role": "button", "name": "OK"}
    }
  },
  "error": null
}
```

### windows

List all visible windows with `wN` references.

```
seeless-uia windows [--verbose]
```

| Flag | Description |
|------|-------------|
| `--verbose` | Show HWND and PID columns. |

Output:

```
Windows:
  Ref    Process              Title
  ------------------------------------------------------------
-> w1    Notepad              *Untitled - Notepad
   w2    Calculator           计算器
```

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {
    "windows": [
      {"refId": "w1", "hwnd": 592628, "processId": 12345, "processName": "Notepad", "title": "*Untitled - Notepad"},
      {"refId": "w2", "hwnd": 330588, "processId": 67890, "processName": "CalculatorApp", "title": "计算器"}
    ],
    "active": "w1"
  },
  "error": null
}
```

### window

Switch the active window context.

```
seeless-uia window wN
```

After switching, subsequent commands target wN without needing explicit window refs.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"active": "w2"},
  "error": null
}
```

### app launch

Launch an application and register its window.

```
seeless-uia app launch <name>
seeless-uia launch <name>
```

| Name | Application |
|------|-------------|
| `notepad` / `notepad.exe` | Windows Notepad |
| `calc` / `calculator` / `calc.exe` | Windows Calculator (UWP, PID fallback to CalculatorApp) |
| `cmd` / `cmd.exe` | Command Prompt |
| `explorer` / `explorer.exe` | File Explorer |
| `code` / `vscode` | Visual Studio Code (finds via PATH) |
| `opencode` | OpenCode (finds via PATH) |
| `<path>` | Any executable path |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {
    "refId": "w3",
    "processId": 12345,
    "windowTitle": "Calculator"
  },
  "error": null
}
```

### daemon

Start the daemon server manually (auto-starts on first command).

```
seeless-uia daemon [--port <port>]
```

### close

Close the active window or a specific window.

```
seeless-uia close [wN]
```

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"message": "Shutting down"},
  "error": null
}
```

### ping

Check daemon health.

```
seeless-uia ping
```

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"pong": true, "uptime": 42},
  "error": null
}
```

---

## Interaction Commands

### click

Click an element by ref or selector.

```
seeless-uia click [wN] <sel> [--button left|right|middle] [--click-count 1|2]
```

| Flag | Description |
|------|-------------|
| `--button` | Mouse button: left (default), right, middle. |
| `--click-count` | Number of clicks: 1 (default) or 2. |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"clicked": "e5", "button": "left", "clickCount": 1},
  "error": null
}
```

### dblclick

Double-click. Alias for `click --click-count 2`.

```
seeless-uia dblclick [wN] <sel>
```

### fill

Clear and fill an input element.

```
seeless-uia fill [wN] <sel> <text>
```

Pattern path uses ValuePattern.SetValue (atomic replace). SendInput fallback uses Ctrl+A + Delete + keystrokes.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"filled": "e3", "value": "hello@example.com"},
  "error": null
}
```

### type

Type text into an element.

```
seeless-uia type [wN] <sel> <text> [--delay <ms>]
```

| Flag | Description |
|------|-------------|
| `--delay <ms>` | Milliseconds between keystrokes (default 0). |

Sends character-by-character keystrokes. For control characters (\n, \t), dispatches key press events.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"typed": "e3", "text": "hello"},
  "error": null
}
```

### press

Press a single key or key chord.

```
seeless-uia press <key>
```

| Key | Examples |
|-----|----------|
| Named keys | `Enter`, `Tab`, `Escape`, `Backspace`, `Delete`, `Space` |
| Arrow keys | `Up`, `Down`, `Left`, `Right` |
| Navigation | `Home`, `End`, `PageUp`, `PageDown` |
| Function keys | `F1` through `F12` |
| Modifiers | `Control`, `Alt`, `Shift`, `Win` |
| Single char | `a`, `Z`, `5` |
| Chords | `Control+a`, `Shift+Enter`, `Control+Shift+a` |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"pressed": "Control+a"},
  "error": null
}
```

### hover

Move the mouse to an element's center.

```
seeless-uia hover [wN] <sel>
```

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"hovered": "e5"},
  "error": null
}
```

### scroll

Scroll the active window.

```
seeless-uia scroll <dir> [--amount <px>] [--selector <sel>]
```

| Direction | Meaning |
|-----------|---------|
| `up` | Scroll up |
| `down` | Scroll down (default) |
| `left` | Scroll left |
| `right` | Scroll right |

`--amount` defaults to 300 pixels.

### scrollintoview

Scroll an element into view.

```
seeless-uia scrollintoview [wN] <sel>
seeless-uia scroll-into-view [wN] <sel>
```

Uses ScrollItemPattern. Falls back to SetFocus if unavailable.

### check / uncheck

Check or uncheck a toggle element (checkbox, radio button, switch).

```
seeless-uia check [wN] <sel>
seeless-uia uncheck [wN] <sel>
```

State-aware: reads current ToggleState, only toggles if needed, verifies after toggle, retries with SendInput click on failure.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"checked_target": "e5"},
  "error": null
}
```

### focus

Set keyboard focus on an element.

```
seeless-uia focus [wN] <sel>
```

### expand / collapse

Expand or collapse an element (dropdown, tree node, accordion).

```
seeless-uia expand [wN] <sel>
seeless-uia collapse [wN] <sel>
```

### select

Select a list item or grid cell.

```
seeless-uia select [wN] <sel>
```

### drag

Drag from one element to another.

```
seeless-uia drag <src> <tgt>
```

Uses 10-step interpolated SendInput mouse events. Both source and target must be refs.

### screenshot

Capture a screenshot of the active window.

```
seeless-uia screenshot [path] [wN] [--full] [--json]
```

| Flag | Description |
|------|-------------|
| `--full` | Full scrollable content (scroll-and-stitch via ScrollPattern). |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"screenshot": "<base64-png>", "format": "png"},
  "error": null
}
```

---

## Get Commands

### get text

Read visible text from an element.

```
seeless-uia get text <sel> [wN]
```

Reads from TextPattern.GetText(-1), then ValuePattern.Value, then Name.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"text": "Notepad"},
  "error": null
}
```

### get value

Read the current value of an input control.

```
seeless-uia get value <sel> [wN]
```

Reads from ValuePattern.Value.

### get box

Get an element's bounding rectangle.

```
seeless-uia get box <sel> [wN]
```

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"x": 10.0, "y": 20.0, "width": 300.0, "height": 200.0},
  "error": null
}
```

### get count

Count elements matching a selector.

```
seeless-uia get count <sel> [wN]
```

The selector can be a property selector (e.g. `control:Button`, `class:MyButton`).

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"count": 33},
  "error": null
}
```

### get attr

Read a UIA attribute from an element.

```
seeless-uia get attr <sel> <attr> [wN]
```

| Attribute | Description |
|-----------|-------------|
| `name` | Accessible name |
| `automationid` | AutomationId property |
| `classname` | ClassName property |
| `frameworkid` | FrameworkId (e.g. "WPF", "Win32") |
| `controltype` | ProgrammaticName of ControlType (e.g. "ControlType.Button") |
| `isenabled` | IsEnabled property |
| `isoffscreen` | IsOffscreen property |
| `processid` | Process ID |
| `nativewindowhandle` | Native window handle |

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"value": "num5Button"},
  "error": null
}
```

---

## Is Commands

### is visible

Check if an element is visible.

```
seeless-uia is visible <sel> [wN]
```

Returns `true` if the element is not offscreen and has a non-zero bounding rectangle.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"visible": true},
  "error": null
}
```

### is enabled

Check if an element is enabled.

```
seeless-uia is enabled <sel> [wN]
```

### is checked

Check the toggle state of an element.

```
seeless-uia is checked <sel> [wN]
```

Returns `true` if ToggleState is On. Returns `false` if Off, Indeterminate, or TogglePattern unavailable.

---

## Wait & Find

### wait <sel>

Wait for an element to appear in the accessibility tree.

```
seeless-uia wait <sel> [wN] [--timeout <ms>]
```

| Flag | Description |
|------|-------------|
| `--timeout <ms>` | Maximum wait time in milliseconds (default 30000). |

Polls every 100ms using TreeWalker. Returns when the element is found or the timeout expires.

`--json` output schema:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"appeared": true},
  "error": null
}
```

### wait <ms>

Wait for a fixed duration.

```
seeless-uia wait <ms>
```

Example: `seeless-uia wait 2000` waits 2 seconds.

### wait --text

Wait for specific text to appear in the window's accessibility tree.

```
seeless-uia wait --text <text> [--timeout <ms>]
```

Polls the full UIA tree every 100ms, matching against element Name and ValuePattern.Value with case-insensitive substring matching.

### find

Find an element by semantic criteria and execute an action on it.

```
seeless-uia find role <role> <action> [<value>]   [--name <filter>]
seeless-uia find text <text> <action> [<value>]
seeless-uia find label <label> <action> [<value>]
seeless-uia find placeholder <text> <action> [<value>]
```

| Locator | Searches |
|---------|----------|
| `role` | ControlType match, then optionally filtered by --name. |
| `text` | Full tree scan by Name.Contains (case-insensitive). |
| `label` | Matches label text, then follows LabeledBy or finds sibling input. |
| `placeholder` | Searches HelpText, AutomationId, and Name on Edit/ComboBox elements. |

| Action | Description |
|--------|-------------|
| `click` | Click the found element. |
| `fill <val>` | Fill the found element with value. |
| `type <val>` | Type value into the found element. |
| `hover` | Hover the found element. |
| `focus` | Focus the found element. |
| `check` | Check the found element. |
| `uncheck` | Uncheck the found element. |
| `text` | Read text from the found element. |

`--name` (role only): Filter by accessible name. Case-insensitive substring match.

---

## Raw Input Commands

### keydown / keyup

Press or release a key.

```
seeless-uia keydown <key>
seeless-uia keyup <key>
```

Keys use the same names as `press` (Enter, Control, Shift, a, F1, etc.). Typically used as a pair for modifier+key combinations:

```bash
seeless-uia keydown Control && seeless-uia press a && seeless-uia keyup Control   # Ctrl+A
```

### keyboard type

Type text at the current keyboard focus without targeting a specific element.

```
seeless-uia keyboard type <text> [--delay <ms>]
```

### mouse

Raw mouse control.

```
seeless-uia mouse move <x> <y>
seeless-uia mouse down [left|right|middle]
seeless-uia mouse up [left|right|middle]
seeless-uia mouse wheel <dy>
```

Coordinates are absolute screen coordinates.

---

## Clipboard Commands

### clipboard read / write

```
seeless-uia clipboard read                          # Read text from clipboard
seeless-uia clipboard write <text>                  # Write text to clipboard
```

`--json` output for read:

```json
{
  "id": "abc123",
  "success": true,
  "data": {"text": "clipboard contents"},
  "error": null
}
```

### clipboard copy / paste

```
seeless-uia clipboard copy                          # Send Ctrl+C
seeless-uia clipboard paste                         # Send Ctrl+V
```

## Global Options

| Flag | Applies to | Description |
|------|-----------|-------------|
| `--json` | All commands | Machine-readable JSON output. |
| `--port <port>` | All commands | Daemon TCP port (default 9222). |
| `--timeout <ms>` | wait, find | Operation timeout in milliseconds. |
| `--pid <pid>` | snapshot, interaction | Target process ID (prefer wN refs). |
| `--hwnd <hex>` | snapshot, interaction | Target window handle (prefer wN refs). |
