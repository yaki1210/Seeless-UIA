# Troubleshooting

## Common issues

### "Ref 'e1' not found in RefMap"

**Cause**: No snapshot has been taken yet, or the ref map was cleared.

**Fix**: Run `seeless-uia snapshot w1 -i` first to populate the ref map.

### "Element not found: e5"

**Cause**: The UI changed since the last snapshot. The element may have been
removed, recreated, or moved in the tree.

**Fix**: Re-run `seeless-uia snapshot w1 -i` to get fresh refs.

### "No window found"

**Cause**: The target window was closed, or the daemon was restarted.

**Fix**: Run `seeless-uia windows` to refresh the window list. The window
may have a different wN if it was closed and reopened.

### "Daemon not running"

**Cause**: The daemon process crashed or was killed.

**Fix**: Interaction commands auto-start the daemon. Wait 2 seconds and
retry. If it persists, run `seeless-uia daemon` manually.

### Click does nothing

**Cause**: The element may not support InvokePattern (the UIA click
mechanism). Some custom controls require coordinate-based clicking.

**Fix**: Try `seeless-uia hover @eN` first, then `seeless-uia click @eN`.
Hover ensures the mouse is positioned before clicking.

### Snapshot is slow (>1 second)

**Cause**: Win32 native controls (SysListView32, SysTreeView32) generate
hundreds of UIA nodes. Each node requires a COM IPC call to Windows.

**Fix**: Use `-i` (interactive mode) to only show actionable elements.
Use `-d <n>` to limit tree depth. Consider targeting the specific window
rather than the desktop root.

### Chinese text is garbled

**Cause**: The console doesn't support UTF-8 output.

**Fix**: Use PowerShell with `chcp 65001` or Windows Terminal. The daemon
outputs UTF-8 natively.

### Snapshot shows only window frame

**Cause**: The app (typically Electron Type B) doesn't expose its internal
UI to Windows Accessibility.

**Fix**: No UIA-based fix available. For Electron apps, consider using
CDP (Chrome DevTools Protocol) to access the internal Chromium tree.

### Window refs shift after `windows`

**Cause**: Previous versions reassigned wN on every `windows` call. This
is fixed in v0.2+ — wN refs are now stable across snapshots.

**Fix**: Update to the latest version.
