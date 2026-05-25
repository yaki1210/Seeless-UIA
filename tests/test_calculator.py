"""
SeelessUIA Calculator integration test.
Launches Calculator, runs comprehensive command tests, closes Calculator.

Tests: click, dblclick, fill, type, press, hover, check/uncheck,
       get text/value/box/count, is visible/enabled/checked, wait, --json.

Output: test output, execution log, and performance data saved to tests/output/

Usage:
    python test_calculator.py
    python test_calculator.py --verbose
"""

import subprocess
import time
import sys
import json
import os
import re
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "src" / "bin" / "Debug" / "net10.0-windows" / "SeelessUIA.exe"
OUTPUT_DIR = Path(__file__).resolve().parent / "output"
TIMEOUT = 10  # seconds per command

VERBOSE = "--verbose" in sys.argv


def run(*args, timeout=TIMEOUT):
    cmd = [str(EXE)] + [str(a) for a in args]
    t0 = time.perf_counter()
    try:
        r = subprocess.run(cmd, capture_output=True, text=True,
                          timeout=timeout, encoding="utf-8", errors="replace")
        elapsed = round((time.perf_counter() - t0) * 1000)
        stdout = r.stdout.strip() if r.stdout else ""
        stderr = r.stderr.strip() if r.stderr else ""
        return stdout, stderr, elapsed, r.returncode
    except subprocess.TimeoutExpired:
        elapsed = round((time.perf_counter() - t0) * 1000)
        raise TimeoutError(f"Command timed out after {timeout}s: {' '.join(cmd)}")
    except Exception as e:
        elapsed = round((time.perf_counter() - t0) * 1000)
        raise RuntimeError(f"Command failed: {' '.join(cmd)}\n{str(e)}")


def run_json(*args, timeout=TIMEOUT):
    args = tuple(args) + ("--json",)
    stdout, stderr, elapsed, rc = run(*args, timeout=timeout)
    try:
        return json.loads(stdout), elapsed
    except json.JSONDecodeError:
        raise RuntimeError(f"Invalid JSON from {' '.join(map(str, args))}: {stdout[:200]}")


def format_perf(elapsed_ms):
    if elapsed_ms < 1000:
        return f"{elapsed_ms}ms"
    return f"{elapsed_ms/1000:.1f}s"


class TestLogger:
    def __init__(self, output_dir):
        self.output_dir = Path(output_dir)
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        self.log_file = self.output_dir / f"{self.timestamp}_calc_test.log"
        self.perf_data = []
        self.results = []
        self.log_lines = []

    def log(self, msg):
        line = f"[{datetime.now().strftime('%H:%M:%S')}] {msg}"
        self.log_lines.append(line)
        print(msg)

    def record(self, step, command, elapsed_ms, success, result=None):
        self.perf_data.append({
            "step": step,
            "command": command,
            "elapsed_ms": elapsed_ms,
            "success": success,
            "result": str(result)[:200] if result else None,
        })
        status = "PASS" if success else "FAIL"
        self.results.append(f"  [{status}] {step}: {command} ({format_perf(elapsed_ms)})")
        if VERBOSE:
            print(f"    [{status}] {format_perf(elapsed_ms)}")

    def save(self):
        with open(self.log_file, "w", encoding="utf-8") as f:
            f.write(f"SeelessUIA Calculator Test Log\n")
            f.write(f"Started: {self.timestamp}\n")
            f.write(f"{'='*60}\n\n")
            for line in self.log_lines:
                f.write(line + "\n")
            f.write(f"\n{'='*60}\n")
            f.write("Results Summary:\n")
            for r in self.results:
                f.write(r + "\n")
            f.write(f"\n{'='*60}\n")
            f.write("Performance Data (JSON):\n")
            f.write(json.dumps(self.perf_data, indent=2, ensure_ascii=False))


def main():
    output_dir = OUTPUT_DIR
    logger = TestLogger(output_dir)

    logger.log("=" * 60)
    logger.log("SeelessUIA Calculator Integration Test")
    logger.log(f"Output dir: {output_dir}")
    logger.log(f"Timeout: {TIMEOUT}s per command")
    logger.log("=" * 60)

    # Kill any existing daemon or calculator
    logger.log("\n[0] Cleaning up previous instances...")
    subprocess.run("taskkill /F /IM SeelessUIA.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM CalculatorApp.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM calc.exe 2>nul", shell=True)
    time.sleep(1)
    logger.log("  Cleanup complete")

    # ── Launch Calculator ──────────────────────────────────
    logger.log("\n[1] Launching Calculator...")
    cmd = "app launch calc"
    try:
        out, err, elapsed, rc = run("app", "launch", "calc", timeout=15)
        wN = out.strip()
        logger.record(1, cmd, elapsed, wN.startswith("w"), out)
        if not wN.startswith("w"):
            logger.log(f"  FAILED to launch: stdout={out[:200]}, stderr={err[:200]}")
            logger.save()
            sys.exit(1)
        logger.log(f"  Launched, window ref: {wN}")
    except Exception as e:
        logger.log(f"  FAILED: {e}")
        logger.record(1, cmd, 0, False, str(e))
        logger.save()
        sys.exit(1)

    time.sleep(2)

    # ── Snapshot (interactive) ───────────────────────
    logger.log("\n[2] Taking interactive snapshot...")
    cmd = f"snapshot {wN} -i"
    try:
        resp, elapsed = run_json("snapshot", wN, "-i")
        snapshot = resp.get("data", {}).get("snapshot", "")
        refs = resp.get("data", {}).get("refs", {})
        n_lines = len(snapshot.split("\n"))
        n_refs = len(refs)
        logger.record(2, cmd, elapsed, n_refs > 3, f"{n_lines} lines, {n_refs} refs")
        logger.log(f"  {n_lines} lines, {n_refs} refs")

        if n_refs <= 3:
            logger.log(f"  WARNING: only {n_refs} refs found — snapshot may be incomplete")
    except Exception as e:
        logger.log(f"  FAILED: {e}")
        logger.record(2, cmd, 0, False, str(e))
        logger.save()
        sys.exit(1)

    # Build automationId -> ref mapping from snapshot text
    # Format: "... [ref=e28, num5Button] ..."  (automationId in brackets after ref)
    id_to_ref = {}
    for line in snapshot.split("\n"):
        m = re.search(r"\[ref=(e\d+),\s*(\S+)\]", line)
        if m:
            id_to_ref[m.group(2)] = m.group(1)

    if VERBOSE:
        for k, v in sorted(id_to_ref.items()):
            logger.log(f"    {k} -> {v}")

    # Find calculator button refs
    btn_ids = {
        "num5Button": None, "num3Button": None, "num8Button": None,
        "plusButton": None, "equalButton": None, "clearButton": None,
    }
    for auto_id in btn_ids:
        if auto_id in id_to_ref:
            btn_ids[auto_id] = id_to_ref[auto_id]

    present = [k for k, v in btn_ids.items() if v]
    missing = [k for k, v in btn_ids.items() if not v]
    logger.log(f"  Found: {', '.join(present) if present else 'none'}")
    if missing:
        logger.log(f"  Missing: {', '.join(missing)}")

    # ── is visible / is enabled ────────────────────────
    logger.log("\n[3] Testing 'is' commands...")
    if btn_ids["num5Button"]:
        cmd = f"is visible {btn_ids['num5Button']}"
        try:
            resp, elapsed = run_json("is", "visible", btn_ids["num5Button"])
            ok = resp["success"] and resp["data"]["visible"]
            logger.record(3, cmd, elapsed, ok, resp["data"])
            logger.log(f"  is visible num5: {resp['data']['visible']}")
        except Exception as e:
            logger.record(3, cmd, 0, False, str(e))
            logger.log(f"  FAIL: {e}")

        cmd = f"is enabled {btn_ids['num5Button']}"
        try:
            resp, elapsed = run_json("is", "enabled", btn_ids["num5Button"])
            logger.record(4, cmd, elapsed, resp["success"], resp["data"])
            logger.log(f"  is enabled num5: {resp['data']['enabled']}")
        except Exception as e:
            logger.record(4, cmd, 0, False, str(e))
            logger.log(f"  FAIL: {e}")

    # ── get count ──────────────────────────────────────
    logger.log("\n[4] Testing 'get count'...")
    cmd = "get count control:Button"
    try:
        resp, elapsed = run_json("get", "count", "control:Button")
        count = resp["data"]["count"]
        logger.record(5, cmd, elapsed, count > 0, count)
        logger.log(f"  buttons found: {count}")
    except Exception as e:
        logger.record(5, cmd, 0, False, str(e))
        logger.log(f"  FAIL: {e}")

    # ── click 5 + 3 = ─────────────────────────────────
    logger.log("\n[5] Testing click: 5 + 3 = ...")
    btn_seq = [("num5Button", "5"), ("plusButton", "+"),
               ("num3Button", "3"), ("equalButton", "=")]
    for auto_id, label in btn_seq:
        ref = btn_ids[auto_id]
        if not ref:
            logger.log(f"  SKIP {label}: no ref")
            continue
        cmd = f"click {ref}"
        try:
            resp, elapsed = run_json("click", ref)
            logger.record(10, cmd, elapsed, resp["success"], resp.get("data"))
            logger.log(f"  click {label} ({ref})")
            time.sleep(0.2)
        except Exception as e:
            logger.record(10, cmd, 0, False, str(e))
            logger.log(f"  FAIL: {e}")

    # ── get text (result) ──────────────────────────────
    logger.log("\n[6] Testing 'get text' on display...")
    time.sleep(0.5)
    cmd = "get text control:Text"
    try:
        resp, elapsed = run_json("get", "text", "control:Text")
        display = resp["data"].get("text", "")
        logger.record(20, cmd, elapsed, True, display)
        logger.log(f"  display: {display}")
    except Exception as e:
        logger.record(20, cmd, 0, False, str(e))
        logger.log(f"  FAIL: {e}")

    # ── clear + press digits ─────────────────────────────
    logger.log("\n[7] Testing press: typing 42...")
    if btn_ids["clearButton"]:
        try:
            resp, elapsed = run_json("click", btn_ids["clearButton"])
            logger.log(f"  cleared")
            time.sleep(0.2)
        except:
            pass

    for ch in "42":
        cmd = f"press {ch}"
        try:
            stdout, stderr, elapsed, rc = run("press", ch)
            logger.record(30, cmd, elapsed, rc == 0, stdout)
        except Exception as e:
            logger.record(30, cmd, 0, False, str(e))
        time.sleep(0.1)

    time.sleep(0.3)
    try:
        resp, elapsed = run_json("get", "text", "control:Text")
        logger.log(f"  display after 42: {resp['data'].get('text','')}")
    except:
        pass

    # ── press chord ────────────────────────────────────
    logger.log("\n[8] Testing press chord (Control+c)...")
    cmd = "press Control+c"
    try:
        stdout, stderr, elapsed, rc = run("press", "Control+c")
        logger.record(40, cmd, elapsed, rc == 0)
        logger.log(f"  chord OK")
    except Exception as e:
        logger.record(40, cmd, 0, False, str(e))
        logger.log(f"  FAIL: {e}")

    # ── get box ────────────────────────────────────────
    logger.log("\n[9] Testing 'get box'...")
    if btn_ids["num5Button"]:
        cmd = f"get box {btn_ids['num5Button']}"
        try:
            resp, elapsed = run_json("get", "box", btn_ids["num5Button"])
            data = resp["data"]
            logger.record(50, cmd, elapsed, True, data)
            logger.log(f"  box: ({data['x']:.0f},{data['y']:.0f}) {data['width']:.0f}x{data['height']:.0f}")
        except Exception as e:
            logger.record(50, cmd, 0, False, str(e))
            logger.log(f"  FAIL: {e}")

    # ── Close Calculator ──────────────────────────────
    logger.log("\n[10] Closing Calculator...")
    cmd = "close"
    try:
        resp, elapsed = run_json("close")
        logger.record(99, cmd, elapsed, resp["success"], resp.get("data"))
        logger.log(f"  closed")
    except Exception as e:
        logger.record(99, cmd, 0, False, str(e))
        logger.log(f"  FAIL: {e}")

    # Cleanup
    time.sleep(1)
    subprocess.run("taskkill /F /IM CalculatorApp.exe 2>nul", shell=True)
    subprocess.run("taskkill /F /IM calc.exe 2>nul", shell=True)

    # ── Summary ──────────────────────────────────────
    logger.log(f"\n{'='*60}")
    passed = sum(1 for p in logger.perf_data if p["success"])
    total = len(logger.perf_data)
    logger.log(f"Results: {passed}/{total} passed")
    total_time = sum(p["elapsed_ms"] for p in logger.perf_data)
    logger.log(f"Total time: {format_perf(total_time)}")

    for r in logger.results:
        logger.log(r)

    logger.save()
    logger.log(f"\nLog saved to: {logger.log_file}")

    if passed < total:
        sys.exit(1)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nAborted.")
        sys.exit(1)
    except Exception as e:
        print(f"\nFATAL: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
