# Application Compatibility

SeelessUIA depends on applications exposing meaningful Microsoft UI Automation (UIA) trees. Coverage varies by UI framework and how the application implements accessibility.

> **Last updated:** 2026-06-04. This is a living document — contributions welcome.

## Framework Summary

| Framework | Typical Support | Pattern |
|-----------|----------------|---------|
| **UWP** (Windows Settings, Calculator) | Good | Full tree access. Standard controls well-exposed. Screenshots work. |
| **WPF** | Good | Standard controls well-exposed. AutomationId often present. |
| **Win32** (Notepad, File Explorer, Task Manager) | Partial | Standard controls work. System/elevated processes may return empty trees due to integrity-level blocking. |
| **Electron** (VS Code, Codex, Cursor) | Partial | File trees, tabs, buttons exposed. Headings and ARIA roles not exposed. `contenteditable` input areas often invisible to UIA. |
| **Custom renderers** (LibreOffice, Java Swing, canvas apps) | Poor | Custom-drawn controls rarely implement UIA providers. Returns empty or near-empty trees. |

## Tested Applications

### Good

Applications where snapshots capture a meaningful tree and basic interaction works.

| App | Framework | Snapshot | Interaction | Notes |
|-----|-----------|----------|-------------|-------|
| **Calculator** | UWP | Full tree (~30 refs) | Buttons clickable, display readable | Screenshot works. Classic Win32 calculator also works. |
| **Windows Settings** | UWP | Full tree (~74 refs) | Buttons and toggle switches work; navigation listitems require SelectionItem fallback | Screenshot works. Ref ephemeral across CLI invocations. |

### Partial

Applications where snapshots work but interaction is limited or some UIA data is missing.

| App | Framework | Snapshot | Interaction | Notes |
|-----|-----------|----------|-------------|-------|
| **VS Code** | Electron | File tree, tabs, buttons, statusbar all exposed (~117 refs interactive) | Buttons, tabs, treeitems clickable | Headings not detected (Electron doesn't expose AriaProperties/AriaRole). Menu bar invisible. contenteditable input areas not accessible. |
| **File Explorer** | Win32 | Full tree (~238 refs interactive) | Buttons with InvokePattern work. Treeitems and listitems lack InvokePattern — need SelectionItem/ExpandCollapse fallback. | Excellent snapshot coverage. Interaction is the weak point. |
| **Notepad** | Win32 | Menu bar, title bar, scrollbar visible | Menu items and buttons clickable. Main text area (Edit control) read via UIA is unreliable. | Standard Win32 text edit control doesn't expose editable text through UIA TextPattern. |

### Poor

Applications where UIA returns empty or near-empty trees.

| App | Framework | Snapshot | Interaction | Notes |
|-----|-----------|----------|-------------|-------|
| **Task Manager** | Win32 (High IL) | Empty tree (0 refs) | Nothing works | Runs at High integrity level. SeelessUIA (Medium IL) blocked by Windows security policy. Workaround: run SeelessUIA as Administrator. |
| **Codex** | Electron | Basic shell visible | Minimal via UIA refs. Keyboard input and shortcuts work on the window directly (e.g. `keyboard type`, `clipboard paste`). | Input area (Monaco editor contenteditable div) not exposed to UIA. Window-level keyboard input bypass works. |
| **Cursor** | Electron | Basic shell visible | Same as Codex — input via window-level keyboard shortcuts. | Input area invisible to UIA. |
| **LibreOffice** | Custom | Empty tree | Nothing works | Custom-rendered UI does not implement UIA providers. |

## How to Test an Application

```bash
# 1. Basic snapshot — does it see anything?
seeless-uia snapshot <app-wN> -i

# 2. Raw tree — what does UIA actually expose?
seeless-uia snapshot <app-wN> --no-clean

# 3. Find + interact — can we click things?
seeless-uia find role Button click <app-wN>
seeless-uia find role TreeItem click <app-wN>

# 4. Read content
seeless-uia get text control:Text <app-wN>
seeless-uia find text "example" text <app-wN>
```

## Contributing

Test an app and report your findings:
- Open a [compatibility report](https://github.com/yaki1210/Seeless-UIA/issues/new?template=compatibility_report.md)
- Include: app name + version, framework (if known), snapshot mode flags used, what worked and what didn't
- If something returns `(empty page)`, try `--no-clean` first — this shows the raw unfiltered tree
