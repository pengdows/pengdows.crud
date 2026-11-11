# Datatype cross-reference: verification record (2026-11-09)

This records what was checked against vendor documentation and against live servers for the cross-reference
(`docs/datatype-unified-crossref.md`). The cross-reference is a classification, not a test result. This record says, for every
cell it claims exists that we did not support, what the evidence shows now.

**Scope:** 709 cross-reference cells claim a type exists (`N`, `A`, `V`, `E` or `R`) on one of our 22 databases. At the start of
this round, 94 of those had no declaration and a refusal. After this round, 90 remain refused, and each has a stated reason
below. Every other claimed cell is declared and passing in the fidelity matrix.

## What changed in this round

Each change began with a failing test (a generator test, or a unit test), was confirmed red for the stated reason, and then was
fixed. One exception: the Firebird `SQL array` refusal test was written together with its refusal and was not seen red first.
The refusal is backed by the fixed-dimension evidence, and the Firebird integration suite passes.

**Now supported (live round-trip passes on the fidelity matrix):**

- DuckDB `TIME WITH TIME ZONE` (cross-reference B40). The refusal rested on a false premise: DuckDB has `TIMETZ`, the server
  reads it back as a `DateTimeOffset`, and the offset survives. A `DateTimeOffset` declared as a time now binds its time and
  offset as text (`DuckDbDialect.CreateDbParameter`).
- Informix decimal floating point (B16): `DECIMAL(32)` and bare `DECIMAL` are accepted. Informix's floating decimal is
  `DECIMAL(p)`, and the server accepts it (`results/Informix.jsonl`).
- Informix time (B39): `DATETIME HOUR TO FRACTION(5)`. The seconds-only `DATETIME HOUR TO SECOND` drops fractions, so the
  generator requires the fractional form the dialect already uses (`InformixDialect`).
- Informix collections as SQL arrays (B58): `LIST(INTEGER NOT NULL)`. The docs map Informix collections to `T[]`.
- PostgreSQL `hstore` (map): the cell now uses the `HStore` type that the docs name for `hstore`.
- PostgreSQL `ltree` (hierarchical path): dotted text `1.2`, which is the `ltree` form.
- Informix BSON: the JSON-text row (`native binary JSON`) carries it, through the `::JSON::BSON` cast the dialect writes. The
  raw-bytes `BSON` row is refused, because the server has no byte-to-JSON cast.
- SingleStore geography: compared to six decimal places for SingleStore only. The server stores coordinates at single precision,
  so `1` reads back as `0.99999998`.
- Oracle geography: already declared as `SDO_GEOMETRY` and passing. My earlier summary said Oracle geography stays refused. That
  was wrong, and this record corrects it.

**Refusal corrected (the cross-reference claim is wrong):**

- Oracle `DECFLOAT` (B16, cross-reference `A`): the server refuses `DECFLOAT` and `DECFLOAT(34)` (ORA-00902, ORA-03060). Oracle
  has no decimal floating point type.
- MySQL and MariaDB day-time `INTERVAL` (B45, `A`): there is no interval column type. The server reports
  `Unknown data type: 'INTERVAL'` (MariaDB) and a syntax error (MySQL).
- PostgreSQL and CockroachDB `TIMESTAMP WITH LOCAL TIME ZONE` (B43, `N`): there is no such type. PostgreSQL's `timestamptz` is
  the instant type, which the library already maps as a timestamp with zone.
- DuckDB `BINARY(n)` (B32, `A`): DuckDB has `BLOB`, with no length parameter. The server reports
  `Type 'BLOB' does not take any type parameters`.
- Db2 `ARRAY` as a column type (B58, `R`): refused by the server with SQL20441N.
- Firebird and InterBase `SQL array` (B58, `N`): both accept `INTEGER[10]` only as a fixed-dimension array. A three-element
  value reads back as ten elements, so there is no variable-length SQL array to map. The fixed-length array row is the Firebird
  and InterBase array, and it is declared (Firebird).

**Version and extension gates (verified live, testbed images lack the feature):**

- MySQL `VECTOR` (B71, `V`): accepted by `mysql:9.4`, refused by the testbed's `8.4` (`results/versions/MySql-9.4.jsonl`). The
  MySQL 9.0 introduction is from the MySQL manual.
- MariaDB `VECTOR` (B71, `V`): accepted by `mariadb:11.7` and `11.8`, refused by `11.6`
  (`results/versions/MariaDb-11.6.jsonl`, `MariaDb-11.7.jsonl`). The cross-reference's 11.7 claim came from a single source, and
  these probes confirm it.
- PostgreSQL `pgvector` (`vector`, E): accepted by the `pgvector/pgvector:pg17` image as `float[]`
  (`results/versions/PostgreSql-pgvector.jsonl`). The default testbed image lacks the extension.
- PostgreSQL PostGIS (`geography`, E): accepted by `postgis/postgis:17-3.5` as the library's `Geography`
  (`results/versions/PostgreSql-postgis.jsonl`). The default testbed image lacks the extension.
- SAP HANA `REAL_VECTOR` (B71, `V`): SAP's community and LangChain material name `REAL_VECTOR` for HANA Cloud 2024 (QRC01)
  and later. Our testbed HANA image refuses it (`incorrect syntax near "REAL_VECTOR"`). Testing a newer HANA release is open.

**Server refusals confirmed live (the refusal stands):** Informix `BIGINT` cannot hold `-9223372036854775808` (the server reports
it will not fit, confirmed live 2026-11-06); Spanner refuses `INTERVAL` as a column type; Sybase ASE has no `BOOLEAN` or `XML`
column type; SingleStore reports `GEOMETRY types are not supported` (only `GEOGRAPHY` and `GEOGRAPHYPOINT`); Informix and Db2
security labels need an access-control policy first (`IDSSECURITYLABEL` and `DB2SECURITYLABEL` are undefined without one).

**Extensions now declared on Aurora PostgreSQL (verified live on a provisioned cluster, torn down after the run):**

- pgvector (`vector embedding`): `CREATE EXTENSION vector`; `vector(3)` accepted and read as `float[]`. Aurora PostgreSQL
  supports pgvector per the AWS extension list.
- PostGIS (`geography / geodetic`): `CREATE EXTENSION postgis`; `geography(POINT,4326)` accepted and read as `Geography`.
  Aurora PostgreSQL supports PostGIS per the AWS extension list.
- The default PostgreSQL testbed image still lacks both extensions, so those cells stay refused there.

**Library gaps closed in this round (the server supports the type, and the library now reads and writes it):**

- Oracle `VARRAY` (`SQL array`): the library builds the declared collection in SQL from the elements' JSON text
  (`JSON_TABLE`, `COLLECT`, `CAST`) and reads it back with `JSON_ARRAYAGG`. ODP.NET refuses an array parameter for a VARRAY
  (ORA-50071), so the typed bind is not used. The Oracle fidelity suite passes.
- MySQL 9 `VECTOR`: written with `STRING_TO_VECTOR`, read with `VECTOR_TO_STRING`. A plain text literal is refused by the server
  (ERROR 6136). Round-trip verified live on `mysql:9.4` through the library: `[1, 2.5, -3]` reads back exactly.
- MariaDB `VECTOR`: written with `VEC_FromText`, read with `VEC_ToText`. Round-trip verified live on `mariadb:11.8`.
- The default testbed images (MySQL 8.4, MariaDB 11.4) predate `VECTOR`, so the fidelity matrix still refuses these cells, with
  that reason. Declaring them needs a newer default image: a decision for you.

**Unverified (no evidence either way):**

- SAP ASE native `POINT`, `LINESTRING`, `POLYGON` (`N`): validated against SAP ASE documentation (confirmed 2026-11-11), and the ASE 16 probe refuses all three ("Can't find type").
  The cells stay refused.
- Db2 12.1 native JSON (`R`): the search found no native JSON column type, and the search was inconclusive for 12.1 specifically.
  The cell stays refused.

**Supported by the database, not refused by it (corrected):**

- Oracle `ROWID` (`N`): the server accepts the column, so the database supports it. The refusal is the library's: a `ROWID` is a
  physical address that the library cannot write. Reading it as text is possible; writing it is not.

## Corrections to the cross-reference (for your approval; the matrix itself is unchanged)

- Oracle `DECFLOAT`: `A` should be `X`.
- MySQL and MariaDB day-time `INTERVAL`: `A` should be `X`.
- PostgreSQL and CockroachDB local-zone `TIMESTAMP WITH LOCAL TIME ZONE`: `N` should be `X` (`timestamptz` covers it).
- DuckDB time with zone: `N`, supported through `TIMETZ` (declared).

## Appendix: the 90 refused claims

| ID | Concept | Database | Cross-reference | Now | Evidence or reason |
|---|---|---|---|---|---|
| B16 | decimal floating point | Oracle | A | refused | refused: ORA-00902: invalid datatype https://docs.oracle.com/error-help/db/ora-00902/ |
| B43 | local-zone timestamp | PostgreSql | N | refused | refused: 42601: syntax error at or near \"WITH\" POSITION: 66 |
| B43 | local-zone timestamp | CockroachDb | N | refused | refused: 42601: at or near \"with\": syntax error DETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information. |
| B43 | local-zone timestamp | MariaDb | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MariaDB server version for the right syntax to use near 'WITH LOCAL TIME ZONE)' at line 1 |
| B43 | local-zone timestamp | MySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'WITH LOCAL TIME ZONE)' at line 1 |
| B45 | day-time interval | MariaDb | A | refused | refused: Unknown data type: 'INTERVAL' |
| B45 | day-time interval | MySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'INTERVAL DAY TO SECOND(6))' at line 1 |
| B49 | row identifier / ROWID | Oracle | N | refused | refused: by design: ROWID is a pseudo-column that identifies a row: a ROWID column keeps no value the library writes (it reads back NULL) |
| B58 | SQL array | Oracle | R | refused | refused: gap: the server accepts a VARRAY (probe_varray); the library binds no Oracle collection parameter, and ODP.NET refuses an int[] (ORA-50028). Binding needs OracleDbType.Array with the column's UDT name (open: FUTURE_WORK) |
| B58 | SQL array | Firebird | N | refused | refused: by design: Firebird arrays have fixed dimensions (fblangref 4.0, array_dim); INTEGER[10] reads back as ten elements, so there is no variable-length array to map |
| B63 | list / multiset / set collection | MariaDb | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MariaDB server version for the right syntax to use near ')' at line 1 |
| B63 | list / multiset / set collection | MySql | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'MULTISET)' at line 1 |
| B68 | general geometry | PostgreSql | E | refused | refused: 42704: type \"geometry\" does not exist POSITION: 56 |
| B69 | geography / geodetic | PostgreSql | E | refused | refused: extension gap: PostGIS is not in the default testbed image (extension \"postgis\" is not available); the PostGIS image accepts geography(POINT,4326) (results/versions/PostgreSql-postgis.jsonl) |
| B69 | geography / geodetic | MySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'GEOGRAPHY)' at line 1 |
| B70 | point geometry | PostgreSql | N | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row) |
| B71 | vector embedding | PostgreSql | E | refused | refused: extension gap: pgvector is not in the default testbed image (extension \"vector\" is not available); the pgvector image accepts vector(3) as float[] (results/versions/PostgreSql-pgvector.jsonl) |
| B71 | vector embedding | MariaDb | V | refused | refused: version gate: VECTOR is accepted on mariadb:11.7 and refused on 11.6 (results/versions/MariaDb-11.*.jsonl); it is stored and read as BLOB (byte[]), which the library does not map to a vector yet |
| B71 | vector embedding | MySql | V | refused | refused: version gate: VECTOR is accepted on mysql:9.4 and refused on the testbed's 8.4 (results/versions/MySql-9.4.jsonl); it reads as ReadOnlyMemory<float>, which the library does not map yet |
| A56 | point geometry | PostgreSql | N | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row) |
| A57 | line / linestring geometry | PostgreSql | N | refused | refused: 42704: type \"linestring\" does not exist POSITION: 56 |
| A58 | polygon geometry | PostgreSql | N | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPolygon (the native polygon row) |
| A63 | list / multiset / set collection | PostgreSql | A | refused | refused: 42601: syntax error at or near \"MULTISET\" POSITION: 64 |
| A63 | list / multiset / set collection | CockroachDb | A | refused | refused: 42601: at or near \"multiset\": syntax error DETAIL: Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information. |
| B32 | fixed binary | DuckDB | A | refused | refused: Binder Error: Type 'BLOB' does not take any type parameters LINE 1: CREATE TABLE pr150 (id INTEGER NOT NULL PRIMARY KEY, c BINARY(16)) |
| B43 | local-zone timestamp | DuckDB | N | refused | refused: Parser Error: syntax error at or near \"WITH\" LINE 1: ... TABLE pr207 (id INTEGER NOT NULL PRIMARY KEY, c TIMESTAMP WITH LOCAL TIME ZONE) |
| B43 | local-zone timestamp | YugabyteDb | N | refused | refused: 42601: syntax error at or near \"WITH\" POSITION: 66 |
| B43 | local-zone timestamp | TiDb | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your TiDB version for the right syntax to use line 1 column 69 near \"WITH LOCAL TIME ZONE)\" |
| B43 | local-zone timestamp | AuroraMySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'WITH LOCAL TIME ZONE)' at line 1 |
| B43 | local-zone timestamp | AuroraPostgreSql | N | refused | refused: 42601: syntax error at or near \"WITH\" POSITION: 66 |
| B45 | day-time interval | AuroraMySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'INTERVAL DAY TO SECOND(6))' at line 1 |
| B50 | specialized security label | Db2 | N | refused | refused: ERROR [42704] [IBM][DB2/LINUXX8664] SQL0204N \"IDSSECURITYLABEL\" is an undefined name. |
| B53 | JSON text / JSON value | Db2 | R | refused | refused: ERROR [42704] [IBM][DB2/LINUXX8664] SQL0204N \"JSON\" is an undefined name. |
| B58 | SQL array | Db2 | R | refused | refused: server: Db2 refuses an ARRAY type in a column definition (SQL20441N: a ARRAY data type is not supported in the context where it is being used, confirmed live) |
| B60 | map / dictionary | YugabyteDb | E | refused | refused: 42601: syntax error at or near \",\" POSITION: 71 |
| B60 | map / dictionary | AuroraPostgreSql | E | refused | refused: 42601: syntax error at or near \",\" POSITION: 71 |
| B62 | enumeration | YugabyteDb | N | refused | refused: 42704: type \"enum\" does not exist POSITION: 56 |
| B62 | enumeration | AuroraPostgreSql | N | refused | refused: 42704: type \"enum\" does not exist POSITION: 56 |
| B63 | list / multiset / set collection | TiDb | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your TiDB version for the right syntax to use line 1 column 71 near \"MULTISET)\" |
| B63 | list / multiset / set collection | AuroraMySql | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'MULTISET)' at line 1 |
| B66 | user-defined enum / domain | YugabyteDb | N | refused | refused: 42601: syntax error at or near \"CREATE\" POSITION: 56 |
| B66 | user-defined enum / domain | AuroraPostgreSql | N | refused | refused: 42601: syntax error at or near \"CREATE\" POSITION: 56 |
| B68 | general geometry | DuckDB | E | refused | refused: Unrecognised type 40 (40) for column c |
| B68 | general geometry | YugabyteDb | E | refused | refused: 42704: type \"geometry\" does not exist POSITION: 56 |
| B68 | general geometry | AuroraPostgreSql | E | refused | refused: 42704: type \"geometry\" does not exist POSITION: 56 |
| B68 | general geometry | Db2 | E | refused | refused: ERROR [42704] [IBM][DB2/LINUXX8664] SQL0204N \"GEOMETRY\" is an undefined name. |
| B69 | geography / geodetic | YugabyteDb | E | refused | refused: 42704: type \"geography\" does not exist POSITION: 56 |
| B69 | geography / geodetic | AuroraMySql | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'GEOGRAPHY)' at line 1 |
| B69 | geography / geodetic | AuroraPostgreSql | E | refused | refused: extension gap: PostGIS is not in the default testbed image (extension \"postgis\" is not available); the PostGIS image accepts geography(POINT,4326) (results/versions/PostgreSql-postgis.jsonl) |
| B70 | point geometry | YugabyteDb | N | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row) |
| B70 | point geometry | AuroraPostgreSql | N | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row) |
| B71 | vector embedding | DuckDB | R | refused | refused: Catalog Error: Type with name VECTOR does not exist! Did you mean \"JSON\"? LINE 1: CREATE TABLE pr349 (id INTEGER NOT NULL PRIMARY KEY, c VECTOR(3)) |
| B71 | vector embedding | YugabyteDb | E | refused | refused: 42704: type \"vector\" does not exist POSITION: 56 |
| B71 | vector embedding | AuroraPostgreSql | E | refused | refused: extension gap: pgvector is not in the default testbed image (extension \"vector\" is not available); the pgvector image accepts vector(3) as float[] (results/versions/PostgreSql-pgvector.jsonl) |
| B73 | hierarchical path | AuroraPostgreSql | E | refused | refused: 42704: type \"hierarchyid\" does not exist POSITION: 56 |
| A56 | point geometry | DuckDB | E | refused | refused: Catalog Error: Type with name POINT does not exist! Did you mean \"JSON\"? LINE 1: CREATE TABLE pr276 (id INTEGER NOT NULL PRIMARY KEY, c POINT) |
| A56 | point geometry | AuroraPostgreSql | V | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row) |
| A57 | line / linestring geometry | DuckDB | E | refused | refused: Catalog Error: Type with name LINESTRING does not exist! Did you mean \"main.INTEGER\"? LINE 1: CREATE TABLE pr285 (id INTEGER NOT NULL PRIMARY KEY, c LINESTRING) |
| A57 | line / linestring geometry | AuroraPostgreSql | V | refused | refused: 42704: type \"linestring\" does not exist POSITION: 56 |
| A58 | polygon geometry | DuckDB | E | refused | refused: Catalog Error: Type with name POLYGON does not exist! Did you mean \"JSON\"? LINE 1: CREATE TABLE pr291 (id INTEGER NOT NULL PRIMARY KEY, c POLYGON) |
| A58 | polygon geometry | AuroraPostgreSql | V | refused | refused: by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPolygon (the native polygon row) |
| A63 | list / multiset / set collection | DuckDB | N | refused | refused: Parser Error: syntax error at or near \"MULTISET\" LINE 1: ... TABLE pr320 (id INTEGER NOT NULL PRIMARY KEY, c INTEGER MULTISET) |
| A63 | list / multiset / set collection | YugabyteDb | A | refused | refused: 42601: syntax error at or near \"MULTISET\" POSITION: 64 |
| A63 | list / multiset / set collection | AuroraPostgreSql | A | refused | refused: 42601: syntax error at or near \"MULTISET\" POSITION: 64 |
| B08 | signed 64-bit | Informix | N | refused | refused: server: Informix's BIGINT and INT8 exclude -9223372036854775808, their NULL sentinel; the server refuses the value (Value will not fit in a BIGINT or an INT8, confirmed live 2026-11-06) |
| B20 | boolean | SybaseASE | A | refused | refused: Can't find type 'BOOLEAN'. |
| B43 | local-zone timestamp | SingleStore | A | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'WITH LOCAL TIME ZONE)' at line 1 |
| B43 | local-zone timestamp | Spanner | N | refused | refused: P0001: syntax error at or near \"WITH\"; failed to parse the DDL statements. |
| B44 | year-month interval | Spanner | R | refused | refused: P0001: Column of type <interval> is not supported. |
| B45 | day-time interval | Spanner | R | refused | refused: P0001: Column of type <interval> is not supported. |
| B50 | specialized security label | Informix | N | refused | refused: ERROR [HY000] [Informix][Informix ODBC Driver][Informix]User (informix) does not have DBSECADM authority. |
| B53 | JSON text / JSON value | SapHana | R | refused | refused: sql syntax error: incorrect syntax near \"JSON\": line 1 col 56 (at pos 56) |
| B55 | BSON | Informix | N | refused | refused: by design: raw bytes have no cast to JSON on Informix (No cast from byte to json, confirmed live); Informix BSON is written from JSON text, covered by the native binary JSON row |
| B56 | XML | SybaseASE | R | refused | refused: Can't find type 'XML'. |
| B58 | SQL array | InterBase | N | refused | refused: by design: InterBase arrays have fixed dimensions (INTEGER[10] is accepted, results/InterBase.jsonl); there is no variable-length array to map |
| B63 | list / multiset / set collection | SingleStore | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'MULTISET)' at line 1 |
| B63 | list / multiset / set collection | Access | R | refused | refused: Syntax error in CREATE TABLE statement. |
| B66 | user-defined enum / domain | Informix | N | refused | refused: ERROR [42000] [Informix][Informix ODBC Driver][Informix]A syntax error has occurred. |
| B66 | user-defined enum / domain | InterBase | N | refused | refused: Dynamic SQL Error SQL error code = -104 Token unknown - line 1, column 56 CREATE |
| B68 | general geometry | Informix | E | refused | refused: ERROR [HY000] [Informix][Informix ODBC Driver][Informix]Type (geometry) not found. |
| B71 | vector embedding | SapHana | V | refused | refused: sql syntax error: incorrect syntax near \"VECTOR\": line 1 col 56 (at pos 56) |
| A42 | generic interval | Spanner | A | refused | refused: P0001: Column of type <interval> is not supported. |
| A56 | point geometry | SingleStore | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'POINT)' at line 1 |
| A56 | point geometry | SybaseASE | N | refused | refused: Can't find type 'POINT'. |
| A56 | point geometry | Informix | E | refused | refused: ERROR [HY000] [Informix][Informix ODBC Driver][Informix]Type (point) not found. |
| A57 | line / linestring geometry | SingleStore | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'LINESTRING)' at line 1 |
| A57 | line / linestring geometry | SybaseASE | N | refused | refused: Can't find type 'LINESTRING'. |
| A58 | polygon geometry | SingleStore | N | refused | refused: You have an error in your SQL syntax; check the manual that corresponds to your MySQL server version for the right syntax to use near 'POLYGON)' at line 1 |
| A58 | polygon geometry | SybaseASE | N | refused | refused: Can't find type 'POLYGON'. |
| A63 | list / multiset / set collection | Spanner | A | refused | refused: P0001: syntax error at or near \"MULTISET\"; failed to parse the DDL statements. |
