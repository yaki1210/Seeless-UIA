# SeelessUIA

Windows UI Automation CLI for AI agents. Fast native .NET CLI.

## Installation

### Global Installation (recommended)

Installs the native .NET binary:

```bash
npm install -g seeless-uia
```

### From Source

```bash
git clone https://github.com/yaki1210/Seeless-UIA
cd Seeless-UIA
pnpm install
pnpm build:native    # dotnet build src/SeelessUIA.slnx -c Release
```

### Requirements

- **Windows 10+** — UI Automation requires Windows Runtime
- **.NET 10 SDK** — Only needed when building from source

## Quick Start

```bash
# List windows
seeless-uia windows

# Launch Calculator and interact
seeless-uia app launch calc
seeless-uia snapshot w1 -i
seeless-uia click @e28           # click "5" button
seeless-uia click @e21           # click "+"
seeless-uia click @e26           # click "3"
seeless-uia click @e22           # click "="
seeless-uia get text control:Text
seeless-uia close
```

The daemon auto-starts on the first interaction command and persists
between commands. Use `seeless-uia close` when done.

## Commands

### Core Commands

```bash
seeless-uia snapshot [wN] [-i] [-c] [-r] [--raw] [-d <n>]
seeless-uia windows [--verbose]
seeless-uia window <wN>
seeless-uia app launch <name>
seeless-uia daemon [--port <port>]
seeless-uia close
seeless-uia ping
```

### Interaction

```bash
seeless-uia click <sel> [--button left|right|middle] [--click-count 1|2]
seeless-uia dblclick <sel>
seeless-uia fill <sel> <text>
seeless-uia type <sel> <text> [--delay <ms>]
seeless-uia press <key>                    # "Enter", "Control+a"
seeless-uia hover <sel>
seeless-uia scroll <dir> [--amount <px>]
seeless-uia scroll_amount <sel>
seeless-uia check <sel>
seeless-uia uncheck <sel>
seeless-uia focus <sel>
seeless-uia expand <sel>
seeless-uia collapse <sel>
seeless-uia select <sel>
seeless-uia scrollintoview <sel>
seeless-uia drag <src> <tgt>
seeless-uia screenshot [path]             # --full for scrollable content
```

### Get Info

```bash
seeless-uia get text <sel>
seeless-uia get value <sel>
seeless-uia get box <sel>
seeless-uia get count <sel>
seeless-uia get attr <sel> <name>         # name, automationid, classname, ...
```

### Check State

```bash
seeless-uia is visible <sel>
seeless-uia is enabled <sel>
seeless-uia is checked <sel>
```

### Find Elements (Semantic Locators)

```bash
seeless-uia find role <role> <action> [--name <name>]
seeless-uia find text <text> <action>
seeless-uia find label <label> <action>
seeless-uia find placeholder <ph> <action>

# Actions: click, fill, type, hover, focus, check, uncheck, text
```

### Wait

```bash
seeless-uia wait <sel> [--timeout <ms>]
seeless-uia wait --text <text> [--timeout <ms>]
```

### Raw Input

```bash
seeless-uia keydown <key>
seeless-uia keyup <key>
seeless-uia keyboard type <text> [--delay <ms>]
seeless-uia mouse move <x> <y>
seeless-uia mouse down [button]           # left/right/middle
seeless-uia mouse up [button]
seeless-uia mouse wheel <dy>
```

### Clipboard

```bash
seeless-uia clipboard read
seeless-uia clipboard write <text>
seeless-uia clipboard copy
seeless-uia clipboard paste
```

## Snapshot Options

The `snapshot` command supports filtering to reduce output size:

```bash
seeless-uia snapshot w1                  # Full UIA tree
seeless-uia snapshot w1 -i               # Interactive elements only (buttons, inputs, links)
seeless-uia snapshot w1 -i -c            # Compact (remove empty structural elements)
seeless-uia snapshot w1 -i -d 3          # Limit depth to 3 levels
seeless-uia snapshot w1 -i --json        # Machine-readable JSON output
```

## Options

| Option | Description |
|--------|-------------|
| `--json` | Machine-readable JSON output for all commands |
| `--port <port>` | Connect to daemon on specific port |
| `--timeout <ms>` | Request timeout (default 30s) |
| `--full` | Full scrollable content (screenshot) |
| `--verbose` | Show additional columns (windows command) |

## Architecture

SeelessUIA uses a client-daemon architecture:

1. **CLI** (SeelessUIA.exe) — Parses commands, communicates with daemon
2. **Daemon** — TCP/NDJSON server on 127.0.0.1:9222, handles UIA tree walks and actions

The daemon auto-starts on the first interaction command and persists between
commands for fast subsequent operations.

## Usage with AI Agents

Add the skill to your AI coding assistant:

```bash
npx skills add yaki1210/Seeless-UIA
```

Or use directly with any AI agent:

```
Use seeless-uia to automate Calculator. Run seeless-uia help to see available commands.
```

The core workflow: `windows` → `snapshot -i` → `click @eN` → `snapshot -i`.

### Skills

```bash
seeless-uia skills list                  # List available skills
seeless-uia skills get core              # Get core skill content
seeless-uia skills get core --full       # Include reference docs and templates
```

## Contrast with agent-browser

| | agent-browser | SeelessUIA |
|---|-------------|-----------|
| Target | Web pages (Chrome/Chromium) | Native Windows apps (UIA) |
| Technology | CDP (Chrome DevTools Protocol) | UI Automation (.NET) |
| Platform | Linux, macOS, Windows | Windows only |
| Tree source | Accessibility.getFullAXTree (CDP) | TreeWalker + CacheRequest (UIA) |
| Element resolution | backend_node_id → DOM.getBoxModel | RuntimeId → BoundingRectangle |
| Interaction | CDP mouse/key events | UIA Patterns + Win32 SendInput |
| Page model | URLs, tabs, navigation | Windows, window refs (w1/w2) |
| Performance | ~50ms snapshot | ~40ms (Electron/Qt), ~500ms (Win32) |
