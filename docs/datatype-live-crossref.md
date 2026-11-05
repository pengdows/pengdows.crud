# Live datatype cross-reference

What each database actually accepts for each semantic column type, measured against a live
server. This replaces the documentation-level `?` and `N` cells in the cross-reference workbooks
with results from the engines themselves.

## Method

`tools/datatype-probe/` runs against the testbed containers (the same ones the integration suite
uses). For each database it:

1. Starts the container and opens a `DatabaseContext` on it.
2. For every semantic type, tries each candidate column declaration in `candidates.json`:
   `CREATE TABLE prN (id INTEGER NOT NULL PRIMARY KEY, c <declaration>)`.
3. For a declaration the server accepts, runs `SELECT c FROM prN` and records the provider's own
   type name (`GetDataTypeName`) and CLR type (`GetFieldType`).
4. Drops the table and moves on.

`summarize.py` joins the results to the workbook's documented status and writes `crossref.md`
and `crossref.csv`. Each result row carries `accepted`, the `stage` that failed (`create` or
`select`), and the server's error text.

Candidates are the probe's own list of vendor spellings, plus every plain SQL spelling the
workbook gives (353 declarations in total). A workbook spelling that is only prose (for example
`CREATE TYPE ... AS ENUM`) is kept as a candidate, but a refusal of it means the probe didn't
create the prerequisite object, not that the engine lacks the type.

## Scope and limits

- **Databases:** 15 always-on containers: PostgreSql, SqlServer, Oracle, Firebird, CockroachDb,
  MariaDb, MySql, Sqlite, DuckDB, YugabyteDb, TiDb, SingleStore, SybaseASE, Spanner, FlatFile.
  **Not yet probed:** Db2 and Informix (their native-library bootstraps are `internal` to the
  testbed, so the probe can't call them yet), and the opt-in or platform-bound ones: Aurora
  PostgreSQL and Aurora MySQL (AWS), Snowflake (credentials), SAP HANA and InterBase (opt-in), and
  Access (Windows only).
- **78 semantic types** from the workbook.
- **Candidates are a generator, not an oracle.** A type reported as `—` means none of its
  candidates was accepted. It is not a claim that the engine lacks the type. Add candidates and
  rerun to test further.
- **Prerequisite types are not tested.** Domains, user-defined types and CLR UDTs need an object
  created first. The probe doesn't create them, so those cells read as refused.

## What "accepted" does and does not mean

- **Accepted is not the same type.** The server accepted `VARCHAR(45)` for an IP address on
  Spanner, `NUMERIC(38,0)` for a 128-bit integer on MariaDB, and `INTEGER[10]` on Spanner, which
  came back as an unbounded `bigint[]`. The provider's reported type (`providerType`) decides
  whether a declaration is a real match. Those are approximations, not native types.
- **SQLite accepts almost any type name.** Its type affinity takes any identifier, so its 76
  accepted declarations are not evidence of support. Treat SQLite results as affinity, not as type
  checks. Its CLR types are also unreliable: the zero-row metadata read gave `System.String` for
  `NUMBER(3,0)`.
- **Create and read are different stages.** DuckDB accepts `UNION(a INTEGER)` as a column, but the
  provider cannot read it back (`Unrecognised type Union`). The library cannot hydrate that column,
  so it is not supported even though DDL succeeds.
- **The CLR type is zero-row metadata.** It is what the provider reports before any row exists. It
  is not proof of how a stored value would be read; the round-trip matrix
  (`docs/type-support-matrix.md`, `TypeRoundTripMatrixTests`) is what proves that.

## Bug found and fixed in the probe during this run

The first run used `id INTEGER PRIMARY KEY`. Sybase ASE rejects a nullable primary key
(`Column 'id' is part of a primary key constraint and cannot be null`), so every Sybase candidate
failed on the key column and none reached the type. The probe now declares `NOT NULL` and every
database was rerun. The first run's Sybase count of zero was this bug, not a finding about Sybase.

A second bug was in `summarize.py`: for a refused cell it listed the refused candidates as
"accepted", which made SQL Server appear to accept `LINESTRING`. Fixed; only accepted candidates
are listed now.

## Coverage

- 77 of 78 semantic types have at least one accepted declaration on at least one database. The
  exception is user-defined scalar / object, which needs a created type.
- Accepted declarations per database (of 353 probed): SQLite 76 (not meaningful, see above),
  PostgreSql 51, YugabyteDb 51, CockroachDb 48, DuckDB 47, SqlServer 45, MariaDb 42, MySql 42,
  Oracle 41, SingleStore 39, TiDb 39, FlatFile 32, Firebird 31, SybaseASE 31, Spanner 23.

## Disagreements with the workbook

Compared with the workbook's documented status, the live run disagreed in three ways. The
`crossref.csv` file has a row for each.

1. **Workbook `?` resolved by the probe:** 248 cells. These are the cells the workbook could not
   answer, and most are now answered (accepted or refused).
2. **Workbook `N` that no candidate confirmed:** 30 cells. Most are prerequisite types (composite
   and user-defined types, which need `CREATE TYPE` first) or types the engine doesn't have
   (Sybase geometry). Two need a closer look: DuckDB `VARIANT` is refused on storage versions
   before 1.5.0. SingleStore refuses `GEOMETRY` outright (`try GEOGRAPHY or GEOGRAPHYPOINT`) and
   gives a syntax error for `POINT`, so its geometry `N` cells are not confirmed. The workbook's
   `N` for these is not confirmed.
3. **Workbook `X` that the probe accepts:** 7 cells. Classified by the reported `providerType`:
   - **Native match:** CockroachDB `PG_LSN` for WAL position (`pg_lsn`); Spanner identity and
     `SERIAL` for auto-increment (`bigint`).
   - **Approximation:** MariaDB `NUMERIC(38,0)` for a 128-bit integer (`DECIMAL`); MariaDB and
     DuckDB `JSON` for native binary JSON (MariaDB's `JSON` is an alias for a text type, and
     DuckDB's is text-based, so neither is binary JSON).
   - **String fallback:** Spanner `VARCHAR(45)` for an IP address (`character varying`).
   - **Unbounded, not fixed-length:** Spanner `INTEGER[10]` (reports `bigint[]`, no length).

The workbook also has 13 corrections that were marked "from knowledge" or low confidence (for
example InterBase `BIGINT` at 0.6 and SAP HANA's deprecated types at 0.7). The probe does not
cover InterBase or SAP HANA yet, so those corrections are still unverified.

## Matrix

Each cell is the first accepted declaration for that semantic type on that database. `—` means
no candidate was accepted. The full per-candidate result, including the provider's reported type
and the error text for each refused declaration, is in `crossref.csv`.

| semantic type | CockroachDb | DuckDB | Firebird | FlatFile | MariaDb | MySql | Oracle | PostgreSql | SingleStore | Spanner | SqlServer | Sqlite | SybaseASE | TiDb | YugabyteDb |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| signed 8-bit | SMALLINT | TINYINT | SMALLINT | SMALLINT | TINYINT | TINYINT | NUMBER(3,0) | SMALLINT | TINYINT | — | TINYINT | TINYINT | TINYINT | TINYINT | SMALLINT |
| signed 16-bit | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | INT8 | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT |
| signed 32-bit | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER |
| signed 64-bit | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | NUMBER(19,0) | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT |
| signed 128-bit | NUMERIC(38,0) | HUGEINT | INT128 | NUMERIC(38,0) | NUMERIC(38,0) | NUMERIC(38,0) | NUMBER(38,0) | NUMERIC(38,0) | NUMERIC(38,0) | — | NUMERIC(38,0) | HUGEINT | NUMERIC(38,0) | NUMERIC(38,0) | NUMERIC(38,0) |
| unsigned 8-bit | — | UTINYINT | — | — | TINYINT UNSIGNED | TINYINT UNSIGNED | — | — | TINYINT UNSIGNED | — | TINYINT | TINYINT UNSIGNED | TINYINT | TINYINT UNSIGNED | — |
| unsigned 16-bit | — | USMALLINT | — | — | SMALLINT UNSIGNED | SMALLINT UNSIGNED | — | — | SMALLINT UNSIGNED | — | — | SMALLINT UNSIGNED | UNSIGNED SMALLINT | SMALLINT UNSIGNED | — |
| unsigned 32-bit | — | UINTEGER | — | — | INT UNSIGNED | INT UNSIGNED | — | — | INT UNSIGNED | — | — | INT UNSIGNED | UNSIGNED INT | INT UNSIGNED | — |
| unsigned 64-bit | — | UBIGINT | — | — | BIGINT UNSIGNED | BIGINT UNSIGNED | — | — | BIGINT UNSIGNED | — | — | BIGINT UNSIGNED | UNSIGNED BIGINT | BIGINT UNSIGNED | — |
| unsigned 128-bit | — | UHUGEINT | — | — | — | — | — | — | — | — | — | UHUGEINT | — | — | — |
| arbitrary-size integer | NUMERIC(1000,0) | VARINT | — | NUMERIC(1000,0) | DECIMAL(65,0) | DECIMAL(65,0) | NUMBER | NUMERIC(1000,0) | DECIMAL(65,0) | — | — | NUMERIC(1000,0) | — | DECIMAL(65,0) | NUMERIC(1000,0) |
| auto-increment integer | INTEGER GENERATED BY DEFAULT AS IDENTITY | — | INTEGER GENERATED BY DEFAULT AS IDENTITY | — | SERIAL | SERIAL | INTEGER GENERATED BY DEFAULT AS IDENTITY | INTEGER GENERATED BY DEFAULT AS IDENTITY | — | INTEGER GENERATED BY DEFAULT AS IDENTITY | INTEGER IDENTITY | INT AUTO_INCREMENT | INTEGER IDENTITY | INT AUTO_INCREMENT | INTEGER GENERATED BY DEFAULT AS IDENTITY |
| fixed decimal | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | NUMERIC | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) |
| decimal floating point | — | — | DECFLOAT | — | — | — | — | — | — | — | — | DECFLOAT | — | — | — |
| binary float32 | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL |
| binary float64 | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION |
| money / currency | — | — | — | — | — | — | — | MONEY | — | — | MONEY | MONEY | MONEY | — | MONEY |
| bit / bitstring | BIT(8) | BIT(8) | — | — | BIT(8) | BIT(8) | — | BIT(8) | BIT(8) | — | BIT | BIT(8) | — | BIT(8) | BIT(8) |
| boolean | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BIT | BOOLEAN | — | BOOLEAN | BOOLEAN |
| fixed character | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | — | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) | CHAR(10) |
| varying character | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) |
| unbounded text | TEXT | TEXT | BLOB SUB_TYPE TEXT | CLOB | TEXT | TEXT | CLOB | TEXT | TEXT | TEXT | TEXT | CLOB | TEXT | TEXT | TEXT |
| national / Unicode character | — | NVARCHAR(100) | NCHAR(10) | NCHAR(10) | NVARCHAR(100) | NVARCHAR(100) | NCHAR(10) | NCHAR(10) | NVARCHAR(100) | — | NVARCHAR(100) | NVARCHAR(100) | NVARCHAR(100) | NVARCHAR(100) | NCHAR(10) |
| long text LOB | TEXT | TEXT | BLOB SUB_TYPE TEXT | CLOB | TEXT | TEXT | CLOB | TEXT | TEXT | TEXT | TEXT | CLOB | TEXT | TEXT | TEXT |
| enumeration | — | ENUM('a','b') | — | — | ENUM('a','b') | ENUM('a','b') | — | — | ENUM('a','b') | — | — | ENUM | — | ENUM('a','b') | — |
| set-valued enum | — | — | — | — | SET('a','b') | SET('a','b') | — | — | SET('a','b') | — | — | — | — | SET('a','b') | — |
| fixed binary | — | — | BINARY(16) | BINARY(16) | BINARY(16) | BINARY(16) | RAW(16) | — | BINARY(16) | — | BINARY(16) | BINARY(16) | BINARY(16) | BINARY(16) | — |
| varying binary | BYTEA | BYTEA | VARBINARY(16) | BLOB | VARBINARY(16) | VARBINARY(16) | RAW(16) | BYTEA | VARBINARY(16) | BYTEA | VARBINARY(16) | VARBINARY(16) | VARBINARY(16) | VARBINARY(16) | BYTEA |
| binary LOB | BLOB | BLOB | BLOB | BLOB | BLOB | BLOB | BLOB | BYTEA | BLOB | BYTEA | IMAGE | BLOB | IMAGE | BLOB | BYTEA |
| legacy long binary | — | — | — | — | TINYBLOB | TINYBLOB | LONG RAW | — | TINYBLOB | — | IMAGE | LONG RAW | IMAGE | TINYBLOB | — |
| rowversion / timestamp token | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | TIMESTAMP | — | ROWVERSION | ROWVERSION | — | TIMESTAMP | TIMESTAMP |
| calendar date | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE |
| time without zone | TIME(6) | TIME | TIME | TIME(6) | TIME(6) | TIME(6) | — | TIME(6) | TIME(6) | — | TIME(6) | TIME(6) | TIME | TIME(6) | TIME(6) |
| time with zone | TIME WITH TIME ZONE | TIME WITH TIME ZONE | TIME WITH TIME ZONE | TIME WITH TIME ZONE | — | — | — | TIME WITH TIME ZONE | — | — | — | TIME WITH TIME ZONE | — | — | TIME WITH TIME ZONE |
| timestamp without zone | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | — | DATETIME2(6) | TIMESTAMP(6) | DATETIME | TIMESTAMP(6) | TIMESTAMP(6) |
| timestamp with zone | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | — | — | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | — | TIMESTAMP WITH TIME ZONE | DATETIMEOFFSET | TIMESTAMP WITH TIME ZONE | — | — | TIMESTAMP WITH TIME ZONE |
| local-zone timestamp | — | — | — | — | — | — | TIMESTAMP WITH LOCAL TIME ZONE | — | — | — | — | TIMESTAMP WITH LOCAL TIME ZONE | — | — | — |
| second-precision datetime | DATE | DATETIME(0) | DATE | DATE | DATETIME(0) | DATETIME(0) | DATE | DATE | DATETIME(0) | DATE | SMALLDATETIME | DATETIME(0) | SMALLDATETIME | DATETIME(0) | DATE |
| year-only | SMALLINT | SMALLINT | SMALLINT | SMALLINT | YEAR | YEAR | SMALLINT | SMALLINT | YEAR | — | SMALLINT | YEAR | SMALLINT | YEAR | SMALLINT |
| year-month interval | INTERVAL YEAR TO MONTH | INTERVAL YEAR TO MONTH | — | — | — | — | INTERVAL YEAR TO MONTH | INTERVAL YEAR TO MONTH | — | — | — | INTERVAL | — | — | INTERVAL YEAR TO MONTH |
| day-time interval | INTERVAL DAY TO SECOND(6) | INTERVAL DAY TO SECOND | — | — | — | — | INTERVAL DAY TO SECOND(6) | INTERVAL DAY TO SECOND(6) | — | — | — | INTERVAL | — | — | INTERVAL DAY TO SECOND(6) |
| generic interval | INTERVAL | INTERVAL | — | — | — | — | — | INTERVAL | — | — | — | INTERVAL | — | — | INTERVAL |
| UUID / GUID | UUID | UUID | CHAR(36) | UUID | UUID | CHAR(36) | RAW(16) | UUID | CHAR(36) | UUID | UNIQUEIDENTIFIER | UUID | CHAR(36) | CHAR(36) | UUID |
| row identifier / ROWID | CHAR(18) | CHAR(18) | CHAR(18) | CHAR(18) | CHAR(18) | CHAR(18) | ROWID | CHAR(18) | CHAR(18) | — | CHAR(18) | ROWID | CHAR(18) | CHAR(18) | CHAR(18) |
| object identifier / OID | OID | OID | — | — | — | — | — | OID | — | — | — | OID | — | — | OID |
| transaction ID | — | — | — | — | — | — | — | XID | — | — | — | XID | — | — | XID |
| WAL / log position | PG_LSN | — | — | — | — | — | — | PG_LSN | — | — | — | PG_LSN | — | — | PG_LSN |
| JSON text / JSON value | JSON | JSON | — | JSON | JSON | JSON | CLOB CHECK (c IS JSON) | JSON | JSON | JSONB | JSON | JSON | — | JSON | JSON |
| native binary JSON | JSONB | JSON | — | JSON | JSON | JSON | — | JSONB | BSON | JSONB | JSON | JSONB | — | JSON | JSONB |
| XML | — | — | — | CLOB | — | — | CLOB | XML | — | — | XML | XML | — | — | XML |
| BSON | BYTEA | BYTEA | — | — | — | — | — | BYTEA | BSON | BYTEA | — | BSON | — | — | BYTEA |
| variant / dynamic value | — | — | — | — | — | — | ANYDATA | — | — | — | SQL_VARIANT | VARIANT | — | — | — |
| IP address | INET | INET | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | INET | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | INET | VARCHAR(45) | VARCHAR(45) | INET |
| IP network / CIDR | INET | INET | — | — | — | — | — | CIDR | — | — | — | CIDR | — | — | CIDR |
| MAC address | — | — | — | — | — | — | — | MACADDR | — | — | — | MACADDR | — | — | MACADDR |
| point geometry | GEOMETRY(POINT,4326) | — | — | — | POINT | POINT | POINT | POINT | — | — | GEOMETRY | POINT | — | — | POINT |
| line / linestring geometry | GEOMETRY(LINESTRING) | — | — | — | LINESTRING | LINESTRING | LINESTRING | — | — | — | GEOMETRY | LINESTRING | — | — | — |
| polygon geometry | GEOMETRY(POLYGON) | — | — | — | POLYGON | POLYGON | POLYGON | POLYGON | — | — | GEOMETRY | POLYGON | — | — | POLYGON |
| general geometry | GEOMETRY | — | — | — | GEOMETRY | GEOMETRY | GEOMETRY | — | — | — | GEOMETRY | GEOMETRY | — | — | — |
| geography / geodetic | GEOGRAPHY | — | — | — | — | — | ST_GEOMETRY | — | — | — | GEOGRAPHY | GEOGRAPHY | — | — | — |
| SQL array | INTEGER ARRAY | INTEGER ARRAY | — | — | — | — | — | INTEGER ARRAY | — | INTEGER ARRAY | — | INTEGER ARRAY | — | — | INTEGER ARRAY |
| fixed-length array | INTEGER ARRAY[10] | INTEGER ARRAY[10] | INTEGER[10] | — | — | — | — | INTEGER ARRAY[10] | — | INTEGER ARRAY[10] | — | INTEGER ARRAY[10] | — | — | INTEGER ARRAY[10] |
| list / multiset / set collection | — | — | — | — | — | — | — | — | — | — | — | INTEGER MULTISET | — | — | — |
| map / dictionary | — | MAP(VARCHAR(10),INTEGER) | — | — | — | — | — | — | — | — | — | HSTORE | — | — | — |
| struct / composite / row | — | ROW(a INTEGER) | — | — | — | — | — | — | — | — | — | STRUCT | — | — | — |
| tagged union / variant | — | — | — | — | — | — | — | — | — | — | SQL_VARIANT | SQL_VARIANT | — | — | — |
| range | — | — | — | — | — | — | — | INT4RANGE | — | — | — | INT4RANGE | — | — | INT4RANGE |
| multirange | — | — | — | — | — | — | — | INT4MULTIRANGE | — | — | — | INT4MULTIRANGE | — | — | INT4MULTIRANGE |
| user-defined enum / domain | — | — | — | — | — | — | — | — | — | — | — | ENUM | — | — | — |
| user-defined scalar / object | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — |
| full-text search vector / query | TSVECTOR | — | — | — | — | — | — | TSVECTOR | — | — | — | TSVECTOR | — | — | TSVECTOR |
| vector embedding | VECTOR(3) | — | — | — | — | — | — | — | VECTOR(3) | — | VECTOR(3) | VECTOR(3) | — | VECTOR(3) | — |
| hierarchical path | — | — | — | — | — | — | — | — | — | — | HIERARCHYID | HIERARCHYID | — | — | — |
| opaque / special database type | — | — | — | — | — | — | — | — | — | — | SQL_VARIANT | SQL_VARIANT | — | — | — |
| external-file reference | — | — | — | — | — | — | BFILE | — | — | — | — | BFILE | — | — | — |
| spatial raster / other extensions | — | — | — | — | — | — | — | — | — | — | — | RASTER | — | — | — |
| cursor / table variable | REFCURSOR | — | — | — | — | — | — | REFCURSOR | — | — | — | REFCURSOR | — | — | REFCURSOR |
| specialized security label | — | — | — | — | — | — | — | — | — | — | — | IDSSECURITYLABEL | — | — | — |

## Querying the results (SQLite)

`tools/datatype-probe/store_sqlite.py` loads the JSON-lines results and the workbook statuses into
`crossref.db`, rebuilt from scratch on each run. The JSON-lines files stay the source of truth; the
`.db` is generated and not committed.

```bash
python3 tools/datatype-probe/store_sqlite.py
sqlite3 tools/datatype-probe/crossref.db \
  "SELECT database, declaration, provider_type FROM accepted_declarations WHERE semantic_type = 'IP address';"
```

Tables and views:

- `probe_result`: one row per candidate declaration tried (`accepted`, `stage`, `provider_type`,
  `clr_type`, `error`).
- `workbook_status`: the workbook's documented status per (semantic type, database).
- `accepted_declarations`: the accepted rows only.
- `disagreements`: the cells where the live run and the workbook differ, with the reason.

Counts differ by grain: `accepted_declarations` has one row per accepted declaration (1,405), while
the matrix above counts one per type and database cell (638).
## Integration fidelity types

The full list the integration type-fidelity test covers (`TypeFidelityIntegrationTests`): every column, its CLR type and `DbType`, and for each database the declaration it was created with. Generated by `tools/datatype-probe/gen_fidelity.py`; the same rows are in the `fidelity_types` table of `crossref.db`.

Cell key: the declaration; `W:` a widened column (the database has no native type, so the next signed type the library binds is used); `—` unsupported (the server refused every candidate, reason in `fidelity_types.reason`).

| column | semantic type | CLR | DbType | PostgreSql | SqlServer | Oracle | Firebird | CockroachDb | MariaDb | MySql | Sqlite | DuckDB | YugabyteDb | TiDb | SingleStore | SybaseASE | Spanner | FlatFile |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| signed_8_bit | signed 8-bit | sbyte | SByte | SMALLINT | TINYINT | NUMBER(3,0) | SMALLINT | SMALLINT | TINYINT | TINYINT | TINYINT | TINYINT | SMALLINT | TINYINT | TINYINT | TINYINT | — | SMALLINT |
| signed_16_bit | signed 16-bit | short | Int16 | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | SMALLINT | INT8 | SMALLINT |
| signed_32_bit | signed 32-bit | int | Int32 | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER | INTEGER |
| signed_64_bit | signed 64-bit | long | Int64 | BIGINT | BIGINT | NUMBER(19,0) | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT | BIGINT |
| signed_128_bit | signed 128-bit | Int128 | Object | NUMERIC(38,0) | NUMERIC(38,0) | NUMBER(38,0) | INT128 | NUMERIC(38,0) | NUMERIC(38,0) | NUMERIC(38,0) | HUGEINT | HUGEINT | NUMERIC(38,0) | NUMERIC(38,0) | NUMERIC(38,0) | NUMERIC(38,0) | — | NUMERIC(38,0) |
| unsigned_8_bit | unsigned 8-bit | byte | Byte | W:SMALLINT | TINYINT | W:SMALLINT | W:SMALLINT | W:SMALLINT | TINYINT UNSIGNED | TINYINT UNSIGNED | TINYINT UNSIGNED | UTINYINT | W:SMALLINT | TINYINT UNSIGNED | TINYINT UNSIGNED | TINYINT | W:SMALLINT | W:SMALLINT |
| unsigned_16_bit | unsigned 16-bit | ushort | UInt16 | W:INTEGER | W:INTEGER | W:INTEGER | W:INTEGER | W:INTEGER | SMALLINT UNSIGNED | SMALLINT UNSIGNED | SMALLINT UNSIGNED | USMALLINT | W:INTEGER | SMALLINT UNSIGNED | SMALLINT UNSIGNED | UNSIGNED SMALLINT | W:INTEGER | W:INTEGER |
| unsigned_32_bit | unsigned 32-bit | uint | UInt32 | W:BIGINT | W:BIGINT | W:BIGINT | W:BIGINT | W:BIGINT | INT UNSIGNED | INT UNSIGNED | INT UNSIGNED | UINTEGER | W:BIGINT | INT UNSIGNED | INT UNSIGNED | UNSIGNED INT | W:BIGINT | W:BIGINT |
| unsigned_64_bit | unsigned 64-bit | ulong | UInt64 | W:NUMERIC(20,0) | W:NUMERIC(20,0) | W:NUMERIC(20,0) | W:NUMERIC(20,0) | W:NUMERIC(20,0) | BIGINT UNSIGNED | BIGINT UNSIGNED | BIGINT UNSIGNED | UBIGINT | W:NUMERIC(20,0) | BIGINT UNSIGNED | BIGINT UNSIGNED | UNSIGNED BIGINT | W:NUMERIC(20,0) | W:NUMERIC(20,0) |
| arbitrary_integer | arbitrary-size integer | BigInteger | Object | NUMERIC(1000,0) | NUMERIC(38,0) | NUMBER | NUMERIC(38,0) | NUMERIC(1000,0) | DECIMAL(65,0) | DECIMAL(65,0) | NUMERIC(1000,0) | VARINT | NUMERIC(1000,0) | DECIMAL(65,0) | DECIMAL(65,0) | NUMERIC(38,0) | — | NUMERIC(1000,0) |
| binary_float32 | binary float32 | float | Single | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL | REAL |
| binary_float64 | binary float64 | double | Double | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION | DOUBLE PRECISION |
| fixed_decimal | fixed decimal | decimal | Decimal | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | DECIMAL(18,4) | NUMERIC | DECIMAL(18,4) |
| boolean | boolean | bool | Boolean | BOOLEAN | BIT | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | BOOLEAN | — | BOOLEAN | BOOLEAN |
| bit_string | bit / bitstring | BitArray | Object | BIT VARYING(8) | BIT | — | — | BIT VARYING(8) | BIT(8) | BIT(8) | BIT VARYING(8) | BIT(8) | BIT VARYING(8) | BIT(8) | BIT(8) | — | — | — |
| varying_character | varying character | string | String | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) | VARCHAR(100) |
| unbounded_text | unbounded text | string | String | TEXT | TEXT | CLOB | BLOB SUB_TYPE TEXT | TEXT | TEXT | TEXT | CLOB | TEXT | TEXT | TEXT | TEXT | TEXT | TEXT | CLOB |
| uuid | UUID / GUID | Guid | Guid | UUID | UNIQUEIDENTIFIER | RAW(16) | CHAR(36) | UUID | UUID | CHAR(36) | UUID | UUID | UUID | CHAR(36) | CHAR(36) | CHAR(36) | UUID | UUID |
| calendar_date | calendar date | DateOnly | Date | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE | DATE |
| time_of_day | time without zone | TimeOnly | Time | TIME(6) | TIME(6) | — | TIME | TIME(6) | TIME(6) | TIME(6) | TIME(6) | TIME | TIME(6) | TIME(6) | TIME(6) | TIME | — | TIME(6) |
| time_span | time without zone | TimeSpan | Time | TIME(6) | TIME(6) | — | TIME | TIME(6) | TIME(6) | TIME(6) | TIME(6) | TIME | TIME(6) | TIME(6) | TIME(6) | TIME | — | TIME(6) |
| day_time_interval | day-time interval | IntervalDaySecond | Object | INTERVAL DAY TO SECOND(6) | — | INTERVAL DAY TO SECOND(6) | — | INTERVAL DAY TO SECOND(6) | — | — | INTERVAL | INTERVAL DAY TO SECOND | INTERVAL DAY TO SECOND(6) | — | — | — | — | — |
| timestamp | timestamp without zone | DateTime | DateTime2 | TIMESTAMP(6) | DATETIME2(6) | TIMESTAMP(6) | TIMESTAMP | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | TIMESTAMP(6) | DATETIME | — | TIMESTAMP(6) |
| timestamp_with_zone | timestamp with zone | DateTimeOffset | DateTimeOffset | TIMESTAMP WITH TIME ZONE | DATETIMEOFFSET | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | — | — | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE | — | — | — | TIMESTAMP WITH TIME ZONE | TIMESTAMP WITH TIME ZONE |
| binary | binary LOB | byte[] | Binary | BYTEA | IMAGE | BLOB | BLOB | BLOB | BLOB | BLOB | BLOB | BLOB | BYTEA | BLOB | BLOB | IMAGE | BYTEA | BLOB |
| json | JSON text / JSON value | JsonDocument | Object | JSON | JSON | CLOB CHECK (c IS JSON) | — | JSON | JSON | JSON | JSON | JSON | JSON | JSON | JSON | — | JSONB | JSON |
| ip_address | IP address | IPAddress | Object | INET | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | INET | VARCHAR(45) | VARCHAR(45) | INET | INET | INET | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) | VARCHAR(45) |
| int_array | SQL array | int[] | Object | INTEGER ARRAY | — | — | — | INTEGER ARRAY | — | — | INTEGER ARRAY | INTEGER ARRAY | INTEGER ARRAY | — | — | — | INTEGER ARRAY | — |

Totals: 350 declared, 31 widened, 39 unsupported, of 420 (type, database) cells.


## Reconciliation with the merged cross-reference

`docs/datatype-unified-crossref.md` (the merged workbook) was compared cell by cell with the live probe. Only the
15 always-on databases were compared; SQLite is excluded (its affinity accepts any name). Source:
`tools/datatype-probe/reconciliation.csv` and the `reconciliation` table in `crossref.db`.

| Category | Cells |
|---|---:|
| Agree: supported (workbook N/A/V/E, probe accepted) | 410 |
| Agree: unsupported (workbook X, probe refused) | 285 |
| Not checkable (workbook `?`) | 192 |
| Workbook supported, live refused every candidate | 60 |
| Workbook unsupported (X), live accepted | 103 |

**Workbook unsupported, live accepted (103).** The probe found a column the workbook calls unsupported, but it's
usually a different type from the one named. Examples from the probe's own records:

- signed 8-bit on PostgreSQL: `SMALLINT`, the next size up.
- signed 128-bit on PostgreSQL: `NUMERIC(38,0)`, which isn't a 128-bit integer.
- arbitrary-size integer on MySQL: `DECIMAL(38,0)`.
- year-only on PostgreSQL: `SMALLINT`.
- rowversion on MySQL: a plain `TIMESTAMP`, which is not a rowversion.
- UUID on Oracle: `CHAR(36)` or `RAW(16)`.
- IP address on Spanner: `VARCHAR(45)`.
- bit string on SQL Server: `BIT`, a single-bit type.

Verdict: the workbook is right that the native type is absent, and the probe is right that a column can be made. The
probe's `accepted` is not the same as `native`. These cells need a third state, *approximation*, with the reported
provider type recorded, rather than being counted as supported.

**Workbook supported, live refused (60).** These are mostly probe candidate gaps or prerequisites:

- Enumerations and user-defined domains on PostgreSQL, SQL Server, Oracle, Firebird and YugabyteDB need `CREATE TYPE`
  or `CREATE DOMAIN` first, which the probe doesn't create. Prerequisite, not absence.
- Point and line geometry on DuckDB need the spatial extension loaded. Prerequisite.
- Geodetic geography on MySQL is `GEOMETRY` with a geographic SRID, which no candidate spells. Candidate gap.
- Interval year–month on Spanner is `R` (query-only): the workbook is right that it can't be a column. Our probe is
  right too.
- SQL array on Firebird (fixed-dimension arrays) and cursor/table variable on SQL Server and Oracle are not
  column types in the sense the probe tests.

**Action items.** Add the MySQL geometry-with-SRID spelling to the geography candidates. Add a prerequisite step to
the probe for enumerations, domains and extension-based types, so they're created before they're tested. Add a
third status (*approximation*) to the probe's output. Keep the workbook's `R` and `E` codes as they are; they already
say "not a plain column".

## Version sweep: version-dependent types

The probe was run against every older image in the testbed's version matrix (`TestbedImageMatrix`,
`ParallelTestOrchestrator.cs`), in addition to the latest image in `results/`. Output per image:
`tools/datatype-probe/results-versions/<Database>__<tag>.jsonl`. Analysis: `version_compare.py`,
`version_dependence.csv`, `version_reconciliation.csv`.

Images swept: PostgreSQL 15.0 and 9.5; MySQL 8.0.36 and 5.7; MariaDB 11.4.12, 10.11.11, 10.4 and 10.2; SQL Server
2022, 2019 and 2017; CockroachDB v24.3.0 and v23.2.14; Firebird 4.0.5 and 3.0.9; TiDB v7.5.7; YugabyteDB
2.25.2.0-b359; Oracle 21c and 18c (slim). Not swept: Db2 and Citus/TimescaleDB/Fujitsu/Percona (forks or not in
the always-on set), Aurora, Snowflake, SAP HANA, InterBase, Access, Informix, and the file and embedded databases.

**Result.** 26 database-and-type pairs change with the version. Firebird is the most version-sensitive: 128-bit
integers, decimal floating point and binary types all appear only in 4.0 and later. Oracle 18c and 21c refuse
`BOOLEAN` (ORA-00902), which 23ai accepts. Oracle 18c and 21c also lack the geometry types. PostgreSQL 9.5 lacks
`MACADDR8`, and CockroachDB v23.2 refuses `VECTOR`.

**Reconciliation with the workbook's `V` cells** (`version_reconciliation.csv`):

| Verdict | Cells |
|---|---:|
| Version-dependent, confirmed by the sweep | 12 |
| No change across the tested versions | 9 |
| Not swept (no version matrix for that database) | 5 |

A `V` cell with no change across the tested versions is not evidence that it's version-independent. It may
mean the versions tested are on the same side of the change, or that the candidate spelling is missing. The
sweep covers only the testbed's own versions within each major line.

## Fidelity matrix: every type on every always-on database

Every fidelity type (72 cells, one per type, covering 71 of the 78 semantic types) is round-tripped on each of the 15
always-on databases: its own table, written and read through TableGateway with its own entity, compared with the value
written. The matrix is in `tools/datatype-probe/fidelity-matrix/<Database>.tsv`, and in the `fidelity_matrix` table of
`crossref.db`. Test: `TypeFidelityIntegrationTests.EveryFidelityType_RoundTripsOnEveryDatabase`. Generator:
`tools/datatype-probe/gen_fidelity.py`.

Totals across the 1,080 cells: **496 PASS, 418 UNSUPPORTED** (the server refused every candidate; the refusal is
recorded in `fidelity_types`), **166 FAIL**, and 0 UNRECORDED (every type either passes, is recorded as unsupported, or
fails).

| Database | PASS | UNSUPPORTED | FAIL |
|---|---:|---:|---:|
| PostgreSql | 39 | 18 | 15 |
| SqlServer | 35 | 25 | 12 |
| Oracle | 28 | 28 | 16 |
| Firebird | 25 | 36 | 11 |
| CockroachDb | 36 | 21 | 15 |
| MariaDb | 33 | 30 | 9 |
| MySql | 31 | 30 | 11 |
| Sqlite | 48 | 1 | 23 |
| DuckDB | 39 | 25 | 8 |
| YugabyteDb | 39 | 18 | 15 |
| TiDb | 31 | 33 | 8 |
| SingleStore | 34 | 32 | 6 |
| SybaseASE | 26 | 40 | 6 |
| Spanner | 21 | 46 | 5 |
| FlatFile | 31 | 35 | 6 |

The failures fall into a few groups:

- **Binding:** `BitArray`, `IPAddress`, `IntervalDaySecond`, `BigInteger` and `Int128` have no parameter mapping on some
  drivers (SQLite, SQL Server, MySQL family, Npgsql, Sybase). These are library bugs.
- **Geometry reads:** the value comes back as EWKB hex (`AQEAAAAA...`) rather than as the geometry, on several
  databases.
- **Datetime precision and zone:** `timestamp without zone`, `second-precision datetime` and `time with zone` lose
  precision or move instant on some databases.
- **Wide integers:** `arbitrary-size integer` and `signed 128-bit` fail on most databases that don't have a native type
  for them.
- **Native JSON and rowversion:** the binary forms don't round-trip as the text form the library writes.

No failure has been fixed yet. Each is a separate TDD cycle: a failing unit test first, then the fix.

**Update (fix cycle, PostgreSQL-family intervals).** The matrix now reads 502 PASS, 418 UNSUPPORTED, 160 FAIL across the
1,080 cells. PostgreSQL, YugabyteDB and CockroachDB pass all three interval cells (day-time, year-month, generic).
PostgreSQL and YugabyteDB send the Npgsql interval (months, days, microseconds) through the `PostgreSqlInterval` wire
format; CockroachDB keeps ISO 8601 text. The year-month read failed because Npgsql reports an INTERVAL YEAR TO MONTH
column as `interval(65535)`, and the typed interval reader matched only the bare name `interval`; so the read fell back to
`GetValue`, whose TimeSpan refuses months. The name comparison now ignores a type modifier (unit test
`NpgsqlTypedDataTypeNameTests`). fakeDb does not emulate Npgsql's modifier or its TimeSpan refusal, so the live matrix
cell is the check for this path.

**Update (geometry comparison).** The fidelity cell compared spatial values by their `ToString`, which is WKT for a value
written as text and base64 WKB for a value read back. The same geometry looked different, so MySQL, MariaDB, SQL Server and
CockroachDB showed FAIL for their geometry cells although they stored and returned it correctly. The cell now compares the
SRID and the geometry's WKT (`ExtendedWellKnownText.WellKnownTextOf`), and those four databases pass their geometry cells.
Still failing, each needing a design decision before a fix: PostgreSQL and YugabyteDB native `point` and `polygon` columns
(the library binds geometry as `bytea`, and the native types are not PostGIS geometry; the library has no mapping for them);
SQLite (no spatial mapping; keeping the SRID needs an EWKB blob); Oracle SDO_GEOMETRY (ORA-00932 on bind).

**Fidelity fix cycle (live matrix 588 PASS, 417 UNSUPPORTED, 75 FAIL of 1,080).** Each fix was test-first (unit test
red, then fix, then the live cell). The changes, grouped by what they decided:

- *Library fixes.* A converter-backed value with no mapping for a database is bound as its converter's output (text or
  binary); a library value object is bound as its canonical text; spatial values are excluded (their binary form drops the
  SRID). The converter registration now marks its type as one the dialect's parameter path reaches. New converters:
  `System.Net.IPAddress` (text, or the value on the Npgsql family). MAC addresses are text off the Npgsql family. CIDR is
  text on a database with no cidr type (CockroachDB), and an inet with a prefix reads as the CIDR it names. An interval
  is ISO 8601 text on SQLite. A `BitArray` on SQLite is its 0/1 text. Wide integers (BigInteger, Int128) are bound as their
  exact decimal text on SQL Server, Sybase, Oracle and the MySQL family (Firebird's driver binds them natively: TYPE-005).
  Verbose interval text scales hours, minutes and microseconds correctly (it had read them as seconds).
- *Reader gate.* The typed Npgsql interval reader applies only to an Npgsql reader: another provider's "interval" column
  (SQLite, DuckDB) was being cast to NpgsqlInterval. Npgsql reports an INTERVAL YEAR TO MONTH column as `interval(65535)`,
  so the type name is compared without its modifier.
- *Declarations (generator overrides, `gen_fidelity.py` DB_PREFER / ROWVERSION rule).* A column whose native type cannot
  hold the sample is replaced by the database's type that can: SQLite BIT, DECIMAL-wide and 128-bit integers are TEXT
  (SQLite's NUMERIC affinity turns "101" into 101 and a 39-digit integer into a REAL; HUGEINT and BIGINT UNSIGNED get
  64-bit integer affinity); a rowversion token is the database's VARBINARY (PostgreSQL, MySQL and others stored the 8 bytes
  as a date); a signed 8-bit value is SMALLINT on SQL Server and Sybase (TINYINT is unsigned) and BIGINT on Spanner; an
  enumeration sample is one of the declared names; DuckDB's address and network are VARCHAR (INET refuses text); a
  second-precision datetime is TIMESTAMP(0) / DATETIME2(0) / DATETIME, not DATE (which drops the time) or SMALLDATETIME
  (which rounds to the minute); a national string is VARCHAR / NVARCHAR2 (a fixed CHAR pads reads); a native binary JSON
  value is a JSON document (compared as one, as the database normalises its text).
- *Decisions still open (not fixed; each is a design choice, not a bug fix):* PostgreSQL's native `point`, `polygon`,
  `xml`, `tsvector`, `pg_lsn` and `xid` columns (a string or a ulong has no bind to these types without a column-type hint
  in the entity model; `xid` is a 32-bit counter, so a 2^32 sample cannot be held); PostgreSQL's `numeric` beyond decimal's
  range (Npgsql's decimal read overflows and GetString refuses numeric, so the exact digits need a text SELECT);
  `time with zone` (a TIME has no date, and DateTimeOffset carries one); SQL Server's `rowversion` (database-generated: the
  cell's explicit insert is refused); Oracle's SDO_GEOMETRY bind; SQLite's geometry and collections (no mapping); the MySQL
  family's fixed-width `BIT(n)` and SQL Server's single-bit `BIT` (a variable-length bit string needs a length-bearing
  representation); the PostgreSQL multirange write.

**Final state of this fix cycle (live matrix 597 PASS, 417 UNSUPPORTED, 66 FAIL of 1,080).** Later in the cycle: the
DuckDB interval is read through its own struct (its `GetFieldValue` of the bindings' `DuckDBInterval`, with `UInt64`
microseconds); a day-time interval on a database with no interval type is its ISO 8601 text (a TimeSpan was a time of day
on Access and SQL Server), and DuckDB, whose INTERVAL takes a TimeSpan, names that format (`IntervalWireFormat.DayTimeSpan`);
SQLite int arrays are comma-separated text and float vectors are float32 bytes, read back by the existing coercions; GUIDs
are the schema their dialect writes (Firebird `CHAR(16) CHARACTER SET OCTETS`, Sybase `BINARY(16)`, Oracle text); Oracle's
JSON column has no check constraint (the probe's check named a column that does not exist).

The 66 remaining FAIL cells, by cause:

- *Native types that need a column-type hint (design decision).* PostgreSQL and YugabyteDB `point`, `polygon`, `xml`,
  `tsvector`, `pg_lsn` (WAL position), `xid` (transaction ID, a 32-bit counter that cannot hold the 2^32 sample). A string
  or ulong binds to these only with an Npgsql type that the entity model cannot name today.
- *Exact digits beyond a driver's decimal (design decision).* `arbitrary-size integer` on PostgreSQL, YugabyteDB,
  CockroachDB, MySQL, MariaDB, TiDB, SingleStore, SQL Server, Oracle, FlatFile, Firebird and Sybase: Npgsql's decimal read
  overflows and refuses GetString on numeric, and the other drivers do not return the digits as text. A text SELECT for
  wide-integer targets would fix it, which changes the read SQL.
- *Zone-less time (design decision).* `time with zone` (PostgreSQL, YugabyteDB, CockroachDB, DuckDB, Firebird, FlatFile):
  a TIME has no date, and the library's DateTimeOffset carries one.
- *Geometry without a native type.* SQLite has no spatial type (its SRID needs an EWKB blob); Oracle's SDO_GEOMETRY does not
  bind (ORA-00932). The PostgreSQL point and polygon columns are the native types above.
- *Bit strings.* MySQL, MariaDB, TiDB and SingleStore have only fixed-width BIT(n); SQL Server's BIT holds one bit.
- *Database-generated or system values.* SQL Server `rowversion` (the insert of an explicit token is refused) and Oracle
  `ROWID` (a pseudo column, NullReferenceException on the cell).
- *Other.* `map` (SQLite; DuckDB's MAP maps no CLR dictionary), `list` (SQLite), `multirange` (PostgreSQL, YugabyteDB, and
  SQLite), `full-text` (tsvector, above), Oracle `ANYDATA` (variant), Oracle `BFILE` (needs BFILENAME), Firebird DECFLOAT
  (FbDecFloat binding), Firebird fixed and varying binary and the timestamp and rowversion reads (the driver returns a
  Guid for a 16-byte CHAR OCTETS), SingleStore BSON (binary JSON), FlatFile second-precision datetime and FlatFile's
  own types.

The fakeDb round-trip test (`FakeDbSampleRoundTripTests`) still reports 13 failures: seven spatial values
(SQL Server, Oracle, Snowflake, SingleStore) and six SAP HANA nullable and string arrays. The project rule is that fakeDb
gaps are fixed in fakeDb, not deferred, so these are open work, not accepted.

**Final state of the fix cycle (live matrix: 636 PASS, 444 UNSUPPORTED, 0 FAIL of 1,080).** At the start of the cycle the
matrix had 66 FAIL. 39 of them now pass on a real fix; 27 are UNSUPPORTED by a stated decision (below), not fixed.

*Fixes (each test-first, in the library or the fakeDb emulator):*
- Wide integers (BigInteger, Int128, UInt128) are selected as their text where the driver's decimal read overflows
  (`WideIntegerTextCast` per dialect) and written through CONVERT on Sybase ASE (`WideIntegerArgument`). Firebird binds a
  BigInteger as its text and keeps its INT128 typed (the driver refuses text in an INT128 column).
- SQLite: geometry and geography as EWKB in a BLOB (the SRID survives); int arrays as comma text; float vectors as float32
  bytes; a list, a map and a range array as text (`TextCollections`); an interval as ISO text.
- SAP HANA: a SMALLINT or REAL array element is named by a CAST in the ARRAY subquery (JSON_TABLE types neither).
- Spatial: EWKB hex text reads back with its SRID (Snowflake); SingleStore refuses a geometry (no GEOMETRY type, GEOGRAPHY is
  WGS 84 only), so the SRID is never dropped silently; Oracle geometry columns are SDO_GEOMETRY.
- PostgreSQL family: a multirange is sent as an array typed to the driver's range type and read back per range.
- TiDB: a vector is sent as its bracketed text and read from it. SingleStore's BSON column reads as its JSON document.
- Firebird: DateTime2 is sent as DateTime (no DateTime2 parameter in the driver); a 16-byte binary read as a Guid reads back
  as its stored bytes (the inverse of the dialect's byte order); varying binary and rowversion columns are VARBINARY(32)
  (the driver reads a 16-octet column as a Guid and refuses another length).
- DuckDB: an interval is read through its own struct (its `GetFieldValue` of `DuckDBInterval`, `UInt64` microseconds); a
  map is its JSON text.
- A plain null from a driver reads as a null string (it was a NullReferenceException in the mapper).

*Decisions (UNSUPPORTED, each with its reason in `gen_fidelity.py` DB_REFUSED; review these):*
- PostgreSQL and YugabyteDB native point and polygon, xml, tsvector, pg_lsn, xid: each needs a column-type hint or a cast
  the entity model does not carry (xid is a 32-bit counter, not a storable 64-bit value).
- Fixed-width bit columns (MySQL family BIT(n), SQL Server BIT): no length on read, so a bit string of another length cannot
  round-trip.
- Firebird DECFLOAT (the driver binds FbDecFloat, not a decimal), and a 30-digit integer on Firebird or FlatFile (both
  parse a numeric as decimal, 28 digits).
- FlatFile: its only date type is DATE (no time of day) and it has no zoned time type.
- Oracle: ANYDATA (built by its constructor functions) and BFILE (set by BFILENAME), neither bound as a value; ROWID (a
  pseudo-column; a ROWID column reads back NULL for the value written).

*Changed comparisons (test side, not the library; review):*
- A time with zone is compared by its UTC time of day: a TIME WITH TIME ZONE keeps no date, so an instant can never match.
- Spatial values compare their coordinates as numbers ("1" and "1.0" are the same ordinate).

*fakeDb emulation added (the store now evaluates the server statements these cells use):*
- SAP HANA's ARRAY(SELECT V FROM JSON_TABLE(...)) builds the driver's wire encoding (`HanaArrayWire`).
- SQL Server's geometry::STGeomFromWKB builds the native encoding the spatial reader decodes (`SqlServerSpatialWire`), and a
  spatial column reports its UDT (geometry or geography) to the reader, which then reads the stored bytes.
- Oracle's SDO_GEOMETRY write (SRID 0 is NULL) and its SDO_UTIL.TO_WKTGEOMETRY read (`FakeDataStoreOracleSpatialTests`).
- A SELECT's FROM is the statement's own (a scalar subquery's FROM DUAL no longer ends the select list).
- A NULL scalar is DBNull, not "no row" (the command had returned a constant 42 for a NULL cell).
- The fakeDb round-trip test passes: the 13 known gaps (SAP HANA arrays, spatial) are closed.

*Still open:* the unit suite is green (12,653 tests); the TenantTests race (DisposeAsync_RacingWithInFlightCreate) is
load-sensitive and passes alone. Documented refusals are decisions, so they are only as good as their reasons.

**Current state (2026-11-05, supersedes the counts above).** The live matrix is 1,125 cells (75 types, 15 databases):
**655 PASS, 5 FAIL, 465 UNSUPPORTED**. The per-cell verdicts, and the reason for each unsupported cell, are in
[`type-representation-matrix.md`](type-representation-matrix.md), generated from the probe results and the generator's
tables. The five FAIL cells are the time-with-zone cell on PostgreSQL, YugabyteDB, CockroachDB, Firebird and SQLite: each
returns the instant with its offset dropped (the offset is 2 hours when written and 0 when read). That is an open defect,
not a settled limit. Earlier sections above are the dated history of the cycle and are kept as written.

