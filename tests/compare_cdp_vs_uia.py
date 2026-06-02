"""
Enhanced CDP vs UIA comparison with element overlap and tree structure analysis.

Mode mapping (SeelessUIA ↔ agent-browser):
  Raw tree:       snapshot --no-clean  ↔  snapshot
  Structured:     snapshot             ↔  snapshot -c
  Interactive:    snapshot -i          ↔  snapshot -i

Usage:
  python tests/compare_cdp_vs_uia.py
"""

import subprocess, json, re, time, sys, os
from collections import Counter, defaultdict
from pathlib import Path

# Fix Windows console encoding for Unicode output
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent
SEELESSUIA = str(ROOT / "src" / "bin" / "Release" / "net10.0-windows" / "SeelessUIA.exe")
OUTPUT = ROOT / "tests" / "output" / "cdp_vs_uia"
OUTPUT.mkdir(parents=True, exist_ok=True)
CDP_PORT = 9223


# ════════════════════════════════════════════════════════════════════
# Parse & Helpers
# ════════════════════════════════════════════════════════════════════

def run(cmd, timeout=30):
    use_shell = isinstance(cmd, str)
    t0 = time.perf_counter()
    r = subprocess.run(cmd, capture_output=True, text=True,
                       encoding="utf-8", errors="replace",
                       timeout=timeout, cwd=ROOT, shell=use_shell)
    return r.stdout or "", r.stderr or "", round(time.perf_counter() - t0, 3)


def j(s):
    for line in (s or "").split("\n"):
        line = line.strip()
        if line.startswith("{"):
            try: return json.loads(line)
            except: pass
    return {}


def parse_line(line):
    """Parse a single snapshot line → (depth, role, name, has_ref)."""
    if not line or not line.strip():
        return None
    stripped = line.rstrip()
    # Count leading spaces (2-space indent)
    depth = 0
    for ch in stripped:
        if ch == ' ': depth += 1
        else: break
    depth //= 2
    content = stripped[depth*2:]
    # Must start with "- "
    if not content.startswith("- "):
        return None
    content = content[2:]
    # Extract role (first word)
    m = re.match(r'(\S+)\s*', content)
    if not m:
        return None
    role = m.group(1)
    rest = content[m.end():]
    # Extract quoted name
    name = ""
    if rest.startswith('"'):
        # Find matching closing quote, handling escaped quotes
        end = 1
        while end < len(rest):
            if rest[end] == '"' and rest[end-1] != '\\':
                break
            end += 1
        name = rest[1:end].strip()
    has_ref = "[ref=" in rest
    return (depth, role, name, has_ref)


def parse_snapshot(text):
    """Parse full snapshot text → list of (depth, role, name, has_ref)."""
    elements = []
    for line in (text or "").split("\n"):
        parsed = parse_line(line)
        if parsed:
            elements.append(parsed)
    return elements


def normalize_name(name):
    """Normalize name for matching: lowercase ASCII, collapse whitespace, strip."""
    if not name:
        return ""
    n = name.strip()
    n = re.sub(r'\s+', ' ', n)
    # Lowercase ASCII only (preserve Chinese case)
    n = re.sub(r'[a-zA-Z]', lambda m: m.group().lower(), n)
    return n


def make_key(role, name):
    """Create normalized (role, name) key for matching."""
    return (role.lower(), normalize_name(name))


def basic_stats(elements):
    """Compute basic stats from parsed elements."""
    total = len(elements)
    named = sum(1 for _, _, n, _ in elements if n)
    has_ref = sum(1 for _, _, _, r in elements if r)
    roles = Counter(r for _, r, _, _ in elements)
    generic = roles.get("generic", 0)
    max_depth = max((d for d, _, _, _ in elements), default=0)
    return {
        "total": total,
        "named": named,
        "has_ref": has_ref,
        "roles": len(roles),
        "generic": generic,
        "max_depth": max_depth,
        "role_dist": roles,
    }


# ════════════════════════════════════════════════════════════════════
# Element Overlap Analysis
# ════════════════════════════════════════════════════════════════════

def compute_overlap(cdp_elements, uia_elements):
    """Match elements by (role, normalized_name). Returns matched, cdp_only, uia_only lists."""
    # Build key→count maps (handle duplicates)
    cdp_keys = Counter()
    for _, role, name, _ in cdp_elements:
        cdp_keys[make_key(role, name)] += 1
    uia_keys = Counter()
    for _, role, name, _ in uia_elements:
        uia_keys[make_key(role, name)] += 1

    all_keys = set(cdp_keys.keys()) | set(uia_keys.keys())
    matched = []
    cdp_only = []
    uia_only = []
    for k in all_keys:
        c = cdp_keys.get(k, 0)
        u = uia_keys.get(k, 0)
        m = min(c, u)
        if m > 0:
            matched.append((k, m))
        if c > m:
            cdp_only.append((k, c - m))
        if u > m:
            uia_only.append((k, u - m))
    return matched, cdp_only, uia_only


def role_overlap_breakdown(cdp_elements, uia_elements):
    """Per-role overlap breakdown."""
    cdp_by_role = defaultdict(list)
    for _, role, name, _ in cdp_elements:
        cdp_by_role[role.lower()].append(name)
    uia_by_role = defaultdict(list)
    for _, role, name, _ in uia_elements:
        uia_by_role[role.lower()].append(name)

    all_roles = sorted(set(cdp_by_role.keys()) | set(uia_by_role.keys()))
    rows = []
    for role in all_roles:
        cdp_names = [normalize_name(n) for n in cdp_by_role.get(role, [])]
        uia_names = [normalize_name(n) for n in uia_by_role.get(role, [])]
        cdp_set = set(cdp_names)
        uia_set = set(uia_names)
        both = cdp_set & uia_set
        c_only = cdp_set - uia_set
        u_only = uia_set - cdp_set
        rows.append((role, len(cdp_names), len(uia_names), len(both), len(c_only), len(u_only)))
    return rows


# ════════════════════════════════════════════════════════════════════
# Tree Structure Analysis
# ════════════════════════════════════════════════════════════════════

def tree_structure(elements):
    """Analyze tree structure: depth distribution, branching, generic ratio."""
    if not elements:
        return {"max_depth": 0, "avg_children": 0, "depth_dist": {},
                "generic_ratio": 0, "named_ratio": 0}

    depths = [d for d, _, _, _ in elements]
    max_depth = max(depths)
    total = len(elements)

    # Depth distribution
    depth_dist = Counter(depths)

    # Branching factor: for each node, count children (next nodes with depth+1)
    children_counts = []
    for i, (d, _, _, _) in enumerate(elements):
        count = 0
        j = i + 1
        while j < total and elements[j][0] > d:
            if elements[j][0] == d + 1:
                count += 1
            j += 1
        children_counts.append(count)
    avg_children = sum(children_counts) / total if total else 0

    # Generic ratio
    generic = sum(1 for _, r, _, _ in elements if r.lower() == "generic")
    named = sum(1 for _, _, n, _ in elements if n)

    return {
        "max_depth": max_depth,
        "avg_children": round(avg_children, 2),
        "depth_dist": dict(sorted(depth_dist.items())),
        "generic": generic,
        "generic_ratio": round(generic / total * 100, 1) if total else 0,
        "named": named,
        "named_ratio": round(named / total * 100, 1) if total else 0,
    }


# ════════════════════════════════════════════════════════════════════
# Launch & Capture
# ════════════════════════════════════════════════════════════════════

print("=" * 72)
print("  CDP vs UIA Comparison — VS Code")
print("=" * 72)

print("\n[0] Launching VS Code with CDP...")
subprocess.run("taskkill /F /IM Code.exe 2>nul", shell=True)
subprocess.run("taskkill /F /IM SeelessUIA.exe 2>nul", shell=True)
time.sleep(2)
subprocess.Popen(f"Code.cmd --remote-debugging-port={CDP_PORT} --no-sandbox --disable-gpu",
                 shell=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
time.sleep(6)
print("    VS Code launched.")

# ── Connect agent-browser ──────────────────────────────────────
stdout, _, t_connect = run(f"agent-browser connect {CDP_PORT}")
cdp_ok = "Done" in stdout
print(f"    agent-browser connect: {t_connect:.3f}s {'OK' if cdp_ok else 'ERR'}")
time.sleep(0.5)

# ── Find VS Code window for UIA ────────────────────────────────
stdout, _, _ = run([SEELESSUIA, "windows", "--json"])
wins = j(stdout).get("data", {}).get("windows", [])
code_w = next((w for w in wins if w.get("processName") == "Code"), None)
uia_ok = False
if code_w:
    stdout, _, _ = run([SEELESSUIA, "window", code_w["refId"]])
    uia_ok = True
    print(f"    UIA window: {code_w['refId']}")
else:
    print("    UIA window: NOT FOUND")
time.sleep(0.3)


# ── Capture all 6 snapshots ────────────────────────────────────
MODES = [
    # (label,        uia_args,              cdp_args)
    ("raw (--no-clean)", ["--no-clean"],     []),
    ("structured",       [],                 ["-c"]),
    ("interactive",      ["-i"],             ["-i"]),
]

results = {}  # key: (mode, tool) → (text, refs_dict, time, stderr)

for mode_label, uia_args, cdp_args in MODES:
    print(f"\n  Capturing: {mode_label}...")

    # UIA
    if uia_ok:
        cmd = [SEELESSUIA, "snapshot"] + uia_args + ["--json"]
        stdout, stderr, t = run(cmd)
        data = j(stdout)
        text = data.get("data", {}).get("snapshot", "")
        refs = data.get("data", {}).get("refs", {})
        results[(mode_label, "uia")] = (text, refs, t, stderr)
        print(f"    UIA: {t:.3f}s  refs={len(refs)}  chars={len(text)}")
    else:
        results[(mode_label, "uia")] = ("", {}, 0, "")

    # CDP
    if cdp_ok:
        cmd = "agent-browser snapshot " + " ".join(cdp_args) + " --json"
        stdout, stderr, t = run(cmd)
        data = j(stdout)
        text = data.get("data", {}).get("snapshot", "")
        refs = data.get("data", {}).get("refs", {})
        results[(mode_label, "cdp")] = (text, refs, t, stderr)
        print(f"    CDP: {t:.3f}s  refs={len(refs)}  chars={len(text)}")
    else:
        results[(mode_label, "cdp")] = ("", {}, 0, "")


# ════════════════════════════════════════════════════════════════════
# Analysis & Output
# ════════════════════════════════════════════════════════════════════

W = 72
SEP = "=" * W

def section(title):
    print(f"\n{SEP}")
    print(f"  {title}")
    print(SEP)


def row(label, cdp_val, uia_val, w=20):
    print(f"    {label:<28s}  CDP: {str(cdp_val):>{w}s}  UIA: {str(uia_val):>{w}s}")


# ── 1. Basic Metrics ───────────────────────────────────────────
section("1. Basic Metrics")

for mode_label, _, _ in MODES:
    cdp_text, cdp_refs, cdp_t, _ = results.get((mode_label, "cdp"), ("", {}, 0, ""))
    uia_text, uia_refs, uia_t, _ = results.get((mode_label, "uia"), ("", {}, 0, ""))

    print(f"\n  [{mode_label}]")
    row("Time", f"{cdp_t:.3f}s", f"{uia_t:.3f}s")
    row("Refs", str(len(cdp_refs)), str(len(uia_refs)))
    row("Lines", str(cdp_text.count(chr(10))+1 if cdp_text else 0),
                     str(uia_text.count(chr(10))+1 if uia_text else 0))
    row("Chars", str(len(cdp_text)), str(len(uia_text)))
    cdp_roles = len(set(v.get("role","") for v in cdp_refs.values()))
    uia_roles = len(set(v.get("role","") for v in uia_refs.values()))
    row("Unique roles", str(cdp_roles), str(uia_roles))
    cdp_named = sum(1 for v in cdp_refs.values() if v.get("name",""))
    uia_named = sum(1 for v in uia_refs.values() if v.get("name",""))
    row("Named refs", str(cdp_named), str(uia_named))
    cdp_cpr = len(cdp_text) / max(len(cdp_refs), 1)
    uia_cpr = len(uia_text) / max(len(uia_refs), 1)
    row("Chars/ref", f"{cdp_cpr:.1f}", f"{uia_cpr:.1f}")


# ── 2. Element Overlap (interactive mode) ──────────────────────
section("2. Element Overlap (interactive -i)")

cdp_i_text = results.get(("interactive", "cdp"), ("", {}, 0, ""))[0]
uia_i_text = results.get(("interactive", "uia"), ("", {}, 0, ""))[0]
cdp_i_elements = parse_snapshot(cdp_i_text)
uia_i_elements = parse_snapshot(uia_i_text)

matched, cdp_only, uia_only = compute_overlap(cdp_i_elements, uia_i_elements)
total_keys = len(matched) + len(cdp_only) + len(uia_only)
jaccard = len(matched) / total_keys if total_keys else 0

print(f"\n  Matched (both):   {len(matched):>4}  ({len(matched)/max(total_keys,1)*100:.1f}%)")
print(f"  CDP-only:         {len(cdp_only):>4}  ({len(cdp_only)/max(total_keys,1)*100:.1f}%)")
print(f"  UIA-only:         {len(uia_only):>4}  ({len(uia_only)/max(total_keys,1)*100:.1f}%)")
print(f"  Jaccard:          {jaccard:.3f}")

# Per-role breakdown
print(f"\n  {'Role':<16s} {'CDP':>5s} {'UIA':>5s} {'Both':>5s} {'CDP-':>5s} {'UIA-':>5s}")
print(f"  {'-'*15} {'-'*5} {'-'*5} {'-'*5} {'-'*5} {'-'*5}")
role_rows = role_overlap_breakdown(cdp_i_elements, uia_i_elements)
for role, c, u, b, co, uo in role_rows:
    if b > 0 or co > 0 or uo > 0:
        print(f"  {role:<16s} {c:>5d} {u:>5d} {b:>5d} {co:>5d} {uo:>5d}")

# CDP-only elements (first 15)
if cdp_only:
    print(f"\n  CDP-only elements (top 15):")
    for (role, name), cnt in sorted(cdp_only, key=lambda x: -x[1])[:15]:
        nm = name[:40] + "..." if len(name) > 40 else name
        extra = f" x{cnt}" if cnt > 1 else ""
        print(f"    - {role} \"{nm}\"{extra}")

# UIA-only elements (first 15)
if uia_only:
    print(f"\n  UIA-only elements (top 15):")
    for (role, name), cnt in sorted(uia_only, key=lambda x: -x[1])[:15]:
        nm = name[:40] + "..." if len(name) > 40 else name
        extra = f" x{cnt}" if cnt > 1 else ""
        print(f"    - {role} \"{nm}\"{extra}")


# ── 3. Element Overlap (raw tree) ──────────────────────────────
section("3. Element Overlap (raw tree: UIA --no-clean vs CDP snapshot)")

cdp_raw_text = results.get(("raw (--no-clean)", "cdp"), ("", {}, 0, ""))[0]
uia_raw_text = results.get(("raw (--no-clean)", "uia"), ("", {}, 0, ""))[0]
cdp_raw_elements = parse_snapshot(cdp_raw_text)
uia_raw_elements = parse_snapshot(uia_raw_text)

matched_r, cdp_only_r, uia_only_r = compute_overlap(cdp_raw_elements, uia_raw_elements)
total_r = len(matched_r) + len(cdp_only_r) + len(uia_only_r)
jaccard_r = len(matched_r) / total_r if total_r else 0

print(f"\n  Matched (both):   {len(matched_r):>4}  ({len(matched_r)/max(total_r,1)*100:.1f}%)")
print(f"  CDP-only:         {len(cdp_only_r):>4}  ({len(cdp_only_r)/max(total_r,1)*100:.1f}%)")
print(f"  UIA-only:         {len(uia_only_r):>4}  ({len(uia_only_r)/max(total_r,1)*100:.1f}%)")
print(f"  Jaccard:          {jaccard_r:.3f}")

print(f"\n  {'Role':<16s} {'CDP':>5s} {'UIA':>5s} {'Both':>5s} {'CDP-':>5s} {'UIA-':>5s}")
print(f"  {'-'*15} {'-'*5} {'-'*5} {'-'*5} {'-'*5} {'-'*5}")
role_rows_r = role_overlap_breakdown(cdp_raw_elements, uia_raw_elements)
for role, c, u, b, co, uo in role_rows_r:
    if b > 0 or co > 0 or uo > 0:
        print(f"  {role:<16s} {c:>5d} {u:>5d} {b:>5d} {co:>5d} {uo:>5d}")

if cdp_only_r:
    print(f"\n  CDP-only elements (top 15):")
    for (role, name), cnt in sorted(cdp_only_r, key=lambda x: -x[1])[:15]:
        nm = name[:40] + "..." if len(name) > 40 else name
        extra = f" x{cnt}" if cnt > 1 else ""
        print(f"    - {role} \"{nm}\"{extra}")

if uia_only_r:
    print(f"\n  UIA-only elements (top 15):")
    for (role, name), cnt in sorted(uia_only_r, key=lambda x: -x[1])[:15]:
        nm = name[:40] + "..." if len(name) > 40 else name
        extra = f" x{cnt}" if cnt > 1 else ""
        print(f"    - {role} \"{nm}\"{extra}")


# ── 4. Tree Structure ──────────────────────────────────────────
section("4. Tree Structure")

for mode_label, _, _ in MODES:
    cdp_text = results.get((mode_label, "cdp"), ("", {}, 0, ""))[0]
    uia_text = results.get((mode_label, "uia"), ("", {}, 0, ""))[0]
    cdp_tree = tree_structure(parse_snapshot(cdp_text))
    uia_tree = tree_structure(parse_snapshot(uia_text))

    print(f"\n  [{mode_label}]")
    row("Total nodes", str(cdp_tree["total"] if "total" in cdp_tree else len(parse_snapshot(cdp_text))),
                       str(uia_tree["total"] if "total" in uia_tree else len(parse_snapshot(uia_text))))
    row("Max depth", str(cdp_tree["max_depth"]), str(uia_tree["max_depth"]))
    row("Avg children/node", str(cdp_tree["avg_children"]), str(uia_tree["avg_children"]))
    row("Generic nodes", f"{cdp_tree['generic']} ({cdp_tree['generic_ratio']}%)",
                         f"{uia_tree['generic']} ({uia_tree['generic_ratio']}%)")
    row("Named nodes", f"{cdp_tree['named']} ({cdp_tree['named_ratio']}%)",
                       f"{uia_tree['named']} ({uia_tree['named_ratio']}%)")

    # Depth distribution
    all_depths = sorted(set(list(cdp_tree["depth_dist"].keys()) + list(uia_tree["depth_dist"].keys())))
    print(f"\n    {'Depth':<8s} {'CDP':>8s} {'UIA':>8s}")
    print(f"    {'-'*7} {'-'*8} {'-'*8}")
    for d in all_depths:
        cv = cdp_tree["depth_dist"].get(d, 0)
        uv = uia_tree["depth_dist"].get(d, 0)
        print(f"    {d:<8d} {cv:>8d} {uv:>8d}")


# ── 5. UIA Perf Details ────────────────────────────────────────
section("5. UIA Performance Details")

for mode_label, _, _ in MODES:
    stderr = results.get((mode_label, "uia"), ("", {}, 0, ""))[3]
    perf_lines = [l for l in (stderr or "").split("|") if "perf" in l.lower()]
    if perf_lines:
        print(f"  [{mode_label}] {perf_lines[0][:120]}")


# ════════════════════════════════════════════════════════════════════
# Save Snapshots
# ════════════════════════════════════════════════════════════════════

for mode_label, _, _ in MODES:
    for tool in ("cdp", "uia"):
        text = results.get((mode_label, tool), ("", {}, 0, ""))[0]
        fname = f"{tool}_{mode_label.split()[0].replace('(','')}.txt"
        (OUTPUT / fname).write_text(text, encoding="utf-8")

print(f"\n{SEP}")
print(f"  Snapshots saved to: {OUTPUT}")
print(SEP)
