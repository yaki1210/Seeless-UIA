---
name: core
description: Core SeelessUIA usage guide. Read this before running any SeelessUIA commands. Covers the snapshot-and-ref workflow for native Windows apps, interacting with elements (click, fill, type, press), extracting text and data, taking screenshots, managing windows, handling forms, waiting for content, and troubleshooting common failures. Use when the user asks to interact with a Windows app, click a button, fill a form, extract data, take a screenshot, or automate any native app task.
allowed-tools: Bash(seeless-uia:*), Bash(npx seeless-uia:*)
---

# SeelessUIA core

Windows UI Automation CLI for AI agents. Direct UIA access via .NET, no
browser dependency. Accessibility-tree snapshots with compact `@eN` refs
let agents interact with native apps in ~200 tokens instead of parsing
raw UIA trees.

## The core loop

```bash
seeless-uia windows              # 1. See available windows (get wN refs)
seeless-uia snapshot w1 -i       # 2. Snapshot interactive elements
seeless-uia click @e3            # 3. Act on refs from the snapshot
seeless-uia snapshot w1 -i       # 4. Re-snapshot after any UI change
```

Refs (`@e1`, `@e2`, ...) are assigned fresh on every snapshot. They become
**stale the moment the UI changes** — after clicks that open dialogs, form
submissions, tab switches, window changes. Always re-snapshot before your
next ref interaction.

Window refs (`w1`, `w2`, ...) are stable across sessions and snapshots.
They persist across daemon restarts and never change for the same window.

## Quickstart

```bash
# Install once
npm i -g seeless-uia

# Launch an app and interact
seeless-uia app launch calc        # Launch Calculator → returns wN ref
seeless-uia snapshot w1 -i         # Snapshot interactive elements
seeless-uia click @e28             # Click the "5" button
seeless-uia click @e21             # Click "+"
seeless-uia click @e26             # Click "3"
seeless-uia click @e22             # Click "="
seeless-uia get text control:Text  # Read the result display
seeless-uia close                  # Close the window
```

The daemon auto-starts on the first interaction command and stays alive
across commands. Use `seeless-uia close` when you're done.

## Reading a window

```bash
seeless-uia snapshot w1                   # full tree
seeless-uia snapshot w1 -i                # interactive elements only (preferred)
seeless-uia snapshot w1 -i -c             # compact (no empty structural nodes)
seeless-uia snapshot w1 -i -d 3           # cap depth at 3 levels
seeless-uia snapshot w1 -i --json         # machine-readable output
```

Snapshot output looks like:

```
- document "Calculator" [ref=e1] scrollable
  - button "5" [ref=e28, num5Button] clickable
  - button "+" [ref=e21, plusButton] clickable
  - textbox "" [ref=e91] editable
```

## Interacting

```bash
# Core interactions (use refs from snapshot)
seeless-uia click @e28                    # Click by ref
seeless-uia dblclick @e5                  # Double-click
seeless-uia fill @e91 "hello"            # Clear and fill input
seeless-uia type @e91 "slow text" --delay 50  # Type character-by-character
seeless-uia press Enter                   # Press a key
seeless-uia press Control+a               # Key chord with modifiers

# State changes
seeless-uia check @e81                    # Check a checkbox (state-aware)
seeless-uia uncheck @e81                  # Uncheck
seeless-uia focus @e91                    # Set keyboard focus
seeless-uia hover @e5                     # Move mouse to element
seeless-uia select @e100                  # Select a list/tab item
seeless-uia scroll down --amount 300      # Scroll by direction
seeless-uia scroll_amount @e40            # Native ScrollAmount scroll
seeless-uia expand @e5                    # Expand a dropdown/tree
seeless-uia collapse @e5                  # Collapse
seeless-uia scrollintoview @e91           # Scroll element into view
seeless-uia drag @e28 @e26                # Drag from one element to another
seeless-uia screenshot [path]             # Take window screenshot
seeless-uia screenshot --full [path]      # Full scrollable content screenshot
```

## Getting information

```bash
seeless-uia get text @e3                  # Read element text
seeless-uia get value @e91                # Read input value
seeless-uia get box @e28                  # Get bounding rectangle
seeless-uia get count control:Button      # Count matching elements
seeless-uia get attr @e1 name             # Read UIA property (name, automationid, classname, etc.)
```

## Checking state

```bash
seeless-uia is visible @e1                # Check if element is visible
seeless-uia is enabled @e28               # Check if element is enabled
seeless-uia is checked @e81               # Check toggle state
```

## Waiting

```bash
seeless-uia wait @e91                     # Wait for element to appear (30s default)
seeless-uia wait @e91 --timeout 5000      # Wait with custom timeout
seeless-uia wait --text "Welcome"         # Wait for text to appear anywhere
```

Raw input: `keydown`, `keyup`, `keyboard type`, `mouse move/down/up/wheel`
don't need a snapshot — they operate on current focus and screen position.

## Window management

```bash
seeless-uia windows                       # List all visible windows (w1, w2, ...)
seeless-uia window w2                     # Switch active window to w2
seeless-uia app launch notepad            # Launch app and register its window
seeless-uia close                         # Close the active window
```

## Find elements (semantic locators)

```bash
seeless-uia find role Button click --name "Submit"   # Find button by role+name
seeless-uia find text "Welcome" text                 # Find element containing text
seeless-uia find label "Email" fill "test@test.com"  # Find by label text
seeless-uia find placeholder "Search" click          # Find by placeholder/help text
```

## Clipboard

```bash
seeless-uia clipboard read                # Read clipboard text
seeless-uia clipboard write "test"        # Write to clipboard
seeless-uia clipboard copy                # Send Ctrl+C
seeless-uia clipboard paste               # Send Ctrl+V
```

## JSON output

All commands support `--json` for machine-readable output:

```bash
seeless-uia snapshot w1 -i --json
# {"success":true,"data":{"snapshot":"- button...","refs":{"e1":{...}}}}

seeless-uia get text @e3 --json
# {"success":true,"data":{"text":"Hello World"}}
```

## Troubleshooting

| Problem | Likely cause | Fix |
|---------|-------------|-----|
| "Element not found: e5" | UI changed since last snapshot | Re-run `snapshot` to get fresh refs |
| "Ref 'e1' not found in RefMap" | No snapshot taken yet | Run `snapshot w1 -i` first |
| "No window found" | Window closed or daemon restarted | Run `windows` to refresh window list |
| "Daemon not running" | Daemon crashed | Commands auto-start it; wait 2s and retry |
| Click does nothing | Element not interactive | Try `hover @eN` then `click @eN` |
| Snapshot slow (>1s) | Win32 native controls | Expected for complex Win32 dialogs; Electron/Qt apps are <50ms |
| Chinese text garbled | Console encoding | Use PowerShell or Windows Terminal with UTF-8 |

## Working safely

- **No browser context** — SeelessUIA interacts with native Windows apps only.
  There are no cookies, no URL spoofing, no XSS. Security is about what apps
  the agent is allowed to control.
- **Refs expire on UI change** — always re-snapshot after interactions that
  change the UI (click, fill, open dialog, switch tab).
- **Daemon is local** — TCP listener on 127.0.0.1 only, no network exposure.
- **PID/hwnd is internal** — users reference windows by `w1`/`w2`, not raw PIDs.
- **Keyboard input via SendInput** — system-level key injection, same as a
  real user typing. Works with any app including UWP and elevated processes.

## Full reference

For the complete command reference, troubleshooting guide, and workflow
templates, use `seeless-uia skills get core --full`.
