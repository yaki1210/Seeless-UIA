# Snapshot + Ref Model

SeelessUIA uses the same snapshot-and-ref model as agent-browser, adapted
for native Windows UIA trees.

## How it works

1. `seeless-uia snapshot w1 -i` walks the UIA tree, finds interactive
   elements, assigns each a ref ID (`@e1`, `@e2`, ...), and returns the
   formatted tree.

2. The ref IDs map to element metadata (role, name, UIA RuntimeId, nth
   occurrence) stored in a RefMap on the daemon.

3. Interaction commands (`click @e3`, `fill @e3 "text"`, `get text @e3`)
   resolve the ref to the actual UIA element and perform the action.

## Ref lifecycle

- Refs are assigned fresh on every snapshot
- Refs are **cleared** when you take a new snapshot
- Refs become **stale** when the UI changes (new window, dialog opens, tab
  switches)
- Always re-run `snapshot` after any interaction that changes the UI

## Dual-path element resolution

When you use a ref (`@e3`), the system tries two paths:

1. **Fast path**: Uses the cached UIA RuntimeId from the snapshot to
   directly find the element. Works 99% of the time.

2. **Fallback path**: If RuntimeId is stale (element destroyed and
   recreated), searches the UIA tree by matching role + name + nth
   occurrence.

## Snapshot options

| Option | Effect |
|--------|--------|
| `-i` | Interactive mode: only show elements with refs |
| `-c` | Compact: remove empty structural containers |
| `-r` | Include refs list in stderr output |
| `-d <n>` | Limit tree depth |
| `--raw` | Use UIA RawView (includes hidden MSAA elements) |
| `--json` | JSON output with snapshot text + refs dictionary |

## Output format

```
{indent}- {role} "{name}" [ref=eN, automationId] {kind} [{hints}] : {value}
```

- `kind`: `clickable`, `editable`, `toggleable`, `selectable`, `expandable`,
  `scrollable`, `focusable`
- `hints`: `[on]`, `[off]`, `[collapsed]`, `[expanded]`, `[disabled]`
- `automationId`: UIA AutomationId (bare, no `automationId=` prefix)

## Window refs (w1, w2, ...)

Window refs are separate from element refs. They identify top-level windows
and are stable across sessions:

- `seeless-uia windows` lists all visible windows with w1, w2, ...
- `seeless-uia window w2` switches the active window
- Window refs persist across daemon restarts (stored in `windows.json`)
- Window refs are never reused — closed windows retire their wN permanently
