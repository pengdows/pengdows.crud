#!/usr/bin/env python
"""Everything that still needs testing or verification, by database and type: writes docs/type-verification-worklist.md.

Built on audit_gaps.py's parsers: the documented status of every concept on all 23 databases
(docs/datatype-unified-crossref.md), the concept-to-probe-type mapping (reconciliation.csv plus audit_gaps.OVERRIDES), and
what each database tests or refuses (TypeFidelityDeclarations.cs, the cells in TypeFidelityCells.cs).

Each (database, concept) cell lands in at most one work category:

  1 NEW TEST       documented N, A, V, E or R, and no fidelity cell covers the concept: nothing tests it.
  2 SPELLING CHECK documented N or A, a cell exists, but the only record is "refused: <server text>" (the probe found no accepted
                   spelling) or the type round-trips only through a wider native column.
  3 VERSION CHECK  documented V (version-dependent), E (extension) or R (restricted): tested or refused on the test
                   environment's version only.
  4 CLASSIFY       documented ? (unverified): the workbook status is missing, whether or not a column round-trips.

Not listed: tested cells documented N or A (done), refused cells with a stated verdict (done), and cells documented X. Where a column
round-trips for an X concept the database has no native type and the test uses a substitute (an unsigned 16-bit value in INTEGER, a
year in SMALLINT, an IP address in VARCHAR(45)): that is the library's mapping working, not an error in the workbook.

Usage: python tools/datatype-probe/gen_verification_worklist.py [repo root]
"""
import importlib.util
import pathlib
import sys
from collections import Counter, defaultdict

ROOT = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[2]
sys.argv = [sys.argv[0], str(ROOT)]
_spec = importlib.util.spec_from_file_location("audit_gaps", ROOT / "tools" / "datatype-probe" / "audit_gaps.py")
ag = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ag)

CATEGORIES = [
    ("NEW TEST", "New test needed: no fidelity cell covers the concept"),
    ("SPELLING CHECK", "Spelling check: documented native or alias, but the probe found no accepted spelling (or only a wider column)"),
    ("VERSION CHECK", "Version, extension or restriction check: documented V, E or R"),
    ("CLASSIFY", "Classify first: the workbook marks it ? (unverified)"),
]
STATUS_MEANING = {"N": "native", "A": "alias", "V": "version-dependent", "E": "extension-dependent", "R": "restricted",
                  "X": "unsupported", "?": "unverified"}

KNOWN_OPEN = [
    ("Access", "Date/Time Extended (DATETIME2)",
     "Upstream ACE defect: stored correctly, but OLE DB reads it with the month one early and a read can crash the process. Refused on read "
     "with a typed DataMappingException. Cannot be made to pass; re-check if a newer ACE build is installed."),
    ("Access", "AutoNumber as Replication ID",
     "DAO refuses the AutoNumber attribute on a GUID field. Tested as a GUID column with a GenGUID() default; the true attribute is untested."),
    ("Access", "B13 identity column, B77 Hyperlink / Attachment / Calculated / OLE",
     "Listed under category 1 because no fidelity cell covers them, but dedicated live tests do: the COUNTER key round trip (InsertReturningTests) and "
     "AccessComplexTypeTests (hyperlink, attachment, calculated fields, multi-value, lookup, memo variants, limit values)."),
    ("All", "Dedicated tests outside the fidelity cells",
     "Some category 1 rows are covered by live tests that are not fidelity cells. Check these before writing a new test: "
     "PostgreSqlAdvancedTypeRoundTripTests, PostgreSqlIntervalRoundTripTests, PostgreSqlRangeRoundTripTests, DuckDbWideAndListTypeRoundTripTests, "
     "SqlServerOddTypeRoundTripTests, MySqlSpatialRoundTripTests, OracleIntervalRoundTripTests, PortableAdvancedTypeRoundTripTests, "
     "SmallAndUnsignedTypeRoundTripTests. A row they cover should be closed by adding the cell or by recording the test here."),
    ("Access", "Attachment bytes, append-only memo",
     "ACE refuses a SQL write of attachment bytes and of an append-only memo; both are pinned as typed failures, not stored values."),
    ("Snowflake, SapHana, InterBase, AuroraMySql, AuroraPostgreSql", "Generated matrix report",
     "They have declarations and run the fidelity test, but are absent from docs/type-representation-matrix.md, the probe's fidelity-matrix "
     "TSVs and reconciliation.csv, so their per-type results are not in the generated documents. Needs a probe run per database "
     "(opt-in databases: credentials, licence or an AWS account)."),
    ("FlatFile", "Workbook classification",
     "The workbook marks all 47 concepts unverified for pengdows.flatfile, so nothing about it is classified."),
    ("All", "reconciliation.csv",
     "Covers 14 databases and predates Db2, Informix, Access, Snowflake, SAP HANA, InterBase and the Aurora variants; their concepts are "
     "classified only from the unified workbook. Regenerate it."),
    ("All", "Probe candidate lists",
     "A '-' in docs/type-representation-matrix.md means no candidate spelling was accepted, not that the database lacks the type. "
     "Every category 2 row below is such a case: add the spelling that works, or record the server's refusal as by design or server."),
]


def main():
    matrix = ag.parse_status_matrix()
    to_semantic = ag.parse_concept_to_semantic()
    declared, refused = ag.parse_declarations()
    cells = ag.parse_cell_semantics()
    databases = sorted(set(ag.ABBREV.values()))

    items = defaultdict(lambda: defaultdict(list))  # db -> category -> rows
    for cid, (category, concept, statuses) in sorted(matrix.items()):
        semantic = to_semantic.get(cid) or ag.OVERRIDES.get(cid)
        overridden = cid in ag.OVERRIDES and cid not in to_semantic
        for db in databases:
            status = statuses.get(db, "?")
            has_cell = semantic is not None and semantic in cells
            outcome = None
            if has_cell:
                if semantic in declared.get(db, {}):
                    outcome = "tested"
                elif semantic in refused.get(db, {}):
                    outcome = "probe-refused" if ag.classify_refusal(refused[db][semantic]) == "probe" else "recorded"
                else:
                    outcome = "unrecorded"
            label = semantic + (" [override]" if overridden else "") if semantic else "none"

            def add(cat, note):
                items[db][cat].append((cid, category, concept, status, label, note))

            if status == "X":
                continue  # unsupported: where a column round-trips it is a substitute for a missing native type, not an error
            if status == "?":
                if not has_cell:
                    add("CLASSIFY", "no fidelity cell and no classification")
                elif outcome == "tested":
                    add("CLASSIFY", "exercised (a column round-trips); the workbook status is missing")
                elif outcome == "unrecorded":
                    add("CLASSIFY", "no declaration and no refusal recorded")
                else:
                    add("CLASSIFY", "refused by the server on the test environment; record the verdict")
                continue
            if not has_cell:
                add("NEW TEST", "no cell" + ("" if semantic is None else f" ({semantic} is not a fidelity cell)"))
                continue
            if status in ("V", "E", "R"):
                if outcome == "tested":
                    add("VERSION CHECK", f"{STATUS_MEANING[status]}; tested on the test environment's version only")
                elif outcome == "unrecorded":
                    add("VERSION CHECK", f"{STATUS_MEANING[status]}; no declaration and no refusal recorded")
                else:
                    add("VERSION CHECK", f"{STATUS_MEANING[status]}; refused on the test environment: confirm the prerequisite")
                continue
            # N or A
            if outcome == "probe-refused":
                add("SPELLING CHECK", refused[db][semantic].strip().strip('"')[:110])
            elif outcome == "unrecorded":
                add("SPELLING CHECK", "no declaration and no refusal recorded")
            elif outcome == "tested" and declared[db][semantic][1] == "(widened)":
                add("SPELLING CHECK", f"tested only through a wider column ({declared[db][semantic][0]}); the native type is untested")
            # otherwise tested natively, or a recorded verdict: nothing to do.

    totals = Counter()
    for db in databases:
        for cat, rows in items[db].items():
            totals[cat] += len(rows)

    out = []
    out.append("# Type testing and verification worklist")
    out.append("")
    out.append("Everything that still needs testing or verification, by database and type. Generated by")
    out.append("`tools/datatype-probe/gen_verification_worklist.py` from `docs/datatype-unified-crossref.md` (what each database documents),")
    out.append("`tools/datatype-probe/reconciliation.csv` (concept to probe type), `TypeFidelityCells.cs` (the cells the fidelity test has) and")
    out.append("`TypeFidelityDeclarations.cs` (what each database tests or refuses). Do not edit by hand: close an item in its source and re-run it.")
    out.append("")
    out.append("This is a worklist, not a verdict. A probe type marked *[override]* is the generator's judgement of which cell covers a concept")
    out.append("that `reconciliation.csv` never linked (`OVERRIDES` in `audit_gaps.py`), so a row may disappear once that mapping is confirmed.")
    out.append("A cell is *tested* when the database declares a column for its probe type and the fidelity test round-trips it on both frameworks.")
    out.append("")
    out.append("## Work categories")
    out.append("")
    out.append("| # | Category | Rows | What closes a row |")
    out.append("|---|---|---:|---|")
    closes = {
        "NEW TEST": "Add a fidelity cell (or a dedicated live test) and declare or refuse it per database.",
        "SPELLING CHECK": "Find the spelling the server accepts and declare it, or record the refusal as by design, server or upstream.",
        "VERSION CHECK": "Run the cell against another version or with the extension, or record why one version is enough.",
        "CLASSIFY": "Decide the workbook status (supported, or unsupported with the reason); then test it if supported.",
    }
    for i, (cat, title) in enumerate(CATEGORIES, 1):
        out.append(f"| {i} | {title} | {totals[cat]} | {closes[cat]} |")
    out.append(f"| | **Total** | **{sum(totals.values())}** | |")
    out.append("")
    out.append("## By database")
    out.append("")
    out.append("| Database | " + " | ".join(f"{i}" for i in range(1, 5)) + " | Total |")
    out.append("|---|" + "---:|" * 5)
    for db in databases:
        counts = [len(items[db].get(cat, [])) for cat, _ in CATEGORIES]
        out.append(f"| {db} | " + " | ".join(str(c) for c in counts) + f" | {sum(counts)} |")
    out.append("| **All** | " + " | ".join(str(totals[cat]) for cat, _ in CATEGORIES) + f" | {sum(totals.values())} |")
    out.append("")

    out.append("## Known open items (maintained by hand)")
    out.append("")
    out.append("| Database | Item | State |")
    out.append("|---|---|---|")
    for db, item, state in KNOWN_OPEN:
        out.append(f"| {db} | {item} | {state} |")
    out.append("")

    out.append("## Type conversions")
    out.append("")
    out.append("The standard is a conversion test and a fidelity test for every type on every database. Fidelity is what the rest of this document")
    out.append("tracks. For conversions, what exists:")
    out.append("")
    out.append("- **Unit, provider-independent:** `TypeCoercionCombinatorialMatrixTests` and its siblings convert a runtime value of one type to a target")
    out.append("  type (numeric widening and narrowing, string and numeric, and so on), with no database involved.")
    out.append("- **Live, a fixed set of columns:** `TypeHydrationTests` reads one entity with typical, zero/null and boundary rows on every database;")
    out.append("  `TypeRoundTripMatrixTests` and the fidelity test read each column back as its natural .NET type.")
    out.append("- **Not found:** a live matrix of each database's column type read as each of the other .NET types it can map to (an INTEGER column as")
    out.append("  long, decimal, double, string or an enum; a TEXT column as a number or a date; a NUMERIC as a float), checked per database. Until one")
    out.append("  exists, a conversion that works in the unit matrix is not shown to work on any given database's reader. This is the largest unverified")
    out.append("  area and has no per-database row below.")
    out.append("")

    out.append("## By type, across databases")
    out.append("")
    out.append("Categories 1 and 2 only (a type nothing tests, or whose spelling is unknown), grouped by concept.")
    out.append("")
    for cat, title in CATEGORIES[:2]:
        by_concept = defaultdict(list)
        for db in databases:
            for cid, category, concept, status, label, note in items[db].get(cat, []):
                by_concept[(cid, category, concept)].append(db)
        out.append(f"### {title}")
        out.append("")
        if not by_concept:
            out.append("None.")
            out.append("")
            continue
        out.append("| ID | Category | Concept | Databases |")
        out.append("|---|---|---|---|")
        for (cid, category, concept), dbs in sorted(by_concept.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            out.append(f"| {cid} | {category} | {concept} | {len(dbs)}: {', '.join(dbs)} |")
        out.append("")

    out.append("## By database, in full")
    out.append("")
    for db in databases:
        if not any(items[db].values()):
            out.append(f"### {db}")
            out.append("")
            out.append("Nothing outstanding against the documented statuses.")
            out.append("")
            continue
        out.append(f"### {db}")
        out.append("")
        for cat, title in CATEGORIES:
            rows = items[db].get(cat, [])
            if not rows:
                continue
            out.append(f"**{title}** ({len(rows)})")
            out.append("")
            out.append("| ID | Category | Concept | Doc | Probe type | Note |")
            out.append("|---|---|---|:--:|---|---|")
            for cid, category, concept, status, label, note in rows:
                out.append(f"| {cid} | {category} | {concept} | {status} | {label} | {note.replace('|', '/')} |")
            out.append("")

    (ROOT / "docs" / "type-verification-worklist.md").write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    print("wrote docs/type-verification-worklist.md: " + ", ".join(f"{cat} {totals[cat]}" for cat, _ in CATEGORIES)
          + f" (total {sum(totals.values())})")


if __name__ == "__main__":
    main()
