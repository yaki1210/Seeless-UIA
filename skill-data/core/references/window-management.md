# Window Management: wN Reference System

SeelessUIA assigns stable window references (`w1`, `w2`, `w3`, ...) to visible top-level windows. These refs work exactly like element refs (`@e1`) but for windows instead of UI elements.

## Discovery

```bash
seeless-uia windows
```

Output:
```
Windows:
  Ref    Process              Title
  ------------------------------------------------------------
-> w1    OpenCode             OpenCode
   w2    Notepad              *Untitled - Notepad
   w3    Calculator           计算器
```

The `->` marker indicates the active window.

## Ref Assignment and Persistence

- Each visible window gets a unique wN ref based on its HWND (window handle).
- `wN` numbers are **never reused**. When a window closes, its wN is permanently retired.
- New windows get fresh, monotonically increasing numbers (w4, w5, w6, ...).
- The wN registry persists across daemon restarts via `%LOCALAPPDATA%\SeelessUIA\windows.json`.

**Stability**: The same window keeps the same wN across multiple `windows` calls, even if other windows open or close. Only the HWND matters for matching — position in the list is irrelevant.

## Active Window Context

When you snapshot or interact with a window, that window becomes the **active window**. All subsequent commands implicitly target it:

```bash
seeless-uia snapshot w2 -i        # Sets w2 as active
seeless-uia click @e3             # Implicitly targets w2
seeless-uia snapshot -i           # Implicitly snapshots w2
seeless-uia get text @e1         # Implicitly reads from w2
```

To change the active window:

```bash
seeless-uia window w3             # Switch to w3
seeless-uia snapshot -i           # Now targets w3
```

## Explicit Window Override

Any interaction command accepts an optional `[wN]` prefix to override the active window for a single command:

```bash
seeless-uia click w2 @e5          # Click e5 in w2 (even if w3 is active)
seeless-uia get text w1 @e1       # Read from w1
```

## Launch and Register

```bash
seeless-uia app launch notepad
  -> w4    (new window, auto-registered)
```

`app launch` starts the application, finds its UIA window, assigns a new wN, and sets it as active.

Supported applications:
- `notepad` / `notepad.exe`
- `calc` / `calculator` / `calc.exe` (UWP — uses PID fallback to CalculatorApp)
- `cmd` / `cmd.exe`
- `explorer` / `explorer.exe`
- `code` / `vscode`
- `opencode`

## window close

Close a window from the CLI:

```bash
seeless-uia close w2              # Close specific window
seeless-uia close                 # Close active window
```

## Multi-Window Workflow

```bash
seeless-uia app launch notepad    # wN assigned and activated
seeless-uia snapshot -i           # Snapshot Notepad
...                               # Interact with Notepad
seeless-uia windows               # Discover other windows
seeless-uia window w3             # Switch to Calculator
seeless-uia snapshot -i           # Snapshot Calculator (w3 now active)
...                               # Interact with Calculator
seeless-uia window wN             # Back to Notepad
```

## Technical Notes

- Window enumeration uses Win32 `EnumWindows` + UIA `FindWindowByProcessId`.
- UWP apps (Calculator on Windows 10/11) use `calc.exe` as a launcher. The actual window belongs to `CalculatorApp.exe`. SeelessUIA handles this PID fallback automatically.
- Window refs are stable across `windows` calls thanks to HWND-based matching in `WindowRegistry.RefreshAll()`.
- The `.windows.json` registry file includes wN assignments, window metadata, and the active window ref. It survives daemon restarts.
