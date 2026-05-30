# Interaction Model: UIA Patterns vs SendInput

SeelessUIA uses a dual-path approach for element interaction. Each command tries a UIA Pattern first and falls back to Win32 SendInput if the pattern is unavailable.

## Pattern-First Path (Preferred)

UIA Patterns are the native accessibility API for automating controls. When a control supports a pattern, it accepts direct commands without mouse/keyboard simulation.

| Pattern | Used By | What It Does |
|---------|---------|--------------|
| InvokePattern | `click` | Calls the element's default action (like clicking a button). |
| ValuePattern | `fill`, `get value` | Sets or reads the element's value (textbox content, slider position). |
| TogglePattern | `check`/`uncheck`, `is checked` | Reads or sets the toggle state (checkbox on/off, radio selected). |
| ExpandCollapsePattern | `expand`/`collapse` | Opens or closes expandable elements (dropdowns, tree nodes). |
| SelectionItemPattern | `select` | Selects an item in a list, grid, or tab control. |
| ScrollPattern | `scroll` | Scrolls a scrollable container. |
| TextPattern | `get text` | Reads rich text content with formatting and selection ranges. |
| TransformPattern | (unused) | Could resize or move elements. |

**Advantages of Patterns:**
- Atomic operations (no race conditions with animation)
- Works even when the element is partially hidden or offscreen
- No side effects from mouse movement (focus stays where it was)

## SendInput Fallback (Win32)

When a control does not support the required UIA Pattern, SeelessUIA falls back to Win32 `SendInput` API to simulate real mouse and keyboard events.

| Fallback | Used By | What It Does |
|----------|---------|--------------|
| Mouse move + click | `click`, `dblclick`, `drag` | Moves cursor to element center and sends mouse events. |
| Ctrl+A + Delete + keystrokes | `fill` | Selects all text, deletes it, then types the new value. |
| Character keystrokes | `type`, `press` | Sends per-character keyboard events with VkKeyScan. |
| Mouse hover | `hover` | Moves cursor to element center without clicking. |
| Mouse wheel | `mouse wheel` | Scrolls via wheel delta. |

**Disadvantages of SendInput:**
- Requires the element to be visible and on-screen
- Can interfere with the user if the mouse is being used
- Slower (per-event latency ~10ms vs atomic pattern call)
- Modifier key handling is complex (Shift for uppercase, Ctrl for chords)

## Which Commands Use Which Path

| Command | Pattern Path | SendInput Fallback |
|---------|-------------|-------------------|
| `click` | InvokePattern.Invoke() | Mouse move + left button down/up |
| `fill` | ValuePattern.SetValue() | Ctrl+A + Delete + keystrokes |
| `type` | *(none)* | Per-character keystrokes via VkKeyScan |
| `press` | *(none)* | Named key down/up or keystroke |
| `hover` | *(try ExpandCollapsePattern.Expand)* | Mouse move to element center |
| `scroll` | ScrollPattern.SetScrollPercent() | *(none — no mouse wheel fallback for percent)* |
| `check`/`uncheck` | TogglePattern.Toggle() | Mouse click at element center |
| `focus` | SetFocus() (direct UIA method) | *(no fallback)* |
| `expand`/`collapse` | ExpandCollapsePattern | *(no fallback)* |
| `select` | SelectionItemPattern.Select() | Mouse click at element center |
| `drag` | *(none)* | 10-step interpolated mouse move + down/up |
| `keydown`/`keyup` | *(none)* | Direct Win32 keyboard input |

## Pattern Availability Detection

During snapshot, the TreeBuilder caches 7 boolean flags per element:

```
HasInvokePattern, HasTogglePattern, HasValuePattern,
HasSelectionItemPattern, HasExpandCollapsePattern,
HasScrollPattern, HasTextPattern
```

Structural elements (pane, group, list, window) skip pattern queries — they never need them.

For `get`/`is` commands, pattern availability is checked on-demand via `GetCurrentPattern()` (single COM call per check).

## Try Order

For commands with dual paths, the order is always:

1. Try UIA Pattern
2. If pattern unavailable or throws, silently fall through
3. Execute SendInput fallback

There is no retry loop between paths. If the pattern path fails mid-operation (not at availability check), the exception propagates — no SendInput retry for mid-operation failures.

## Editable Elements

`fill` and `type` only work on editable controls (role=`textbox`, `edit`, `combobox`). Attempting to fill/type on a read-only `text` label will silently do nothing. Always check the element's role with `snapshot -i` before interacting:

- `textbox` / `edit` — accepts `fill` and `type`
- `text` — read-only label; cannot accept input
- `button` — use `click`; `fill`/`type` have no effect
- `checkbox` — use `check`/`uncheck`
- `listitem` / `treeitem` — use `select`

## Scrolling

The `scroll` command uses pixel-based mouse wheel simulation on the target element. Direction must be the first positional argument:

```
seeless-uia scroll up [--amount 300]
seeless-uia scroll down [--amount 300]
seeless-uia scroll left [--amount 300]
seeless-uia scroll right [--amount 300]
```

The `scroll_amount` command uses UIA native `ScrollPattern.Scroll(VerticalScrollAmount.LargeIncrement)`. This only works on standard Win32 controls that expose ScrollPattern. Use `scroll` for general pixel-based wheel scrolling.


