# Window Management

## Window refs (w1, w2, ...)

SeelessUIA assigns stable references to top-level windows. These are
analogous to `t1`/`t2` tab refs in agent-browser.

## Viewing windows

```bash
seeless-uia windows
# Output:
#   Ref    Process              Title
#   ------------------------------------------------------------
# -> w1   Notepad              *Untitled - Notepad
#    w2   Calculator            Calculator
#    w3   OpenCode              OpenCode
```

The `->` marker shows the active window (current interaction target).

Use `--verbose` to see HWND and PID columns.

## Switching windows

```bash
seeless-uia window w2           # Switch to w2 as active window
seeless-uia snapshot -i         # Snapshots the active window (now w2)
```

After switching, all interaction commands target the new active window
without needing to specify wN on each command.

## Launching apps

```bash
seeless-uia app launch calc     # Launch Calculator → returns wN ref
seeless-uia app launch notepad  # Launch Notepad
```

Supported apps: `calc`, `notepad`, `cmd`, `code`, `opencode`

## Window ref stability

- wN refs are based on HWND (window handle), not position or PID
- Closing a window retires its wN permanently — it will never be reassigned
- Reopening a window gets a fresh wN (the old number stays retired)
- Window refs persist across daemon restarts (`windows.json`)

## Implicit window context

Interaction commands use the active window by default:

```bash
seeless-uia windows             # w1 = Notepad, w2 = Calculator
seeless-uia click @e5           # Clicks @e5 on w1 (active window)
seeless-uia click w2 @e3        # Explicit: clicks @e3 on w2
```
