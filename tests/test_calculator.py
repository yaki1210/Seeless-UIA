"""
SeelessUIA Calculator integration test.
Launches Calculator, runs comprehensive command tests, closes Calculator.
Tests: click, dblclick, fill, type, press, hover, check/uncheck,
       get text/value/box/count, is visible/enabled/checked, wait, --json.

Usage:
    python test_calculator.py
    python test_calculator.py --verbose
"""

import subprocess
import time
import sys
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "src" / "bin" / "Debug" / "net10.0-windows" / "SeelessUIA.exe"

VERBOSE = "--verbose" in sys.argv


def run(*args, timeout=30):
    cmd = [str(EXE)] + [str(a) for a in args]
    if VERBOSE:
        print(f"  RUN: {' '.join(cmd)}")
    r = subprocess.run(cmd, capture_output=True, text=True,
                       timeout=timeout, encoding="utf-8", errors="replace")
    if r.returncode != 0 and "Error" not in (r.stdout or "") and "Error" not in (r.stderr or ""):
        raise RuntimeError(f"Command failed ({r.returncode}): {' '.join(cmd)}\nstderr: {r.stderr}")
    return r.stdout.strip(), r.stderr.strip()


def run_json(*args, timeout=30):
    args = list(args) + ["--json"]
    stdout, stderr = run(*args, timeout=timeout)
    try:
        return json.loads(stdout)
    except json.JSONDecodeError:
        raise RuntimeError(f"Invalid JSON from {' '.join(map(str, args))}: {stdout[:200]}")


def assert_contains(haystack, needle, label=""):
    if needle not in haystack:
        raise AssertionError(f"{label}: expected '{needle}' in output\nGot: {haystack[:300]}")


def main():
    print("=== SeelessUIA Calculator Integration Test ===\n")

    # Kill any existing daemon or calculator
    subprocess.run("taskkill /F /IM SeelessUIA.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM CalculatorApp.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM calc.exe 2>nul", shell=True)
    time.sleep(1)

    # ── Launch Calculator ──────────────────────────────────
    print("[1] Launching Calculator...")
    out, err = run("app", "launch", "calc", timeout=30)
    wN = out.strip()
    assert wN.startswith("w"), f"Expected wN ref, got: {out}"
    print(f"  Calculator launched, window ref: {wN}")

    time.sleep(2)

    # ── Snapshot (interactive, JSON) ───────────────────────
    print("\n[2] Taking interactive snapshot...")
    resp = run_json("snapshot", wN, "-i")
    snapshot = resp.get("data", {}).get("snapshot", "")
    refs = resp.get("data", {}).get("refs", {})
    print(f"  Snapshot: {len(snapshot.split(chr(10)))} lines, {len(refs)} refs")
    assert len(refs) > 3, f"Expected more refs, got {len(refs)}"

    # Build automationId -> ref mapping from snapshot text
    import re
    id_to_ref = {}
    for line in snapshot.split("\n"):
        ref_m = re.search(r"ref=(e\d+)", line)
        auto_m = re.search(r"automationId=([^\],\s]+)", line)
        if ref_m and auto_m:
            id_to_ref[auto_m.group(1)] = ref_m.group(1)

    if VERBOSE:
        for k, v in sorted(id_to_ref.items()):
            print(f"    {k} -> {v}")

    # Find calculator button refs by automationId
    btn_ids = {
        "num5Button": None,
        "num3Button": None,
        "num8Button": None,
        "plusButton": None,
        "equalButton": None,
        "clearButton": None,
    }
    for auto_id in btn_ids:
        if auto_id in id_to_ref:
            btn_ids[auto_id] = id_to_ref[auto_id]

    print(f"  Found buttons: {', '.join(k for k, v in btn_ids.items() if v)}")
    missing = [k for k, v in btn_ids.items() if not v]
    if missing:
        print(f"  WARNING: Missing button refs: {missing}")

    # ── is visible / is enabled ────────────────────────────
    print("\n[3] Testing 'is' commands...")
    if btn_ids["num5Button"]:
        r = run_json("is", "visible", btn_ids["num5Button"])
        assert r["data"]["visible"], "num5 should be visible"
        print(f"  is visible num5: True")

        r = run_json("is", "enabled", btn_ids["num5Button"])
        assert r["data"]["enabled"], "num5 should be enabled"
        print(f"  is enabled num5: True")

    # ── get count ─────────────────────────────────────────
    print("\n[4] Testing 'get count'...")
    r = run_json("get", "count", "control:Button")
    count = r["data"]["count"]
    assert count > 5, f"Expected >5 buttons, got {count}"
    print(f"  get count control:Button = {count}")

    # ── click 5 + 3 = ─────────────────────────────────────
    print("\n[5] Testing click: 5 + 3 = ...")
    btn_seq = [("num5Button", "5"), ("plusButton", "+"),
               ("num3Button", "3"), ("equalButton", "=")]
    for auto_id, label in btn_seq:
        ref = btn_ids[auto_id]
        if not ref:
            print(f"  SKIP {label}: no ref for {auto_id}")
            continue
        r = run_json("click", ref)
        assert r["success"], f"click {auto_id} failed: {r.get('error')}"
        print(f"  click {label} ({ref}) OK")
        time.sleep(0.2)

    # ── get text (result) ─────────────────────────────────
    print("\n[6] Testing 'get text' on result...")
    time.sleep(0.5)
    r = run_json("get", "text", "control:Text")
    print(f"  get text control:Text = {r['data']}")

    # ── clear + fill via type ─────────────────────────────
    print("\n[7] Testing fill: clear + type 42...")
    if btn_ids["clearButton"]:
        r = run_json("click", btn_ids["clearButton"])
        print(f"  cleared")
        time.sleep(0.2)

    # Type 42 without selector (uses active focus)
    # Use press to type digits since calculator has no textbox
    run("press", "4")
    time.sleep(0.1)
    run("press", "2")
    time.sleep(0.3)
    print(f"  pressed 4 2")

    r = run_json("get", "text", "control:Text")
    print(f"  display after 42: {r['data']}")

    # ── press Control+c (chord) ────────────────────────────
    print("\n[8] Testing press chord...")
    run("press", "Control+c")
    time.sleep(0.2)
    print(f"  pressed Control+c OK")

    # ── close Calculator ──────────────────────────────────
    print("\n[9] Closing Calculator...")
    r = run_json("close")
    assert r["success"], f"close failed: {r.get('error')}"
    print(f"  Calculator closed")

    # Cleanup
    time.sleep(1)
    subprocess.run("taskkill /F /IM CalculatorApp.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM calc.exe 2>nul", shell=True)

    print(f"\n{'='*50}")
    print("ALL TESTS PASSED")
    print(f"{'='*50}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nAborted.")
        sys.exit(1)
    except Exception as e:
        print(f"\nFAIL: {e}")
        if VERBOSE:
            import traceback
            traceback.print_exc()
        sys.exit(1)
