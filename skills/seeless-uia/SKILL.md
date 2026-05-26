---
name: seeless-uia
description: Windows UI Automation CLI for AI agents. Controls desktop applications via Microsoft UI Automation using the snapshot + ref model.
hidden: true
---

# SeelessUIA Skill

> This file is a **discovery stub**, not the usage guide.  
> To get the actual instructions for your agent, run:

```bash
seeless-uia skills get core
```

The `skills get core` command returns detailed usage instructions that always match your installed CLI version. Do not copy `SKILL.md` files manually — they will become stale.

## Why SeelessUIA

- **Token-efficient** — Snapshot + ref model means you see only interactive elements, not raw DOM or full UIA trees
- **Native Windows apps** — Controls Notepad, Calculator, VS Code, OpenCode, and any Win32/WPF/Electron desktop application
- **Self-hosted** — No cloud dependency, no browser needed, runs entirely on your machine
- **Agent-native design** — CLI-first, JSON output, deterministic ref-based interaction

## Available Skills

| Skill | Description |
|-------|-------------|
| `core` | Full SeelessUIA usage guide — the core skill you should load first |

To load the core skill:

```bash
seeless-uia skills get core
```

To see all available skills:

```bash
seeless-uia skills list
```
