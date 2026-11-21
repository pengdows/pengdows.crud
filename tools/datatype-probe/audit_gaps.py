#!/usr/bin/env python
"""Audit: for each database, which documented-supported types have no passing fidelity test.

Combines three sources and writes docs/type-audit-gaps.md:
  1. docs/datatype-unified-crossref.md : the documented status of every concept on all 23 databases
     (N native, A alias, V version-dependent, E extension, R restricted, X unsupported, ? unverified).
  2. tools/datatype-probe/reconciliation.csv : which unified concept is which probe semantic type (the 75 the
     fidelity test knows); a concept with no probe semantic has no test at all.
  3. pengdows.crud.IntegrationTests/Core/TypeFidelityDeclarations.cs : per database, the semantic types it
     declares a column for (tested) and the ones it records as refused, with the reason.

Gap A: documented supported (N or A), a probe semantic exists, but the database has no declaration, only a refusal
       "refused: <server text>" (the probe found no accepted spelling): the type may be untested because the probe's
       candidate list missed the spelling that works.
Gap B: documented supported (N or A) and no probe semantic: nothing tests it.
Conditional (V, E, R) and unverified (?) cells are counted but not listed as gaps.

Usage: python tools/datatype-probe/audit_gaps.py [repo root]
"""
import csv
import pathlib
import re
import sys
from collections import defaultdict

ROOT = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[2]

ABBREV = {
    "PG": "PostgreSql", "MSSQL": "SqlServer", "ORA": "Oracle", "FB": "Firebird", "CRDB": "CockroachDb",
    "MDB": "MariaDb", "MYSQL": "MySql", "SQLITE": "Sqlite", "DUCK": "DuckDB", "YB": "YugabyteDb", "TIDB": "TiDb",
    "SNOW": "Snowflake", "AUR-M": "AuroraMySql", "AUR-P": "AuroraPostgreSql", "DB2": "Db2", "FLAT": "FlatFile",
    "SS": "SingleStore", "ASE": "SybaseASE", "SPAN": "Spanner", "IDS": "Informix", "HANA": "SapHana",
    "IB": "InterBase", "ACE": "Access",
}


# Concepts reconciliation.csv never linked to a probe type, though a fidelity cell covers them under another name. This is a
# judgement of this audit, not data: each is flagged in the report, and a cell that is declared (tested) or refused on a database
# decides the outcome there exactly as for a reconciled concept.
OVERRIDES = {
    "B20": "boolean",
    "B27": "national / Unicode character",
    "B32": "fixed binary",
    "B36": "external-file reference",
    "B43": "timestamp with zone",
    "B45": "day-time interval",
    "B49": "row identifier / ROWID",
    "B50": "specialized security label",
    "B53": "JSON text / JSON value",
    "B60": "map / dictionary",
    "B61": "variant / dynamic value",
    "B62": "enumeration",
    "B63": "list / multiset / set collection",
    "B66": "user-defined enum / domain",
    "B69": "geography / geodetic",
}


def parse_status_matrix():
    """{concept_id: (category, concept, {database: status})}"""
    lines = (ROOT / "docs" / "datatype-unified-crossref.md").read_text(encoding="utf-8").splitlines()
    matrix = {}
    header = None
    for line in lines:
        if line.startswith("| ID | Category | Semantic concept |"):
            header = [c.strip() for c in line.strip().strip("|").split("|")][3:]
            continue
        if header and line.startswith("|---"):
            continue
        if header and line.startswith("| ") and re.match(r"\| [A-Z]\d+ \|", line):
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            cid, category, concept = cells[0], cells[1], cells[2]
            statuses = cells[3:]
            entry = matrix.setdefault(cid, (category, concept, {}))
            for abbr, status in zip(header, statuses):
                if abbr in ABBREV:
                    entry[2][ABBREV[abbr]] = status.strip("`")
        elif header and not line.startswith("|"):
            header = None
    return matrix


def parse_concept_to_semantic():
    mapping = {}
    with open(ROOT / "tools" / "datatype-probe" / "reconciliation.csv", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row["probe_semantic"]:
                mapping[row["unified_id"]] = row["probe_semantic"]
    return mapping


def parse_declarations():
    """({database: {semantic: declaration}}, {database: {semantic: reason}})"""
    text = (ROOT / "pengdows.crud.IntegrationTests" / "Core" / "TypeFidelityDeclarations.cs").read_text(encoding="utf-8")
    declared, refused = defaultdict(dict), defaultdict(dict)
    mode = db = None
    for line in text.splitlines():
        m = re.match(r"\s+\[SupportedDatabase\.(\w+)\] = new Dictionary<string, \(string Declaration", line)
        if m:
            mode, db = "decl", m.group(1)
            continue
        m = re.match(r"\s+\[SupportedDatabase\.(\w+)\] = new Dictionary<string, string>", line)
        if m:
            mode, db = "refused", m.group(1)
            continue
        if mode and re.match(r"\s{12}\},?\s*$", line):
            mode = db = None
            continue
        if mode:
            e = re.match(r'\s+\["([^"]+)"\] = (.*)', line)
            if e:
                (declared if mode == "decl" else refused)[db][e.group(1)] = e.group(2).strip().rstrip(",")
    return declared, refused


def parse_cell_semantics():
    """The probe types the fidelity test has a cell for (TypeFidelityCells.All)."""
    text = (ROOT / "pengdows.crud.IntegrationTests" / "Core" / "TypeFidelityCells.cs").read_text(encoding="utf-8")
    return set(re.findall(r'\("([^"]+)", "[a-z0-9_]+", typeof\(Cell_\w+\)\)', text))


def classify_refusal(reason):
    r = reason.strip().strip('"')
    if "validated against" in r and "documentation" in r:
        return "documented"  # a probe refusal confirmed against the vendor's own documentation
    for verdict in ("by design", "server", "upstream"):
        if r.startswith(f"refused: {verdict}:") or r.startswith(f"{verdict}:"):
            return verdict
    return "probe"  # "refused: <server text>": the probe found no accepted spelling


def main():
    matrix = parse_status_matrix()
    to_semantic = parse_concept_to_semantic()
    declared, refused = parse_declarations()
    cells = parse_cell_semantics()
    databases = sorted(set(ABBREV.values()))

    rows = {db: {"supported": 0, "tested": 0, "recorded": 0, "gapA": [], "gapB": [], "conditional": 0, "unverified": 0}
            for db in databases}
    for cid, (category, concept, statuses) in sorted(matrix.items()):
        semantic = to_semantic.get(cid) or OVERRIDES.get(cid)
        overridden = cid in OVERRIDES and cid not in to_semantic
        for db in databases:
            status = statuses.get(db, "?")
            r = rows[db]
            if status == "?":
                r["unverified"] += 1
                continue
            if status in ("V", "E", "R"):
                r["conditional"] += 1
                continue
            if status not in ("N", "A"):
                continue
            r["supported"] += 1
            if semantic is None:
                r["gapB"].append((cid, category, concept, status))
            elif semantic not in cells:
                # The probe knows the type but the fidelity test has no cell for it: nothing tests it.
                r["gapB"].append((cid, category, concept, status))
            elif semantic in declared.get(db, {}):
                r["tested"] += 1
            elif semantic in refused.get(db, {}):
                verdict = classify_refusal(refused[db][semantic])
                if verdict == "probe":
                    r["gapA"].append((cid, category, concept, status, semantic + (" [override]" if overridden else "")))
                else:
                    r["recorded"] += 1
            else:
                r["gapA"].append((cid, category, concept, status, semantic + " (no declaration, no refusal)" + (" [override]" if overridden else "")))

    out = []
    out.append("# Type audit: documented-supported types without a passing fidelity test")
    out.append("")
    out.append("Generated by `tools/datatype-probe/audit_gaps.py` from `docs/datatype-unified-crossref.md` (what each database documents),")
    out.append("`tools/datatype-probe/reconciliation.csv` (concept to probe type) and `TypeFidelityDeclarations.cs` (what is tested or refused).")
    out.append("Do not edit by hand: change the sources and re-run it.")
    out.append("")
    out.append("**Supported** is a cell the workbook marks N (native) or A (alias). **Tested**: the database declares a column for the concept's")
    out.append("probe type, so the fidelity test round-trips it. **Recorded**: a refusal with a stated verdict (by design, server, upstream, or a probe refusal validated against the vendor's documentation).")
    out.append("**Gap A**: supported, but the only record is `refused: <server text>`, meaning the probe found no accepted spelling; the type may be")
    out.append("untested because its working spelling was never a candidate. **Gap B**: supported, but the concept has no probe type or no fidelity cell, so nothing tests it.")
    out.append("V, E, R and ? cells are counted, not listed. A probe type marked *[override]* is this audit's judgement of which cell covers a")
    out.append("concept that `reconciliation.csv` never linked (`OVERRIDES` in the script), not data.")
    out.append("")
    out.append("| Database | Supported | Tested | Recorded verdict | Gap A | Gap B | Conditional (V/E/R) | Unverified (?) |")
    out.append("|---|---:|---:|---:|---:|---:|---:|---:|")
    for db in databases:
        r = rows[db]
        out.append(f"| {db} | {r['supported']} | {r['tested']} | {r['recorded']} | {len(r['gapA'])} | {len(r['gapB'])} | {r['conditional']} | {r['unverified']} |")
    total = lambda k: sum((len(rows[d][k]) if isinstance(rows[d][k], list) else rows[d][k]) for d in databases)
    out.append(f"| **All** | {total('supported')} | {total('tested')} | {total('recorded')} | {total('gapA')} | {total('gapB')} | {total('conditional')} | {total('unverified')} |")
    out.append("")
    for db in databases:
        r = rows[db]
        if not r["gapA"] and not r["gapB"]:
            continue
        out.append(f"## {db}")
        out.append("")
        if r["gapA"]:
            out.append("**Gap A** (supported; the probe found no accepted spelling):")
            out.append("")
            for cid, category, concept, status, semantic in r["gapA"]:
                out.append(f"- `{cid}` {category}: {concept} ({status}), probe type *{semantic}*")
            out.append("")
        if r["gapB"]:
            out.append("**Gap B** (supported; no probe type or no fidelity cell, so nothing tests it):")
            out.append("")
            for cid, category, concept, status in r["gapB"]:
                out.append(f"- `{cid}` {category}: {concept} ({status})")
            out.append("")
    (ROOT / "docs" / "type-audit-gaps.md").write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote docs/type-audit-gaps.md: {total('supported')} supported cells, {total('tested')} tested, "
          f"{total('recorded')} recorded, gap A {total('gapA')}, gap B {total('gapB')}")


if __name__ == "__main__":
    main()
