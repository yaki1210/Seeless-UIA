# UIAgent — AI Agent Reference

Windows UI Automation snapshot and interaction CLI for AI agents. Provides browser-automation-compatible output format (`{indent}- {role} "{name}" [attrs] {kind} [{hints}] : {value}`) for desktop applications via UIA.

## Architecture

```
CLI (Program.cs)
  → SnapshotPipeline (orchestrator: tree → clean → detect → refs → render)
  → DaemonServer (TCP/NDJSON, identical protocol to agent-browser)
```

### Execution modes

- `UiaAgent snapshot --pid <pid> [-i] [--raw]` — single snapshot to stdout
- `UiaAgent daemon --port 9222` — persistent TCP server, NDJSON protocol
- `UiaAgent test --app notepad` — launch app, snapshot, kill
- `UiaAgent windows` — list windows with `[n]` indices for `--pid <pid>:<n>`

### Snapshot Pipeline (SnapshotPipeline.cs:15)

| Stage | File | COM calls | Description |
|-------|------|-----------|-------------|
| 1. Build tree | `TreeBuilder.cs` | 1 (CacheRequest.Subtree) | Walk UIA tree, create UiaNode list |
| 2. Clean | `TreeCleaner.cs` | 0 | Dedup, filter, collapse, relabel |
| 3. Detect interactivity | `SnapshotPipeline.cs:34` | 0 | Pattern → kind/hints mapping |
| 4. Assign refs | `SnapshotPipeline.cs:112` | 0 | e1, e2, ... + RoleNameTracker |
| 5. Render | `TreeRenderer.cs` | 0 | Indented text output |

---

## Data Structures

### UiaNode (UiaNode.cs)

Internal IR node, maps to agent-browser's TreeNode:

```
Field           Type          Source (UIA property)
──────────────────────────────────────────────────
RuntimeId       int[]         GetCachedPropertyValue(RuntimeIdProperty)
AutomationId    string        automationId (stripped of -cl-NNNN)
Role            string        ControlType mapped via RoleMapping
Name            string        Name → MSAA accName → MSAA accDesc → MSAA accHelp → HelpText
ClassName       string        ClassName (stripped of "class " prefix)
FrameworkId     string        e.g. "Win32", "Qt", "WPF"
HelpText        string        HelpTextProperty
Description     string        MSAA accDescription (30094)
Checked         string        "true"/"false"/"mixed" (ToggleState from cache)
Expanded        bool?         ExpandCollapseState from cache
Selected        bool?         IsSelected from cache
Value           string        ValuePattern.Value from cache
HasInvokePattern bool        IsInvokePatternAvailable from cache
HasTogglePattern bool        ... (7 pattern flags, all from cache, 0 COM)
CursorKind      string        "clickable"/"editable"/"toggleable"/"selectable"/"scrollable"/"focusable"/"expandable"
CursorHints     List<string>  state hints only (e.g. ["on"], ["collapsed"]), no redundant pattern names
HasRef          bool          assigned a ref ID
RefId           string        "e1", "e2", ...
Children        List<int>     indices into nodes list
Depth           int           tree depth
BoundingRect    Rect?         geometry
```

### RefMap (Element/RefMap.cs)

Maps ref IDs to metadata for element resolution:

```
RefMap._map: Dictionary<string, RefEntry>
RefMap._nextRef: int (reset to 1 on Clear())

RefEntry {
    RuntimeId, Role, Name, Nth, AutomationId
}
```

### RoleMapping (Snapshot/RoleMapping.cs)

ControlType → role string lookup, plus sets:

```
InteractiveRoles:  button, textbox, link, checkbox, radio, combobox,
                   listitem, menuitem, tab, treeitem, slider, spinbutton
ContentRoles:      heading, cell, text, image, progressbar
StructuralRoles:   generic, group, list, table, menu, toolbar,
                   statusbar, pane, window, custom, document
TransparentControlTypes: Pane, Group, DataGrid, Table
SkipControlTypes:  Window
SkipChildrenControlTypes: TitleBar, MenuBar, ToolTip
```

---

## Cleaning Pipeline (TreeCleaner.cs)

Each stage mutates `List<UiaNode>` in place. Order matters:

### 1. DeduplicateChildren
Per-parent: removes duplicate siblings with same (ControlType, Name, AutomationId, ClassName) key. Clears duplicate nodes.

### 2. FilterBasic
Clears: offscreen nodes, zero-size anonymous nodes (keeps if has Name or AutomationId).

### 3. CollapseContainers
Clears: Pane/Group/Table/DataGrid containers if:
- Single child + child is non-structural → promote child
- Empty name + no patterns + no interactive children → clear

### 4. CollapseGenericChains
Iterates to convergence: if parent and its only child are both unnamed generic, promote grandchildren up, clear child.

### 5. AggregateConsecutiveText
Merges consecutive Text/Image siblings into the first node's name.

### 6. DeduplicateName
If parent has exactly one Text child with same name → clear child.

### 7. RelabelContainersByClass
Rewrites role for generic/custom containers based on ClassName (case-insensitive substring). FrameworkId-gated for WPF-specific exact matches.

**Framework-agnostic (substring):**
```
Contains "MenuBar" → "menu"
Contains "ToolBar" / "Toolbar" → "toolbar"
Contains "StatusBar" / "Statusbar" → "statusbar"
Contains "TabBar" / "Tabbar" / "TabWidget" → "tablist"
Contains "SysTabControl32" → "tablist"
```

**WPF-specific (exact match, FrameworkId="WPF"):**
```
"Menu" → "menu"
"ContextMenu" → "menu"
"TabControl" → "tablist"
```

**WinForms:** MenuStrip/ToolStrip/StatusStrip use `WindowsForms10.Window.8.app.0.xxx` (no ClassName signal). These are caught by the heuristic (step 8) when children have correct ControlTypes.

### 8. RelabelContainersHeuristically
Rewrites role based on child distribution:
```
All children are menuitem → "menu"
All children are tab → "tablist"
All children are listitem → "list"
2+ buttons, no inputs, all children are button/separator/image/generic → "toolbar"
```

---

## Interactivity Detection (SnapshotPipeline.cs:52)

Pattern → kind mapping (no hints for obvious cases):

| Condition | Kind | Hints |
|-----------|------|-------|
| HasInvokePattern | clickable | [] |
| HasTogglePattern | toggleable | [on/off/mixed] |
| HasValuePattern + (textbox/combobox) | editable | [] |
| HasSelectionItemPattern | selectable | [] |
| HasExpandCollapsePattern | expandable | [expanded/collapsed] |
| HasScrollPattern | scrollable | [] |
| IsKeyboardFocusable | focusable | [] |

---

## Ref Assignment (SnapshotPipeline.cs:112)

Nodes get a ref ID if:
1. `RoleMapping.IsInteractive(role)` — button, textbox, link, checkbox, radio, etc.
2. `RoleMapping.IsContent(role)` AND has non-empty Name — heading, cell, text, image
3. Has non-empty CursorKind — any interactive pattern detected

Skip: cleared nodes, Window-type, TitleBar, MenuBar, ToolTip.

### RoleNameTracker (Element/RoleNameTracker.cs)

Tracks (role:name) occurrences to disambiguate duplicates. Stores nth (0-based) in RefEntry for element resolution re-query.

---

## Renderer (TreeRenderer.cs)

Output format per line:
```
{indent*2}- {role} "{name}" [ref=eN, automationId, class] {kind} [{hints}] : {value}
```

Skip rules during recursive walk:
1. Cleared nodes → recurse children at same indent
2. Chrome elements (TitleBar, MenuBar, ToolTip) → skip entirely
3. Window → skip line, recurse children
4. Interactive mode + !hasRef → skip line, recurse children
5. Structured mode + structural + no ref descendants → skip line, recurse children
6. Depth cutoff
7. **Cross-parent dedup**: `_rendered` HashSet tracks already-output indices

### ClassName display
Only shown for containers whose role was rewritten by `RelabelContainersByClass` or `RelabelContainersHeuristically` (roles: menu, toolbar, statusbar, tablist). The `class ` prefix is stripped from Qt class names.

### AutomationId display
- Stripped of `-cl-NNNN` noise and random alphanumeric IDs via regex
- Skipped if value is "RootWebArea"

### Compact mode
Post-processing: keeps only lines with `ref=` or `: ` and their ancestors.

---

## Element Resolution (ElementResolver.cs)

Dual-path positioning from ref ID or property selector:

### Ref-based (fast path)
1. `ParseRef("@e42" \| "ref=e42" \| "e42")` → ref ID
2. `RefMap.Get(refId)` → RefEntry
3. `FindFirst(TreeScope.Descendants, PropertyCondition(RuntimeIdProperty))` → element
4. `element.Current.BoundingRectangle` → center coordinates

### Ref-based (fallback, RuntimeId stale)
1. `RoleToControlType(role)` → ControlType
2. `FindAll(TreeScope.Descendants, PropertyCondition(ControlTypeProperty))` → candidates
3. Match by Name, select nth occurrence → element
4. BoundingRectangle → center

### Selector-based
`"automationId:xxx" \| "name:xxx" \| "class:xxx" \| "control:xxx"` → `FindFirst` with PropertyCondition.

---

## Interaction (Interaction/)

### PatternActions.cs
Uses UIA Patterns (preferred, reliable):
- `InvokePattern.Invoke()` — click
- `ValuePattern.SetValue()` — fill
- `TogglePattern.Toggle()` — check/uncheck
- `SelectionItemPattern.Select()` — select list item
- `ExpandCollapsePattern.Expand()/Collapse()` — expand/collapse
- `ScrollPattern.SetScrollPercent()` — scroll

### SendInputActions.cs
Win32 `SendInput` fallback (when patterns unavailable):
- Mouse: `MOUSEEVENTF_MOVE \| LEFTDOWN \| LEFTUP`
- Keyboard: `KEYBDINPUT` with VkKeyScan
- Press: `keybd_event` for named keys (Enter, Tab, Escape, etc.)

### ActionExecutor.cs
Pattern-then-SendInput dispatcher. Holds `ElementResolver` for coordinate resolution.

---

## CLI Flags

```
snapshot --pid <pid>[:<n>] [-i] [-r] [--all] [--raw] [--depth <n>] [--hwnd <hwnd>]
  -i      Interactive mode (flat, ref-only)
  -r      Include refs list in stderr
  --all   Snapshot all windows of the process
  --raw   Use UIA RawViewCondition (unfiltered tree)
  --hwnd  Snapshot a specific window by handle (e.g. 0x12C)

test --app <name> [-i] [-r] [--raw] [--depth <n>]
  Apps: notepad, calc, cmd, code, opencode
  -i      Interactive mode
  -r      Include refs list
  --raw   Raw view
  --depth Limit tree depth

calc-test
  Launch Calculator, click 5+3=, verify result is 8.
  In-process closed-loop interaction test.

windows
  Table: PID [n] Process HWND Title (grouped by PID) with 0-based window index

daemon [--port <port>]
  TCP NDJSON server on 127.0.0.1:<port>
```

---

## Daemon Protocol (NDJSON)

Request:
```json
{"action":"snapshot","id":"1","processId":12345,"interactive":false}
{"action":"click","id":"2","ref":"e5"}
{"action":"fill","id":"3","ref":"e7","value":"hello"}
```

Response:
```json
{"id":"1","success":true,"data":{"snapshot":"- ...","refs":{"e1":{"role":"button","name":"OK"}}}}
```

Supported actions: `snapshot`, `click`, `fill`, `type`, `hover`, `scroll`, `check`, `uncheck`, `focus`, `press`, `expand`, `collapse`, `select`, `scroll_into_view`, `window_list`, `window_focus`, `window_close`, `app_launch`, `screenshot`, `close`.

**New interaction features (v0.2):**

| Action | Parameters | Notes |
|--------|-----------|-------|
| `click` | `ref`, `button` ("left"/"right"/"middle"), `clickCount` (1/2) | Supports right-click and double-click |
| `scroll` | `ref`, `x`/`y` (pixel deltas) or `direction` ("up"/"down"/"left"/"right") + `amount` (default 300) | Direction semantics or pixel scroll |
| `press` | `key` ("Control+a", "Shift+Enter", "Escape") | Modifier chord parsing |
| `check` / `uncheck` | `ref` | State-aware: reads current toggle state, only toggles if needed, verifies after toggle, retries with click fallback |
| `fill` | `ref`, `value` | SendInput path: Ctrl+A clear before typing. Pattern path: SetValue replaces content directly. |
| `expand` / `collapse` | `ref` | ExpandCollapsePattern (new daemon dispatch) |
| `select` | `ref` | SelectionItemPattern (new daemon dispatch) |
| `scroll_into_view` | `ref` | ScrollItemPattern (new daemon dispatch) |

### CLI Commands (matching agent-browser format)

All interaction commands require a daemon (`UiaAgent daemon`). Snapshot and windows are standalone.

```
UiaAgent snapshot --pid <pid> [-i] [-c] [-r] [--all] [--raw] [-d <n>]
UiaAgent click <sel> [--pid <pid>] [--button left|right|middle] [--click-count 1|2]
UiaAgent dblclick <sel> [--pid <pid>]
UiaAgent fill <sel> <text> [--pid <pid>]
UiaAgent type <sel> <text> [--pid <pid>] [--delay <ms>]
UiaAgent press <key> [--pid <pid>]       # "Enter", "Control+a", "Shift+Enter"
UiaAgent hover <sel> [--pid <pid>]
UiaAgent scroll <dir> [--pid <pid>] [--amount <px>]
UiaAgent check/uncheck <sel> [--pid <pid>]
UiaAgent focus <sel> [--pid <pid>]
UiaAgent expand/collapse <sel> [--pid <pid>]
UiaAgent select <sel> [--pid <pid>]
UiaAgent scrollintoview <sel> [--pid <pid>]
UiaAgent screenshot [path] [--pid <pid>]
UiaAgent close [--pid <pid>]
UiaAgent windows
UiaAgent daemon [--port <port>]
```

### Unimplemented Features (vs agent-browser)

**Element queries** (reading properties without a full snapshot):
| Command | Status |
|---------|--------|
| `get text <sel>` | **Implemented** |
| `get value <sel>` | **Implemented** |
| `get box <sel>` | **Implemented** |
| `get attr <sel> <attr>` | Not implemented |
| `get count <sel>` | **Implemented** |

**State checks:**
| Command | Status |
|---------|--------|
| `is visible <sel>` | **Implemented** |
| `is enabled <sel>` | **Implemented** |
| `is checked <sel>` | **Implemented** |

**Advanced interaction:**
| Command | Status |
|---------|--------|
| `drag <src> <tgt>` | Not implemented (DragPattern unused) |
| `highlight <sel>` | Not implemented |
| `keyboard type/inserttext <text>` | Not implemented |
| `keydown/keyup <key>` | KeyDown/KeyUp exist in SendInput, no CLI dispatch |
| `mouse move/down/up/wheel` | MouseWheel added, no CLI dispatch |
| `upload <sel> <files>` | N/A (native apps) |
| `eval <js>` | N/A (no browser) |

**Locators and waiting:**
| Command | Status |
|---------|--------|
| `find role/text/label <sel>` | Not implemented |
| `wait <sel>/<ms>/--text/--url` | **`wait <sel>` and `wait <ms>` implemented**. Text/url not implemented. |

**Output:**
| Feature | Status |
|---------|--------|
| `--json` flag | **Implemented** — all commands support machine-readable JSON output |
**Advanced interaction:**
| Command | Status |
|---------|--------|
| `drag <src> <tgt>` | Not implemented (DragPattern unused) |
| `highlight <sel>` | Not implemented |
| `keyboard type/inserttext <text>` | Not implemented |
| `keydown/keyup <key>` | KeyDown/KeyUp exist in SendInput, no CLI dispatch |
| `mouse move/down/up/wheel` | MouseWheel added, no CLI dispatch |
| `upload <sel> <files>` | N/A (native apps) |
| `eval <js>` | N/A (no browser) |

**Locators and waiting:**
| Command | Status |
|---------|--------|
| `find role/text/label <sel>` | Not implemented |
| `wait <sel>/<ms>/--text/--url` | Not implemented |

**Daemon protocol gaps:**
- No request timeout
- No connection authentication
- No health check / ping
- No protocol version handshake
- Response streaming (not needed for native UIA)

---

## Performance

- **Tree traversal**: single `CacheRequest(TreeScope.Subtree)` call batches all properties. Subsequent tree walk reads from cache (0 COM).
- **Pattern detection**: 7 boolean flags read from cache. State details (ToggleState, ExpandCollapseState, etc.) also from cache (0 extra COM).
- **Structural skip**: nodes with structural roles (pane/group/list/window) skip all pattern queries.
- **Timing**: `[perf] build:Xms clean:Yms detect:Zms refs:Wms render:Vms nodes:N` on stderr.
- **Typical**: Telegram (55 nodes) build:37ms, Chrome/Electron (80 nodes) build:35ms, Win32 (500 nodes) build:4800ms.

---

## Test Commands

```powershell
dotnet test                                          # 49 unit tests
python tests/test_snapshot.py --pid <pid> -i         # integration test, saves to output/
python tests/test_snapshot.py --pid <pid> -i -r      # with refs
```
