# Interaction Model: UIA Patterns vs SendInput

SeelessUIA uses a dual-path interaction model: tries UIA Patterns first,
falls back to Win32 SendInput.

## Pattern path (preferred)

UIA Patterns are the native way to interact with controls:

| Pattern | Used for | Commands |
|---------|---------|----------|
| InvokePattern | Button clicks, menu items | `click`, `dblclick` |
| ValuePattern | Input fields | `fill`, `get value` |
| TogglePattern | Checkboxes, radios | `check`, `uncheck`, `is checked` |
| SelectionItemPattern | List items, tabs | `select` |
| ExpandCollapsePattern | Dropdowns, trees | `expand`, `collapse` |
| ScrollPattern | Scrollable regions | `scroll`, `scroll_amount` |
| ScrollItemPattern | Scrolling items into view | `scrollintoview` |
| TextPattern | Rich text reading | `get text` |
| SetFocus | Keyboard focus | `focus` |

## SendInput fallback

When a UIA Pattern is unavailable, SeelessUIA falls back to Win32 SendInput:

| Action | SendInput equivalent |
|--------|---------------------|
| Click | mouse_move + left_down + left_up |
| Fill | focus + Ctrl+A + Delete + keystrokes |
| Type | Character-by-character keystrokes |
| Hover | mouse_move only |
| Press | key_down + key_up pair |
| Drag | 10-step interpolated mouse drag |
| Scroll | Mouse wheel events |

## Framework differences

| Framework | UIA quality | Notes |
|-----------|-----------|-------|
| WPF | Excellent | Full pattern support, reliable ControlTypes |
| WinForms | Good | Some controls may expose limited patterns |
| Qt | Good | ClassNames prefixed with "class " |
| Electron (Type A) | Good | Apps with accessibility enabled expose full tree |
| Electron (Type B) | Poor | Only window frame visible — content is opaque |
| UWP | Good | Calculator, Settings work well |
| Win32 native | Variable | Tree walk can be slow (>1s for complex dialogs) |
