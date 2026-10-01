#!/usr/bin/env python3
"""Summarize an A/B run produced by run-main-vs-3.0.sh.

usage: summarize-ab.py <out-dir> <base-name> <new-name> [--mean-pct 5] [--tail-pct 10] [--alloc-pct 2] [--dapper-pct 3]

Reads BenchmarkDotNet *-report-full-compressed.json from <out-dir>/<arm>/round*/BenchmarkDotNet.Artifacts/results.
Per benchmark it compares the arms on: median and mean time, P95/P99 across iterations, exact bytes
allocated per op, and GC collections per 1000 ops.

Method: Dapper rows in the same class are an in-run control. If Dapper's own time differs between arms by
more than --dapper-pct the machine drifted and time deltas are marked INVALID rather than trusted. Non-Dapper
rows are also reported normalized to the Dapper geometric mean of their (class, params) group.

Caveat: the tail columns are computed over per-iteration means (BDN iterations each average many
invocations), so they show iteration-to-iteration tail, not per-operation latency tail.
"""
import glob, json, math, os, statistics, sys
from collections import defaultdict


def pct(sorted_vals, p):
    if not sorted_vals:
        return float("nan")
    k = (len(sorted_vals) - 1) * p / 100.0
    lo, hi = math.floor(k), math.ceil(k)
    return sorted_vals[lo] + (sorted_vals[hi] - sorted_vals[lo]) * (k - lo)


def load_arm(out, arm):
    """-> {round: {(type, method, params): metrics}}"""
    rounds = {}
    for rdir in sorted(glob.glob(os.path.join(out, arm, "round*"))):
        rnd = os.path.basename(rdir)
        rows = {}
        pattern = os.path.join(rdir, "BenchmarkDotNet.Artifacts", "results", "*-report-full-compressed.json")
        for f in glob.glob(pattern):
            for b in json.load(open(f))["Benchmarks"]:
                st, mem = b["Statistics"], b.get("Memory") or {}
                if not st or not st.get("OriginalValues"):
                    continue
                vals = sorted(st["OriginalValues"])
                ops = mem.get("TotalOperations") or 0

                def per1k(n, ops=ops):
                    return (n / ops * 1000) if ops else float("nan")

                rows[(b["Type"], b["Method"], b["Parameters"])] = dict(
                    median=st["Median"], mean=st["Mean"], p95=pct(vals, 95), p99=pct(vals, 99),
                    alloc=mem.get("BytesAllocatedPerOperation"),
                    g0=per1k(mem.get("Gen0Collections", 0)), g1=per1k(mem.get("Gen1Collections", 0)),
                    g2=per1k(mem.get("Gen2Collections", 0)), n=st["N"])
        if rows:
            rounds[rnd] = rows
    return rounds


def fmt_t(ns):
    for unit, div in (("s", 1e9), ("ms", 1e6), ("us", 1e3)):
        if ns >= div:
            return f"{ns / div:.3f}{unit}"
    return f"{ns:.1f}ns"


def delta(new, base):
    return (new / base - 1.0) * 100.0 if base else float("nan")


def med(xs):
    xs = [x for x in xs if x is not None and not math.isnan(x)]
    return statistics.median(xs) if xs else float("nan")


def spread_pct(arm, key, field):
    """Round-to-round spread (max-min)/median within one arm, in percent; 0 if fewer than 2 rounds."""
    vals = [r[key][field] for r in arm.values() if key in r]
    if len(vals) < 2:
        return 0.0
    m = statistics.median(vals)
    return (max(vals) - min(vals)) / m * 100.0 if m else 0.0


def geomean(xs):
    xs = [x for x in xs if x and x > 0]
    return math.exp(sum(math.log(x) for x in xs) / len(xs)) if xs else float("nan")


def main():
    a = sys.argv[1:]
    if len(a) < 3:
        print(__doc__)
        sys.exit(2)
    out, base, new = a[0], a[1], a[2]
    opt = {"--mean-pct": 5.0, "--tail-pct": 10.0, "--alloc-pct": 2.0, "--dapper-pct": 3.0}
    for i in range(3, len(a) - 1, 2):
        if a[i] in opt:
            opt[a[i]] = float(a[i + 1])
    mean_t, tail_t, alloc_t, dapper_t = opt["--mean-pct"], opt["--tail-pct"], opt["--alloc-pct"], opt["--dapper-pct"]

    B, N = load_arm(out, base), load_arm(out, new)
    if not B or not N:
        print(f"no results: base={len(B)} rounds, new={len(N)} rounds under {out}")
        sys.exit(1)
    keys = sorted(set().union(*[set(r) for r in B.values()]) & set().union(*[set(r) for r in N.values()]))

    def agg(arm, key, field):
        return med([r[key][field] for r in arm.values() if key in r])

    groups = defaultdict(list)
    for k in keys:
        groups[(k[0], k[2])].append(k)
    control = {}
    for g, ks in groups.items():
        dk = [k for k in ks if "dapper" in k[1].lower()]
        if dk:
            control[g] = (geomean([agg(B, k, "median") for k in dk]), geomean([agg(N, k, "median") for k in dk]))

    print(f"# {base} vs {new}  (rounds: {len(B)} / {len(N)})\n")
    print(f"Flags: time>{mean_t}% (Dapper-normalized when a control exists) AND larger than the round-to-round spread, tail(P99)>{tail_t}% AND larger than P99 spread, alloc>{alloc_t}%. "
          f"Time deltas untrusted if Dapper drifts >{dapper_t}%.\n")
    hdr = ("| class | method | params | base med | new med | d med | d vs Dapper | d P95 | d P99 "
           "| base B/op | new B/op | d alloc | d gen0/1k | round spread | flags |")
    print(hdr)
    print("|" + "---|" * (hdr.count("|") - 1))

    flagged, invalid = [], set()
    for k in keys:
        bm, nm = agg(B, k, "median"), agg(N, k, "median")
        d_med = delta(nm, bm)
        g = (k[0], k[2])
        d_norm = float("nan")
        if g in control and "dapper" not in k[1].lower():
            cb, cn = control[g]
            d_norm = delta(nm / cn, bm / cb)
        d95, d99 = delta(agg(N, k, "p95"), agg(B, k, "p95")), delta(agg(N, k, "p99"), agg(B, k, "p99"))
        ba, na = agg(B, k, "alloc"), agg(N, k, "alloc")
        d_alloc = delta(na, ba) if ba else float("nan")
        d_g0 = agg(N, k, "g0") - agg(B, k, "g0")
        noise_t = max(spread_pct(B, k, "median"), spread_pct(N, k, "median"))
        noise_99 = max(spread_pct(B, k, "p99"), spread_pct(N, k, "p99"))
        flags = []
        if "dapper" in k[1].lower():
            if abs(d_med) > dapper_t:
                flags.append("DAPPER-DRIFT")
                invalid.add(g)
        else:
            t = d_norm if not math.isnan(d_norm) else d_med
            if abs(t) > mean_t and abs(t) <= noise_t:
                flags.append("within-noise")
            elif t > mean_t:
                flags.append("TIME+")
            elif t < -mean_t:
                flags.append("time-")
        if d99 > tail_t and d99 > noise_99:
            flags.append("TAIL+")
        if not math.isnan(d_alloc) and d_alloc > alloc_t:
            flags.append("ALLOC+")
        rr = [r for r in B if r in N and k in B[r] and k in N[r]]
        if len(rr) >= 2 and any(f in flags for f in ("TIME+", "time-", "TAIL+")):
            signs = {delta(N[r][k]["median"], B[r][k]["median"]) > 0 for r in rr}
            flags.append("repro" if len(signs) == 1 else "NOT-REPRO")

        def cell(x):
            return "n/a" if math.isnan(x) else f"{x:+.1f}%"

        print(f"| {k[0]} | {k[1]} | {k[2]} | {fmt_t(bm)} | {fmt_t(nm)} | {cell(d_med)} | {cell(d_norm)} | {cell(d95)} "
              f"| {cell(d99)} | {'' if ba is None else int(ba)} | {'' if na is None else int(na)} | {cell(d_alloc)} "
              f"| {d_g0:+.2f} | ±{noise_t:.1f}% | {' '.join(flags)} |")
        if any(f in flags for f in ("TIME+", "time-", "TAIL+", "ALLOC+")):
            flagged.append((k, flags))

    print("\n## Validity")
    print("- Dapper control drift exceeded threshold for: " +
          (", ".join(f"{t}[{p}]" for t, p in sorted(invalid)) or "none"))
    print("\n## Flagged (investigate; `repro` = same direction in every paired round)")
    for k, f in flagged:
        print(f"- {k[0]}.{k[1]} [{k[2]}]: {' '.join(f)}")
    if not flagged:
        print("- none")


if __name__ == "__main__":
    main()
