# Snapshot + Ref Model

SeelessUIA uses the same snapshot + ref model as agent-browser, adapted for Windows native applications.

## How It Works

1. **Snapshot** scans the UIA accessibility tree of a window and assigns stable reference IDs (`e1`, `e2`, `e3`, ...) to interactive elements.

2. **Refs** are stored in a RefMap alongside the element's RuntimeId, role, name, and nth occurrence index.

3. **Interaction** resolves refs back to AutomationElements via cached RuntimeId (fast path) or TreeWalker re-query (fallback).

## Ref Assignment Rules

A node gets a ref if any of these are true:
- It has a UIA Pattern that makes it interactive (Invoke, Toggle, Value, SelectionItem, ExpandCollapse, Scroll)
- It belongs to a **content role** (heading, cell, listitem) AND has a non-empty name
- It is keyboard-focusable

Structural container roles (generic, group, list, pane, toolbar, menu) do not receive refs unless they hold interactive children.

## RefMap Storage

Each ref maps to a `RefEntry`:

```json
{
  "e1": {"role": "button", "name": "OK", "nth": 0, "runtimeId": [42, 123456]},
  "e2": {"role": "button", "name": "Cancel", "nth": 0, "runtimeId": [42, 789012]}
}
```

If two elements have the same role and name (e.g., two "Submit" buttons), the `nth` field disambiguates: first occurrence gets `nth=0`, second gets `nth=1`.

## Element Resolution (Dual Path)

When an interaction command uses a ref (`click @e2`):

**Path A (Fast)** — RuntimeId Lookup:
- Reads the cached RuntimeId from RefMap
- Finds the element via `FindFirst(TreeScope.Descendants, PropertyCondition(RuntimeIdProperty, ...))`
- This is the 99% path — works as long as the window hasn't changed.

**Path B (Fallback)** — TreeWalker Re-query:
- If Path A fails (RuntimeId stale, element recreated), falls back to TreeWalker
- Searches by ControlType (from role), Name, and nth occurrence
- Slower but recovers from window state changes.

## Ref Lifetime

Refs are valid **until the window state changes**. Invalidating events:
- Opening/closing a dialog
- Switching tabs in a TabControl
- Expanding/collapsing sections that add/remove elements
- Closing and re-opening the window

After any of these, take a fresh snapshot and use the new refs.

## Deduplication

Multiple nodes with the same role and name are deduplicated by their nth occurrence index. When the snapshot assigns refs, identical elements get different refs:

```
- button "Submit" [ref=e4]
- button "Submit" [ref=e7]    (different element, same name)
```

The nth index ensures that `click @e4` clicks the first Submit and `click @e7` clicks the second.

## AutomationId in Snapshots

Snapshots include the element's `automationId` as a bare value in brackets:

```
- button "OK" [ref=e3, okButton] clickable
```

The `okButton` is the AutomationId. It can be used to find elements by their AutomationId via `find` or by inspecting the snapshot output.

## Snapshot Performance

| App Type | Typical Nodes | Build Time |
|----------|-------------|-----------|
| Electron/Chromium | ~180 | ~40ms |
| Qt (Telegram) | ~55 | ~37ms |
| Win32 WinForms | ~500 | ~4800ms |

Win32 COM IPC is the main bottleneck. The tree walk itself is sub-30ms; the COM round-trips for Win32 controls dominate.
