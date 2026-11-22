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
    ("DuckDB", "Extension downloads",
     "The IP address and CIDR fidelity cells install the inet extension (INSTALL inet, LOAD inet in prerequisites.json). DuckDB caches it after the first "
     "download, so a host with no network and an empty cache fails those two cells. The spatial extension loads the same way but is not used: "
     "DuckDB.NET cannot read its GEOMETRY type (refused by name, DuckDbUnreadableColumnRefusalTests)."),
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
     "The workbook marks the FlatFile concepts unverified. 42 of 47 are closed by FlatFileEngineTypeTests plus a fidelity cell; the rest have no fidelity cell and need a workbook decision."),
    ("All", "docs/type-representation-matrix.md is stale",
     "gen_representation_doc.py crashed at HEAD (its database list had grown past the matrix files; fixed: it now covers only databases with a "
     "fidelity-matrix TSV). Regenerating it then reports about 37 cells as open, because roughly 40 refusal reasons in gen_fidelity.py's "
     "DB_REFUSED do not start with a verdict word (by design, server or upstream): the 'probe:' and 'extension gap:' forms, and the 'native "
     "point/polygon is PostgreSQL's ... covered by' notes. Give each a verdict (or teach the generator the new forms), then regenerate. "
     "The committed report predates those entries and says 0 open."),
    ("All", "reconciliation.csv",
     "Covers 14 databases and predates Db2, Informix, Access, Snowflake, SAP HANA, InterBase and the Aurora variants; their concepts are "
     "classified only from the unified workbook. Regenerate it."),
    ("All", "Probe candidate lists",
     "A '-' in docs/type-representation-matrix.md means no candidate spelling was accepted, not that the database lacks the type. "
     "Every category 2 row below is such a case: add the spelling that works, or record the server's refusal as by design or server."),
]


# Rows closed with evidence: (database, concept id) -> (state, evidence). A closed row leaves its work category and is listed under
# "Verified". State is "covered" (a dedicated test exercises it), "verified" (checked live on the test host, and pinned where it can
# be) or "substitute only" (the cell tests a stand-in, because the native type is not available on the test host).
VERIFIED = {
    ("Sqlite", "B13"): ("covered", "AUTOINCREMENT identity: InsertReturningTests.CreateAsync_ReturningClause_PopulatesIdentityAcrossProviders (INSERT ... RETURNING)."),
    ("Sqlite", "B49"): ("substitute only", "rowid is a pseudo-column that is always the INTEGER PRIMARY KEY; the cell declares a column labelled ROWID. SqliteDeclaredTypeNameTests.Rowid_IsAPseudoColumn_ThatIsTheIntegerPrimaryKey."),
    ("Sqlite", "B53"): ("substitute only", "JSON is text with the json_* functions; the cell declares a column labelled JSON. SqliteDeclaredTypeNameTests.ExtensionTypeNames_AreLabels_AndValuesAreStoredAsGiven."),
    ("Sqlite", "B54"): ("verified", "JSONB is built in from SQLite 3.45 (this host: 3.50.4) and is a blob. SqliteDeclaredTypeNameTests.Jsonb_IsBuiltInFromSqlite345_AndIsABlob."),
    ("Sqlite", "B57"): ("substitute only", "Manifest typing: a value keeps the type it is given, whatever the declared name. SqliteDeclaredTypeNameTests.ExtensionTypeNames_AreLabels_AndValuesAreStoredAsGiven."),
    ("Sqlite", "B68"): ("verified", "SpatiaLite 4.3.0 (mod_spatialite NuGet package, win-x64 only) loaded for real: POINT and POLYGON geometry columns, text and WKB round trips, NULL. SqliteNativeExtensionTypeTests.SpatiaLite_*. The fidelity cell still round-trips text under the label POINT."),
    ("Sqlite", "B71"): ("verified", "sqlite-vec 0.1.9 (HiraokaHyperTools.sqlite-vec NuGet package, win/linux/osx) loaded for real: vec0 float32, int8 and bit vectors, nearest-neighbour MATCH, dimension check. SqliteNativeExtensionTypeTests.SqliteVec_*. The fidelity cell still round-trips text under the label VECTOR(3)."),
    ("DuckDB", "B51"): ("verified", "INET through the inet extension, tested natively by the IP address and CIDR fidelity cells: written through CAST(parameter AS INET), read from the struct DuckDB.NET returns (DuckDbInetReader, DuckDbInetColumnTests). The first run downloads the extension (INSTALL inet), so it needs network access."),
    ("PostgreSql", "B31"): ("verified", "citext (CREATE EXTENSION citext), live: stored as citext, compares without case, keeps the case it was given. PostgreSqlFamilyOddTypeRoundTripTests.Citext_*."),
    ("PostgreSql", "B70"): ("verified", "point, line, lseg, box, path, polygon and circle, live: each stored as its type (pg_typeof) and read back as its text form. PostgreSqlFamilyOddTypeRoundTripTests.GeometricPrimitive_*."),
    ("PostgreSql", "B59"): ("verified", "A CREATE TYPE composite column, live: round-trips its row text and its fields are addressable. PostgreSqlFamilyOddTypeRoundTripTests.Composite_*. Written and read as text; there is no CLR composite mapping."),
    ("YugabyteDb", "B31"): ("verified", "citext, live on YugabyteDB: same behavior as PostgreSQL. PostgreSqlFamilyOddTypeRoundTripTests.Citext_*."),
    ("YugabyteDb", "B70"): ("verified", "point, line, lseg, box, path, polygon and circle, live on YugabyteDB: same behavior as PostgreSQL. PostgreSqlFamilyOddTypeRoundTripTests.GeometricPrimitive_*."),
    ("YugabyteDb", "B59"): ("verified", "A CREATE TYPE composite column, live on YugabyteDB. PostgreSqlFamilyOddTypeRoundTripTests.Composite_*."),
    ("YugabyteDb", "B62"): ("verified", "An enum type, live on YugabyteDB: declaration order and an unknown label refused. PostgreSqlFamilyOddTypeRoundTripTests.Enum_*."),
    ("YugabyteDb", "B66"): ("verified", "A domain with a CHECK, live on YugabyteDB: the check is enforced, but the domain's numeric(5,2) scale is not applied to a cast (99.5 stays 99.5; PostgreSQL gives 99.50). PostgreSqlFamilyOddTypeRoundTripTests.Domain_*."),
    ("CockroachDb", "B70"): ("verified", "CockroachDB has no point, line, lseg, box, path, polygon or circle type (its spatial types are GEOMETRY and GEOGRAPHY): the server refuses each, pinned by PostgreSqlFamilyOddTypeRoundTripTests.GeometricPrimitive_*."),
    ("AuroraMySql", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("AuroraPostgreSql", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("CockroachDb", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("Db2", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("DuckDB", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("Firebird", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("Informix", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("MariaDb", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("MySql", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("Oracle", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("PostgreSql", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("SapHana", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("SingleStore", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("Snowflake", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("SqlServer", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("SybaseASE", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("TiDb", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("YugabyteDb", "B13"): ("covered", "The database's own generated-key declaration (IDENTITY, AUTO_INCREMENT, sequence default or generator, per its DDL in InsertReturningTests): InsertReturningTests creates rows without an id and reads the generated id back into the entity."),
    ("PostgreSql", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("CockroachDb", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("YugabyteDb", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("MySql", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("MariaDb", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("TiDb", "B14"): ("verified", "SERIAL as the column spelling, live: the server fills the id and the gateway reads it back; ids are distinct (and consecutive except on CockroachDB, whose unique_rowid() is not). SerialShorthandTests."),
    ("MySql", "B05"): ("verified", "MEDIUMINT and MEDIUMINT UNSIGNED, live: both bounds round-trip into an int and a value one past either bound is refused, not clamped. MySqlFamilyMediumIntTests."),
    ("MariaDb", "B05"): ("verified", "MEDIUMINT and MEDIUMINT UNSIGNED, live: both bounds round-trip into an int and a value one past either bound is refused, not clamped. MySqlFamilyMediumIntTests."),
    ("TiDb", "B05"): ("verified", "MEDIUMINT and MEDIUMINT UNSIGNED, live: both bounds round-trip into an int and a value one past either bound is refused, not clamped. MySqlFamilyMediumIntTests."),
    ("SqlServer", "B30"): ("verified", "sysname, live: nvarchar(128) NOT NULL; NULL and a 129-character value are refused. SqlServerAndOracleSpecialTypeTests.SqlServer_Sysname_*."),
    ("SqlServer", "A74"): ("verified", "sql_variant, live: each value keeps its base type (int, nvarchar, decimal) and NULL reads as NULL. SqlServerAndOracleSpecialTypeTests.SqlServer_SqlVariant_*."),
    ("SqlServer", "A77"): ("verified", "Table variable and cursor variable, live, in a batch. SqlServerAndOracleSpecialTypeTests.SqlServer_TableVariableAndCursorVariable_*."),
    ("SqlServer", "B75"): ("verified", "cursor and table are refused as column types, live. SqlServerAndOracleSpecialTypeTests.SqlServer_CursorAndTable_AreRefusedAsColumnTypes."),
    ("SqlServer", "B67"): ("covered", "The CLR user-defined types SQL Server ships (hierarchyid, geometry, geography): SqlServerHierarchyIdRoundTripTests, SqlServerSpatialRoundTripTests. A user-built CLR assembly type is not tested."),
    ("Oracle", "B59"): ("verified", "An object type column, live: attributes addressable, an oversized attribute refused. SqlServerAndOracleSpecialTypeTests.Oracle_ObjectType_*. Written and read through SQL, no CLR mapping."),
    ("Oracle", "A74"): ("verified", "ANYDATA keeps the type of the value it holds, and XMLTYPE round-trips a document and refuses malformed XML, live. XMLTYPE needs BASICFILE CLOB storage on the test image: the default SecureFile is refused in the SYSTEM tablespace (ORA-43853). SqlServerAndOracleSpecialTypeTests.Oracle_AnyData_*, Oracle_XmlType_*."),
    ("Oracle", "B67"): ("verified", "ANYDATA and XMLTYPE as the opaque extension base types, live. SqlServerAndOracleSpecialTypeTests.Oracle_AnyData_*, Oracle_XmlType_*."),
    ("Oracle", "A77"): ("verified", "A nested table column (TABLE OF NUMBER), live: a collection per row, queried with TABLE(). SqlServerAndOracleSpecialTypeTests.Oracle_NestedTable_*."),
    ("Oracle", "B75"): ("verified", "SYS_REFCURSOR is refused as a column type, live. SqlServerAndOracleSpecialTypeTests.Oracle_RefCursor_IsRefusedAsAColumnType."),
    ("SybaseASE", "B30"): ("verified", "sysname and longsysname, live: varchar aliases, NOT NULL honored. SybaseAndFirebirdSpecialTypeTests.Sybase_Sysname_*."),
    ("SybaseASE", "A74"): ("verified", "timestamp, live: an 8-byte value the server generates and changes on UPDATE. SybaseAndFirebirdSpecialTypeTests.Sybase_Timestamp_*."),
    ("SybaseASE", "A77"): ("verified", "A cursor, live, inside a stored procedure (ASE requires DECLARE CURSOR to be the only statement of a batch). There is no table variable: a #temp table is the alias. SybaseAndFirebirdSpecialTypeTests.Sybase_CursorInsideAProcedure_*."),
    ("Firebird", "B35"): ("verified", "BLOB SUB_TYPE 0 (binary), TEXT and a user-defined negative subtype, live: values round-trip and the subtype is recorded on the column's domain. SybaseAndFirebirdSpecialTypeTests.Firebird_BlobSubtypes_*."),
    ("Access", "B13"): ("covered", "COUNTER identity through SELECT @@IDENTITY: InsertReturningTests.CreateAsync_NonReturningProviders_InsertsSuccessfully."),
    ("Access", "B77"): ("covered", "AccessComplexTypeTests: hyperlink, attachment (name through SQL, bytes through DAO), calculated fields of every result type, OLE object (LONGBINARY in the catalog)."),
    ("Access", "B08"): ("verified", "Large Number needs a current ACE build (16.0.5320 refused it, this host accepts it): AccessComplexTypeTests.NativeLargeNumber_CanBeCreatedWithTheInstalledAceProvider, and the catalog's BIGINT full-range entry."),
    ("Access", "B63"): ("covered", "Multi-value fields of every element type: AccessComplexTypeTests.MultiValue_* (Byte, Integer, Long, Single, Double, Decimal, Text, GUID)."),
}


def main():
    matrix = ag.parse_status_matrix()
    to_semantic = ag.parse_concept_to_semantic()
    declared, refused = ag.parse_declarations()
    cells = ag.parse_cell_semantics()
    databases = sorted(set(ag.ABBREV.values()))

    items = defaultdict(lambda: defaultdict(list))  # db -> category -> rows
    verified = defaultdict(list)  # db -> closed rows
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
                if db == "FlatFile" and cat == "CLASSIFY" and outcome in ("tested", "recorded", "probe-refused"):
                    # The engine's type surface is pinned by FlatFileEngineTypeTests (21 CLR types, 45 accepted and 27 refused SQL spellings),
                    # and the fidelity cell round-trips the type or records the engine's own refusal.
                    if not any(v[0] == cid for v in verified[db]):
                        verified[db].append((cid, category, concept, status, "verified" if outcome == "tested" else "covered",
                                             "FlatFileEngineTypeTests plus the fidelity cell (" + outcome + ")."))
                    return
                if (db, cid) in VERIFIED:
                    if not any(v[0] == cid for v in verified[db]):
                        verified[db].append((cid, category, concept, status) + VERIFIED[(db, cid)])
                    return
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

    closed = sum(len(rows) for rows in verified.values())
    out.append("## Verified (closed with evidence)")
    out.append("")
    out.append(f"{closed} rows that would be work are closed: a dedicated test covers them, they were checked live on the test host, or the cell tests a")
    out.append("substitute because the native type is not available here (the native type is then still untested). They are not counted above.")
    out.append("")
    out.append("| Database | ID | Concept | State | Evidence |")
    out.append("|---|---|---|---|---|")
    for db in databases:
        for cid, category, concept, status, state, evidence in verified.get(db, []):
            out.append(f"| {db} | {cid} | {category}: {concept} | {state} | {evidence} |")
    out.append("")

    out.append("## Type conversions")
    out.append("")
    out.append("The standard is a conversion test and a fidelity test for every type on every database. Fidelity is what the rest of this document")
    out.append("tracks. For conversions, what exists:")
    out.append("")
    out.append("- **Unit, provider-independent:** `TypeCoercionCombinatorialMatrixTests` and its siblings convert a runtime value of one type to a target")
    out.append("  type (numeric widening and narrowing, string and numeric, and so on), with no database involved.")
    out.append("- **fakeDb, every sample on every emulated database:** `FakeDbSampleRoundTripTests` writes each CLR type sample through the library's own")
    out.append("  write path and reads it back on every `SupportedDatabase` product (about 13,000 cases). It proves the library's write and read are inverses")
    out.append("  per product; what a real server does with the stored value is the live matrix's job. A database's own unreadable types are asserted as the")
    out.append("  typed refusal (DuckDB's GEOMETRY).")
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
