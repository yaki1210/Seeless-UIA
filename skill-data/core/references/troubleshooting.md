# Troubleshooting

Common problems and their solutions when using SeelessUIA.

## Daemon Issues

### "Starting daemon... OK" but commands hang

The daemon auto-start succeeded but interaction commands return no response. This happens when the daemon process from a previous session is still running on the default port (9222). Kill existing daemon processes:

```powershell
taskkill /F /IM SeelessUIA.exe
```

Then retry the command.

### "Daemon not running"

If the auto-start fails (port conflict, permissions), start the daemon manually on a different port:

```bash
seeless-uia daemon --port 9230
```

Then pass `--port 9230` on all subsequent commands. Or set `SEELESSUIA_PORT=9230` as an environment variable.

### Port already in use

Another application is using port 9222. Use a different port as described above. Common conflicts: Chrome DevTools Protocol, other automation tools.

## Ref Issues

### "Ref e5 not found"

The ref is from a previous snapshot and the window state has changed. Causes:
- Dialog opened or closed after the snapshot
- Tab switched in a TabControl
- Element was removed or recreated by the application

**Solution**: Take a fresh snapshot and use the new refs.

### Click on wrong element

Two or more elements have the same role and name in the snapshot (e.g., two "Submit" buttons). SeelessUIA assigns different refs to each. Ensure you are using the correct ref from the snapshot output.

If using `find` without a snapshot, `find` returns the first matching element. Use `find role ... --name ...` with more specific name filters to target the right element.

### Element has no ref in snapshot

Interactive mode (`-i`) only shows elements that receive refs. Structural containers (panes, groups, toolbars) do not get refs. If an element you need is missing:
- Use structured mode (no `-i`) to see the full tree
- Use `get count control:Button` to check if the element exists
- Use `find` to locate it without a ref

## Interaction Issues

### click has no effect

1. **Element not visible**: Try `seeless-uia scrollintoview @e5` first.
2. **Element covered**: Another window is on top. Use `seeless-uia window wN` to bring the target window to the foreground.
3. **InvokePattern not supported**: The fallback sends mouse events. Ensure the mouse position is correct via `get box @e5`.
4. **Application is busy**: Add a `wait 500` before clicking.

### fill doesn't work

Some native Win32 controls do not support UIA ValuePattern. The SendInput fallback (Ctrl+A + Delete + keystrokes) will be used automatically. If this also fails:

1. Try `seeless-uia focus @e3` first
2. Then `seeless-uia type @e3 "text"` instead of fill
3. Or use keyboard simulation: `seeless-uia keyboard type "text"`

### press chord doesn't work

`Control+a` and similar chords work by pressing modifiers, typing the key, then releasing modifiers. Some applications require the modifiers to be held for the full duration. If chords fail:

```bash
seeless-uia keydown Control
seeless-uia press a
seeless-uia keyup Control
```

### Drag fails

Drag requires both elements to be visible on screen. If either element is offscreen, use `scrollintoview` first, then drag.

## Find Issues

### find role returns nothing

Role names are case-insensitive. Verify the correct role name: `Button` not `button`, `Edit` not `Textbox`. Supported roles: Button, Edit, Text, CheckBox, RadioButton, ComboBox, ListItem, MenuItem, Tab, TreeItem, Slider, Image, Header, Hyperlink, List, Table, Menu.

### find text doesn't match

`find text` searches element Name (accessible name), not AutomationId or ClassName. If the target element has an empty Name, it won't be found. Check the snapshot output to see element names.

### find label returns no labeled element

UWP and some WinForms applications do not fully implement UIA LabeledBy relationships. `find label` requires proper label-to-input associations in the application's accessibility tree.

### Screenshot returns black or error on UWP apps

Windows Store apps (Calculator, Settings, etc.) may not render via GDI `PrintWindow`. This is a Win32/UWP boundary limitation — UWP apps use a different rendering pipeline. SeelessUIA automatically attempts to restore and foreground the window, but if the error persists:

- Use `snapshot -i` + `get text` / `get value` to inspect the window semantically — no screenshot needed
- For visual verification, use a separate screen-capture tool or `Windows.Graphics.Capture` API

## Performance Issues

### Snapshot takes >5 seconds

Win32 native controls (ToolbarWindow32, SysTabControl32, ListView) produce deep UIA trees that take several seconds to build. This is a fundamental COM IPC limitation — each element requires a cross-process call. Electron and Chromium apps snapshot in ~40ms.

**Workarounds**:
- Use interactive mode (`-i`) to reduce output size
- Limit depth with `-d 3` or `-d 5`
- Target specific windows, not desktop root

### Every command takes >1 second

Each daemon command involves a TCP round-trip + UIA operation. First-command latency includes daemon auto-start (~2-5 seconds). Subsequent commands are faster (~100ms for get/is, ~110ms for click).

## Window Context Issues

### "No window found"

If `snapshot` without a wN ref returns empty, the active window context may have been lost (window closed, daemon restarted). Run `seeless-uia windows` to re-discover windows and `seeless-uia window wN` to re-select.

### wN refs change after restart

wN assignments are persisted in `%LOCALAPPDATA%\SeelessUIA\windows.json`. If this file is deleted or corrupted, new wN assignments start from w1. Avoid deleting this file manually.
