# SeelessUIA

[中文](README_zh.md)

Windows UI Automation CLI for AI agents. Exposes desktop applications through Microsoft UI Automation (UIA) as structured accessibility trees — with stable element refs and native interaction commands.

Uses the snapshot + ref model: take a snapshot to discover elements, then interact by ref. Designed for AI agent workflows where context tokens are expensive and structured data beats pixel inference.

## What this is

- A **UIA-first automation layer** for Windows desktop apps. Best for Win32, WPF, WinForms, and UWP applications that expose meaningful accessibility trees.
- Provides **stable refs** (`e1`, `e2`, ...) that agents can click, fill, expand, and verify.
- Returns **structured state**: role, name, value, checked, expanded, selected, automationId, bounding rect.
- Runs as a lightweight CLI + persistent TCP daemon (auto-start), suitable for agent pipelining.

## What this is not

- **Not a universal visual desktop agent.** It does not "see" the screen — it reads UIA trees. Apps that don't expose UIA (canvas renders, custom frameworks, some Electron apps) will return empty or incomplete snapshots.
- **Not a replacement for agent-browser** in browser scenarios. If you're automating a web page inside a browser, use agent-browser / CDP directly.
- **Not a screenshot-to-action tool.** Screenshot is available but intended for visual verification by a local VLM model — not as the primary discovery mechanism.

## Quick Start

```bash
# Install
npm install -g seeless-uia

# Requires: Windows, .NET 10 Runtime
```

```bash
# Explore Calculator
seeless-uia app launch calc
seeless-uia wait 2000
seeless-uia snapshot -i          # Get interactive elements with refs
seeless-uia click e28            # Click "5"
seeless-uia get text control:Text  # Read the display

# Explore VS Code
seeless-uia app launch code
seeless-uia wait 2000
seeless-uia snapshot -i          # Tabs, buttons, tree items, status bar
seeless-uia find text "Explorer" click
```

## The Core Loop

```
1. windows          Discover windows → w1, w2, w3
2. snapshot w1 -i    Take snapshot → e1, e2, e3...
3. click e2          Interact by ref
4. snapshot -i --diff  Verify changes (added/removed/modified)
```

Refs are valid until the window state changes. After dialogs, tab switches, or expansions, take a fresh snapshot.

**Token-saving tips:**
- Verify text with `get value`, `find text "X" text`, or `wait --text "X"` — no snapshot needed
- `snapshot -i --diff` gives only changed elements (but costs the same as a full snapshot internally)
- Prefer plain text output over `--json` for AI consumption — fewer chars, no escape overhead

## Core Commands

| Category | Commands |
|----------|----------|
| **Discovery** | `windows`, `window wN`, `app launch <name>`, `ping`, `close [wN]` |
| **Snapshot** | `snapshot [wN] [-i] [-c] [-d <n>] [--diff] [--json]` |
| **Interaction** | `click`, `dblclick`, `fill`, `type`, `press`, `hover`, `scroll`, `check`, `uncheck`, `focus`, `expand`, `collapse`, `select` |
| **Read** | `get text`, `get value`, `get box`, `get count`, `get attr` |
| **Check** | `is visible`, `is enabled`, `is checked` |
| **Find** | `find role/text/label/placeholder <criteria> <action>` |
| **Wait** | `wait <sel>`, `wait <ms>`, `wait --text <text>` |
| **Raw** | `keyboard type`, `keydown/up`, `mouse move/down/up/wheel`, `clipboard read/write/copy/paste`, `screenshot` |

Full command reference in the [skill docs](skill-data/core/SKILL.md).

## Compatibility

SeelessUIA depends on applications exposing UIA accessibility trees. Coverage varies by UI framework:

| Framework | Support | Notes |
|-----------|---------|-------|
| **UWP** | Good | Settings, Calculator — full tree access |
| **WPF** | Good | Standard controls well-exposed |
| **Win32** | Partial | Standard controls work; system/elevated processes may return empty trees |
| **Electron** | Partial | File trees, tabs, buttons exposed; headings/ARIA/input areas often missing |
| **Custom renderers** | Poor | Canvas, custom-drawn controls rarely expose UIA |

See [docs/compatibility.md](docs/compatibility.md) for tested applications. Contributions welcome.

## Snapshot Output

```
- document "project - Visual Studio Code" [ref=e1] scrollable
  - tablist [actions-container]
    - tab "Explorer (Ctrl+Shift+E)" [ref=e2] selectable
    - tab "Search (Ctrl+Shift+F)" [ref=e3] selectable
  - tree "Explorer" [ref=e48] clickable
    - treeitem "src" [ref=e49] selectable
      - generic "E:\\project\\src" [ref=e50] clickable
```

Each line carries: indented tree depth, role, name, attributes `[ref, state, hints]`, interactivity kind `(clickable, selectable, editable...)`.

## Installation from Source

```bash
git clone https://github.com/yaki1210/Seeless-UIA
cd Seeless-UIA
dotnet build SeelessUIA.slnx -c Release
# Run directly:
src\bin\Release\net10.0-windows\SeelessUIA.exe windows
```

**Requirements:** Windows, .NET 10 SDK with Windows Desktop workload.

## Documentation

- [Skill docs for AI agents](skill-data/core/SKILL.md) — complete command reference, pipeline internals, troubleshooting
- [Application compatibility](docs/compatibility.md) — tested apps and known limitations
- [Commands reference](skill-data/core/references/commands.md) — full flag + JSON schema reference

## Architecture

CLI → TCP daemon (auto-start, port 9222) → UIA COM API. The daemon holds one RefMap + WindowRegistry across commands. Snapshot runs a 5-stage pipeline: Build → Clean → Detect Interactivity → Assign Refs → Render.

## License

MIT
