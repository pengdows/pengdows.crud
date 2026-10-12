#!/usr/bin/env python3
"""Fold the latest integration results into docs/integration-status.md.

run-integration-tests.sh writes one TestResults/integration/summary-<tfm>-<db>.md per database and framework.
This reads them and keeps one row per database in docs/integration-status.md, so the document says, for each
database and framework, when it last ran, against which commit, and how it came out. A database that was not
run this time keeps its previous cell; only a result newer than the recorded one replaces it. A result that
predates the newest commit touching anything but docs/ and benchmarks/ is ignored: it says nothing about HEAD.

Run it right after the integration run, from the commit the results are for:

    python3 tools/record-integration-run.py [--results TestResults/integration] [--doc docs/integration-status.md]

Opt-in databases (Snowflake, SAP HANA, InterBase, Access) get a row only once they have run.
"""
import argparse
import datetime
import pathlib
import re
import subprocess
import sys

ALWAYS_ON = ["Sqlite", "PostgreSql", "SqlServer", "MySql", "MariaDb", "Firebird", "CockroachDb", "DuckDB", "Oracle",
             "YugabyteDb", "TiDb", "FlatFile", "Db2", "Informix", "SybaseASE", "Spanner", "SingleStore"]
OPT_IN = ["Snowflake", "SapHana", "InterBase", "Access"]
TFMS = ["net8.0", "net10.0"]
FILE_RE = re.compile(r"summary-(net[\d.]+)-([A-Za-z0-9]+)\.md$")
WRITTEN_RE = re.compile(r"Written (\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)Z; (\d+) test results")
ROW_RE = re.compile(r"\|\s*([A-Za-z0-9]+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|")
CELL_RE = re.compile(r"(\d+) pass, (\d+) fail, (\d+) skip · (\d{4}-\d\d-\d\d \d\d:\d\d)Z · ([0-9a-f]{8}\+?)")


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, check=True).stdout.strip()


def head_label():
    sha = git("rev-parse", "--short=8", "HEAD")
    dirty = git("status", "--porcelain", "--untracked-files=no", "--", ".", ":(exclude)docs/integration-status.md")
    return sha + ("+" if dirty else "")


def last_code_change():
    """UTC time of the newest commit that touched anything but docs/ and benchmarks/ (as 'YYYY-MM-DD HH:MM')."""
    iso = git("log", "-1", "--format=%cI", "--", ".", ":(exclude)docs", ":(exclude)benchmarks")
    when = datetime.datetime.fromisoformat(iso).astimezone(datetime.timezone.utc)
    return when.strftime("%Y-%m-%d %H:%M")


def read_results(results):
    found = {}
    for path in sorted(results.glob("summary-*.md")):
        m = FILE_RE.search(path.name)
        if not m:
            continue
        tfm, db = m.groups()
        text = path.read_text()
        written = WRITTEN_RE.search(text)
        rows = [r for r in ROW_RE.findall(text) if r[0] == db]
        if not written or len(rows) != 1:
            continue  # a combined summary (several databases in one file) is not one database's result
        _, pas, fail, skip = rows[0]
        found[(db, tfm)] = (int(pas), int(fail), int(skip), written.group(1)[:16])
    return found


def read_doc(doc):
    cells = {}
    if not doc.exists():
        return cells
    for line in doc.read_text().splitlines():
        parts = [p.strip() for p in line.strip().strip("|").split("|")]
        if len(parts) == len(TFMS) + 1 and parts[0] in ALWAYS_ON + OPT_IN:
            for tfm, cell in zip(TFMS, parts[1:]):
                m = CELL_RE.fullmatch(cell)
                if m:
                    cells[(parts[0], tfm)] = (int(m[1]), int(m[2]), int(m[3]), m[4], m[5])
    return cells


def render(cells):
    out = [
        "# Integration run status",
        "",
        "When each database last passed `run-integration-tests.sh`, per framework, and against which commit.",
        "Maintained by `tools/record-integration-run.py`, which folds the latest `TestResults/integration/summary-*.md`",
        "files in right after a run; a database that was not run keeps its previous cell. A trailing `+` on a commit",
        "means the working tree had uncommitted changes when it was recorded.",
        "",
        "Cells read `pass, fail, skip · time of the run (UTC) · commit`. A skip is a capability skip (a test that does",
        "not apply to that database); the counts are that database's own tests only.",
        "",
        "Ready to release means every always-on row is green on the commit being released, and every opt-in database",
        "that was run for the release is green. Access runs only on Windows.",
        "",
        "| Database | " + " | ".join(TFMS) + " |",
        "|---|" + "|".join("---" for _ in TFMS) + "|",
    ]
    dbs = ALWAYS_ON + [d for d in OPT_IN if any((d, t) in cells for t in TFMS)]
    for db in dbs:
        row = []
        for tfm in TFMS:
            c = cells.get((db, tfm))
            row.append("not recorded" if c is None else f"{c[0]} pass, {c[1]} fail, {c[2]} skip · {c[3]}Z · {c[4]}")
        out.append(f"| {db} | " + " | ".join(row) + " |")
    missing = [d for d in OPT_IN if not any((d, t) in cells for t in TFMS)]
    if missing:
        out += ["", "Opt-in, never recorded: " + ", ".join(missing) + "."]
    failing = sorted(f"{db} {tfm}" for (db, tfm), c in cells.items() if c[1] > 0)
    out += ["", "Failing now: " + (", ".join(failing) if failing else "none") + "."]
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", default="TestResults/integration")
    ap.add_argument("--doc", default="docs/integration-status.md")
    args = ap.parse_args()
    results, doc = pathlib.Path(args.results), pathlib.Path(args.doc)
    if not results.is_dir():
        sys.exit(f"no results directory: {results}")

    label = head_label()
    cutoff = last_code_change()
    cells = read_doc(doc)
    updated = 0
    stale = []
    for key, (pas, fail, skip, written) in read_results(results).items():
        if written < cutoff:
            stale.append(f"{key[0]} {key[1]}")
            continue  # ran before the last code change, so it says nothing about this commit
        old = cells.get(key)
        if old is None or written > old[3]:
            cells[key] = (pas, fail, skip, written, label)
            updated += 1
    doc.write_text(render(cells))
    print(f"{doc}: {updated} cell(s) updated against {label}")
    if stale:
        print(f"skipped {len(stale)} result(s) older than the last code change ({cutoff}Z): " + ", ".join(sorted(stale)))


if __name__ == "__main__":
    main()
