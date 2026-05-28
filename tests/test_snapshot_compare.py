"""Snapshot comparison test — default / -i / --no-clean across multiple apps.

Usage:
    python tests/test_snapshot_compare.py                    # all configured apps
    python tests/test_snapshot_compare.py notion             # single app
    python tests/test_snapshot_compare.py notion,wechat      # comma-separated
    python tests/test_snapshot_compare.py --list             # list configured apps
"""

import subprocess
import re
import os
import sys
import json
import time
from pathlib import Path

SEELESS = os.environ.get("SEELESS_EXE", r"E:\project\Seeless-UIA\bin\SeelessUIA.exe")
OUTPUT_DIR = Path(__file__).parent / "output"

APPS = {
    "notion":   {"name": "Notion",   "title": ""},
    "wechat":   {"name": "Weixin",   "title": "\u5fae\u4fe1"},
    "opencode": {"name": "OpenCode", "title": ""},
}


def run_cmd(cmd: list[str], timeout=20) -> tuple[str, float]:
    """Run command, return (output, elapsed_seconds)."""
    t0 = time.monotonic()
    try:
        r = subprocess.run(
            cmd, capture_output=True, text=True, timeout=timeout,
            encoding="utf-8", errors="replace")
        elapsed = time.monotonic() - t0
        return ((r.stdout or "") + (r.stderr or "")).strip(), elapsed
    except subprocess.TimeoutExpired:
        return "(timeout)", time.monotonic() - t0


def list_windows() -> list[dict]:
    """Return current window list from daemon (refreshes registry)."""
    out, _ = run_cmd([SEELESS, "windows", "--json"], timeout=10)
    for line in out.splitlines():
        line = line.strip()
        if line.startswith("{"):
            data = json.loads(line)
            if data.get("success") and "data" in data:
                return data["data"].get("windows", [])
    return []


def find_window(app: dict, windows: list[dict] | None = None) -> tuple[str, int] | None:
    """Match app by process name + optional title. Returns (ref, hwnd)."""
    if windows is None:
        windows = list_windows()
    proc = app["name"].lower()
    title_sub = app.get("title", "").lower()
    for w in windows:
        p = w.get("processName", "").lower()
        t = w.get("title", "").lower()
        if proc in p and (not title_sub or title_sub in t):
            return w["refId"], w["hwnd"]
    return None


def run_snapshot(*, window_ref: str | None = None, hwnd: int | None = None, mode: str) -> tuple[str, dict, float]:
    """mode in {default, i, noclean}. Returns (output, stats, wall_seconds)."""
    t0 = time.monotonic()
    args = [SEELESS, "snapshot"]
    if hwnd is not None:
        args.extend(["--hwnd", str(hwnd)])
    elif window_ref is not None:
        args.append(window_ref)
    else:
        raise ValueError("window_ref or hwnd required")
    if mode == "i":
        args.append("-i")
    elif mode == "noclean":
        args.append("--no-clean")
    out, _ = run_cmd(args, timeout=25)
    elapsed = time.monotonic() - t0

    lines = out.count('\n') + 1 if out else 0
    refs = len(re.findall(r'\[ref=e\d+\]', out))

    # Extract perf line if present
    pm = re.search(r'\[perf\]\s*(.+)', out)
    perf_detail = pm.group(1) if pm else ""

    return out, {"refs": refs, "lines": lines, "perf": perf_detail}, elapsed


def print_row(cols: list[str], widths: list[int]):
    row = "  ".join(c.ljust(w) for c, w in zip(cols, widths))
    print(f"  {row}")


def main():
    if "--list" in sys.argv:
        print("Configured apps:")
        for k, v in APPS.items():
            print(f"  {k:<12} proc={v['name']:<16} title={v['title']!r}")
        return

    # Determine which apps to test
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        targets = list(APPS.keys())
    else:
        targets = []
        for a in args:
            targets.extend(k for k in APPS if k in a.split(","))

    if not targets:
        print("No matching apps.")
        return

    modes = {
        "default": "default (clean)",
        "i":       "interactive (-i)",
        "noclean": "--no-clean (raw)",
    }

    all_results = []  # list of (app, mode, stats, elapsed, ok)

    # Single windows refresh for the whole run — wN is stable per HWND within a session
    windows = list_windows()

    for app_key in targets:
        app = APPS[app_key]
        print(f"\n{'='*60}")
        print(f"  App: {app_key} (proc={app['name']})")

        found = find_window(app, windows)
        if found is None:
            # Window may have appeared since initial list
            windows = list_windows()
            found = find_window(app, windows)
        if found is None:
            print(f"  SKIP — window not found")
            continue

        ref, hwnd = found
        print(f"  Ref: {ref}  HWND: {hwnd}")
        save_dir = OUTPUT_DIR / app_key
        save_dir.mkdir(parents=True, exist_ok=True)

        for mode, desc in modes.items():
            print(f"  [{desc}]...", end=" ", flush=True)
            try:
                out, stats, elapsed = run_snapshot(hwnd=hwnd, mode=mode)
                (save_dir / f"{mode}.txt").write_text(out, encoding="utf-8")
                ok = True
            except Exception as e:
                out = str(e)
                stats = {"refs": 0, "lines": 0, "perf": ""}
                elapsed = 0
                ok = False

            print(f"refs={stats['refs']} time={elapsed:.2f}s")
            all_results.append((app_key, mode, stats, elapsed, ok))

    # ── Summary table ──────────────────────────────────────────
    if not all_results:
        return

    print(f"\n{'='*80}")
    print(f"  Summary")
    print(f"  {'='*80}")
    widths = [14, 10, 8, 8, 10, 12, 50]
    print_row(["App", "Mode", "Refs", "Lines", "Chg%", "Time", "Perf"], widths)
    print_row(["-"*13, "-"*9, "-"*7, "-"*7, "-"*9, "-"*11, "-"*49], widths)

    for app_key in targets:
        app_group = [(m, s, t, ok) for a, m, s, t, ok in all_results if a == app_key]
        noclean_stats = next((s for m, s, t, ok in app_group if m == "noclean"), None)
        noclean_refs = noclean_stats["refs"] if noclean_stats else 1

        for mode, stats, elapsed, ok in app_group:
            if mode == "noclean":
                chg = ""
            else:
                pct = stats["refs"] / max(noclean_refs, 1) * 100
                chg = f"{pct:.0f}%"
            st = "ERR" if not ok else ""
            print_row([
                app_key, f"{mode} {st}", str(stats["refs"]), str(stats["lines"]),
                chg, f"{elapsed:.2f}s", stats["perf"][:48]
            ], widths)

    print(f"\n  Output saved to: {OUTPUT_DIR}")
    print()


if __name__ == "__main__":
    main()
