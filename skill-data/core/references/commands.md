# SeelessUIA Command Reference

## Core commands

| Command | Description |
|---------|-------------|
| `snapshot [wN] [-i] [-c] [-r] [--raw] [-d <n>]` | Take a snapshot of the active window's UIA tree |
| `windows [--verbose]` | List all visible windows with wN refs |
| `window <wN>` | Switch the active window to wN |
| `app launch <name>` | Launch an application and register its window |
| `daemon [--port <port>]` | Start the daemon server manually |
| `close` | Close the active window |
| `ping` | Check if the daemon is alive |

## Interaction commands

| Command | Description |
|---------|-------------|
| `click <sel> [--button left/right/middle] [--click-count 1/2]` | Click element by ref or selector |
| `dblclick <sel>` | Double-click element |
| `fill <sel> <text>` | Clear and fill an input field |
| `type <sel> <text> [--delay <ms>]` | Type text character by character |
| `press <key>` | Press a key or key chord ("Control+a") |
| `hover <sel>` | Move mouse to element center |
| `scroll <dir> [--amount <px>]` | Scroll by direction (up/down/left/right) |
| `scroll_amount <sel>` | Native ScrollAmount scroll |
| `check <sel>` | Check a checkbox (state-aware) |
| `uncheck <sel>` | Uncheck a checkbox |
| `focus <sel>` | Set keyboard focus |
| `expand <sel>` | Expand a dropdown/tree |
| `collapse <sel>` | Collapse |
| `select <sel>` | Select list/tab item |
| `scrollintoview <sel>` | Scroll element into view |
| `drag <src> <tgt>` | Drag from source to target |
| `screenshot [path] [--full]` | Take window screenshot |

## Get commands

| Command | Description |
|---------|-------------|
| `get text <sel>` | Read element text (TextPattern → ValuePattern → Name) |
| `get value <sel>` | Read input value |
| `get box <sel>` | Get bounding rectangle |
| `get count <sel>` | Count matching elements |
| `get attr <sel> <name>` | Read UIA property |

**Attributes**: `name`, `automationid`, `classname`, `frameworkid`, `controltype`, `isenabled`, `isoffscreen`, `processid`, `nativewindowhandle`

## Is commands

| Command | Description |
|---------|-------------|
| `is visible <sel>` | Check if element is visible |
| `is enabled <sel>` | Check if element is enabled |
| `is checked <sel>` | Check toggle state |

## Wait commands

| Command | Description |
|---------|-------------|
| `wait <sel> [--timeout <ms>]` | Wait for element to appear |
| `wait --text <text> [--timeout <ms>]` | Wait for text to appear anywhere |

## Find commands

| Command | Description |
|---------|-------------|
| `find role <role> <action> [--name <name>]` | Find element by UIA role |
| `find text <text> <action>` | Find element by text content |
| `find label <label> <action>` | Find by label text |
| `find placeholder <ph> <action>` | Find by placeholder/help text |

## Raw input

| Command | Description |
|---------|-------------|
| `keydown <key>` | Hold a key down |
| `keyup <key>` | Release a key |
| `keyboard type <text> [--delay <ms>]` | Type without selector |
| `mouse move <x> <y>` | Move mouse to absolute screen position |
| `mouse down [left/right/middle]` | Press mouse button |
| `mouse up [left/right/middle]` | Release mouse button |
| `mouse wheel <dy>` | Scroll by mouse wheel delta |

## Clipboard

| Command | Description |
|---------|-------------|
| `clipboard read` | Read clipboard text |
| `clipboard write <text>` | Write to clipboard |
| `clipboard copy` | Copy (Ctrl+C) |
| `clipboard paste` | Paste (Ctrl+V) |

## Global options

| Option | Description |
|--------|-------------|
| `--json` | Machine-readable JSON output |
| `--port <port>` | Connect to daemon on specific port |
| `--timeout <ms>` | Request timeout (default 30s) |
