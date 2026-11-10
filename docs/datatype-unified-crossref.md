# Unified database datatype cross-reference

Merged from `database_datatype_cross_reference(1).xlsx` and `pengdows_datatype_crossref.xlsx`. Generated 2026-11-03.

**Scope:** 94 distinct semantic concepts across 23 named database products, excluding the `Unknown` enum value. The merged concepts are not an exhaustive catalog of all possible SQL names, UDTs, or extension types.

**Reconciliation policy:** The Pengdows workbook is the primary source for SQL declarations, ISO and CLR mappings, and restrictions. The first workbook supplies additional distinct concepts and competing classifications. A mismatch is logged, not silently overwritten. `?` means unverified, **not** unsupported. Sources are workbook-derived; vendor links below are references, not independent verification performed for this merge.

## Status codes

| Code | Meaning |
|---|---|
| `N` | Native |
| `A` | Alias or alternate |
| `V` | Version-dependent |
| `E` | Extension-dependent |
| `R` | Restricted |
| `X` | Unsupported |
| `?` | Unverified |

## Summary

- Primary concepts: **77**; extra, non-equivalent concepts retained: **17**.
- Total cells in unified status matrix: **2,162**.
- Cell status counts: `N` 573, `A` 113, `V` 49, `E` 35, `R` 59, `X` 1,016, `?` 317.
- Status disagreements on semantically matched categories: **205** (listed later).
- Source correction notes retained: **13**.

## Database names

| Abbreviation | Database |
|---|---|
| `PG` | PostgreSQL |
| `MSSQL` | SQL Server |
| `ORA` | Oracle |
| `FB` | Firebird |
| `CRDB` | CockroachDB |
| `MDB` | MariaDB |
| `MYSQL` | MySQL |
| `SQLITE` | SQLite |
| `DUCK` | DuckDB |
| `YB` | YugabyteDB |
| `TIDB` | TiDB |
| `SNOW` | Snowflake |
| `AUR-M` | Aurora MySQL |
| `AUR-P` | Aurora PostgreSQL |
| `DB2` | Db2 LUW |
| `FLAT` | pengdows.flatfile |
| `SS` | SingleStore |
| `ASE` | Sybase ASE |
| `SPAN` | Spanner PG |
| `IDS` | Informix |
| `HANA` | SAP HANA |
| `IB` | InterBase |
| `ACE` | Access ACE |

## Status matrix

Each cell records the reconciled *classification*, not a successful ADO.NET test. Detailed SQL spellings and unresolved disagreements follow.

### Databases 1–8

| ID | Category | Semantic concept | PG | MSSQL | ORA | FB | CRDB | MDB | MYSQL | SQLITE |
|---|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| B01 | Integer | 8-bit signed | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `A` |
| B02 | Integer | 8-bit unsigned | `X` | `N` | `X` | `X` | `X` | `N` | `N` | `X` |
| B03 | Integer | 16-bit signed | `N` | `N` | `A` | `N` | `N` | `N` | `N` | `A` |
| B04 | Integer | 16-bit unsigned | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B05 | Integer | 24-bit signed/unsigned | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B06 | Integer | 32-bit signed | `N` | `N` | `A` | `N` | `N` | `N` | `N` | `A` |
| B07 | Integer | 32-bit unsigned | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B08 | Integer | 64-bit signed | `N` | `N` | `X` | `N` | `N` | `N` | `N` | `N` |
| B09 | Integer | 64-bit unsigned | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B10 | Integer | 128-bit signed | `X` | `X` | `X` | `V` | `X` | `X` | `X` | `X` |
| B11 | Integer | 128-bit unsigned | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B12 | Integer | Arbitrary-precision integer | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B13 | Key generation | Identity column (declaration, not a type) | `R` | `R` | `R` | `R` | `R` | `R` | `R` | `R` |
| B14 | Key generation | SERIAL shorthand | `R` | `X` | `X` | `X` | `R` | `R` | `R` | `X` |
| B15 | Exact numeric | Fixed-point DECIMAL(p,s) | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B16 | Exact numeric | Decimal floating point | `X` | `X` | `X` | `V` | `X` | `X` | `X` | `X` |
| B17 | Exact numeric | Money / currency | `N` | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B18 | Approximate numeric | 32-bit IEEE float | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B19 | Approximate numeric | 64-bit IEEE double | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B20 | Boolean | Boolean | `N` | `A` | `V` | `V` | `N` | `A` | `A` | `A` |
| B21 | Character | Fixed-length CHAR(n) | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B22 | Character | Variable-length VARCHAR(n) | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B23 | Character | Unbounded / long text | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B24 | Character | National fixed NCHAR(n) | `A` | `N` | `N` | `A` | `X` | `A` | `A` | `A` |
| B25 | Character | National varying NVARCHAR(n) | `A` | `N` | `N` | `A` | `X` | `A` | `A` | `A` |
| B26 | Character | National / Unicode LOB | `X` | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B27 | Character | Unicode-specific (UTF-16) types | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B28 | Character | Double-byte GRAPHIC types | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B29 | Character | Vendor-specialized strings | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B30 | Character | System-name alias | `X` | `A` | `X` | `X` | `X` | `X` | `X` | `X` |
| B31 | Character | Case-insensitive text | `E` | `X` | `X` | `X` | `?` | `X` | `X` | `X` |
| B32 | Binary | Fixed-length BINARY(n) | `X` | `N` | `A` | `V` | `X` | `N` | `N` | `A` |
| B33 | Binary | Variable-length VARBINARY(n) | `A` | `N` | `N` | `V` | `A` | `N` | `N` | `A` |
| B34 | Binary | Binary LOB | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B35 | Binary | Custom BLOB subtypes | `X` | `X` | `X` | `N` | `X` | `X` | `X` | `X` |
| B36 | Binary | External file reference | `X` | `X` | `N` | `X` | `X` | `X` | `X` | `X` |
| B37 | Binary | Bit string BIT(n) / BIT VARYING | `N` | `X` | `X` | `X` | `N` | `N` | `N` | `X` |
| B38 | Temporal | Date only | `N` | `N` | `A` | `N` | `N` | `N` | `N` | `A` |
| B39 | Temporal | Time of day | `N` | `N` | `X` | `N` | `N` | `N` | `N` | `A` |
| B40 | Temporal | Time with time zone | `N` | `X` | `X` | `V` | `N` | `X` | `X` | `X` |
| B41 | Temporal | Timestamp without time zone | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B42 | Temporal | Timestamp with time zone (offset/zone retained) | `X` | `N` | `N` | `V` | `X` | `X` | `X` | `X` |
| B43 | Temporal | Instant (UTC-normalized, session-zone rendered) | `X` | `X` | `N` | `X` | `X` | `A` | `A` | `X` |
| B44 | Temporal | Interval year–month | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B45 | Temporal | Interval day–second | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B46 | Temporal | Year | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B47 | Temporal | Row version (not a time) | `X` | `N` | `R` | `X` | `X` | `X` | `X` | `X` |
| B48 | Identifier | UUID / GUID | `N` | `N` | `X` | `X` | `N` | `V` | `X` | `X` |
| B49 | Identifier | Physical row id | `R` | `X` | `N` | `X` | `X` | `X` | `X` | `R` |
| B50 | Identifier | Security label | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B51 | Network | IP address / CIDR | `N` | `X` | `X` | `X` | `N` | `V` | `X` | `X` |
| B52 | Network | MAC address | `N` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B53 | Semi-structured | JSON (text-preserving) | `N` | `V` | `V` | `X` | `A` | `A` | `N` | `R` |
| B54 | Semi-structured | Binary JSON | `N` | `X` | `X` | `X` | `N` | `X` | `X` | `V` |
| B55 | Semi-structured | BSON | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B56 | Semi-structured | XML | `N` | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B57 | Semi-structured | Variant / dynamic | `X` | `N` | `X` | `X` | `X` | `X` | `X` | `R` |
| B58 | Composite | Array | `N` | `X` | `R` | `N` | `N` | `X` | `X` | `X` |
| B59 | Composite | Struct / row / composite | `N` | `X` | `N` | `X` | `?` | `X` | `X` | `X` |
| B60 | Composite | Map | `E` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B61 | Composite | Tagged union | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B62 | Composite | Enumeration | `N` | `X` | `X` | `X` | `N` | `N` | `N` | `X` |
| B63 | Composite | Multi-valued set | `X` | `X` | `X` | `X` | `X` | `N` | `N` | `X` |
| B64 | Composite | Range | `N` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B65 | Composite | Multirange | `V` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B66 | User-defined | Domain / distinct type | `N` | `N` | `V` | `N` | `X` | `X` | `X` | `X` |
| B67 | User-defined | Opaque / CLR / extension base type | `N` | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B68 | Spatial | Planar geometry | `E` | `N` | `E` | `X` | `N` | `N` | `N` | `E` |
| B69 | Spatial | Geodetic geography | `E` | `N` | `E` | `X` | `N` | `X` | `A` | `X` |
| B70 | Spatial | Built-in 2-D geometric primitives | `N` | `X` | `X` | `X` | `R` | `X` | `X` | `X` |
| B71 | Vector / AI | Vector embedding | `E` | `V` | `V` | `X` | `?` | `V` | `V` | `E` |
| B72 | Text search | Full-text document/query | `N` | `X` | `X` | `X` | `V` | `X` | `X` | `X` |
| B73 | Hierarchy | Hierarchy path | `E` | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B74 | System | Object id / reg* / xid / lsn / snapshot | `N` | `X` | `X` | `X` | `R` | `X` | `X` | `X` |
| B75 | System | Pseudo / non-column types | `R` | `R` | `R` | `X` | `X` | `X` | `X` | `X` |
| B76 | System | Commit timestamp | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B77 | Access-only | Hyperlink / Attachment / Calculated / OLE | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| A26 | Character | set-valued enum | `?` | `?` | `?` | `?` | `?` | `N` | `N` | `?` |
| A30 | Binary | legacy long binary | `?` | `N` | `N` | `?` | `?` | `N` | `N` | `?` |
| A38 | Temporal | second-precision datetime | `?` | `A` | `N` | `?` | `?` | `?` | `?` | `?` |
| A42 | Temporal | generic interval | `N` | `?` | `?` | `?` | `?` | `?` | `?` | `?` |
| A45 | Identifiers | object identifier / OID | `N` | `?` | `?` | `?` | `?` | `?` | `?` | `?` |
| A46 | Identifiers | transaction ID | `N` | `?` | `?` | `?` | `X` | `?` | `?` | `?` |
| A47 | Identifiers | WAL / log position | `N` | `?` | `?` | `?` | `X` | `?` | `?` | `?` |
| A53 | Network | IP address | `N` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A54 | Network | IP network / CIDR | `N` | `?` | `?` | `?` | `A` | `?` | `?` | `?` |
| A56 | Spatial | point geometry | `N` | `N` | `?` | `?` | `N` | `N` | `N` | `?` |
| A57 | Spatial | line / linestring geometry | `N` | `N` | `?` | `?` | `N` | `N` | `N` | `?` |
| A58 | Spatial | polygon geometry | `N` | `N` | `?` | `?` | `N` | `N` | `N` | `?` |
| A62 | Collections | fixed-length array | `A` | `?` | `?` | `N` | `A` | `?` | `?` | `?` |
| A63 | Collections | list / multiset / set collection | `A` | `?` | `?` | `?` | `A` | `?` | `?` | `?` |
| A74 | Specialized | opaque / special database type | `?` | `N` | `N` | `?` | `?` | `?` | `?` | `?` |
| A76 | Specialized | spatial raster / other extensions | `E` | `?` | `?` | `?` | `E` | `?` | `?` | `?` |
| A77 | Specialized | cursor / table variable | `?` | `A` | `A` | `?` | `?` | `?` | `?` | `?` |

### Databases 9–16

| ID | Category | Semantic concept | DUCK | YB | TIDB | SNOW | AUR-M | AUR-P | DB2 | FLAT |
|---|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| B01 | Integer | 8-bit signed | `N` | `X` | `N` | `A` | `N` | `X` | `X` | `X` |
| B02 | Integer | 8-bit unsigned | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B03 | Integer | 16-bit signed | `N` | `N` | `N` | `A` | `N` | `N` | `N` | `?` |
| B04 | Integer | 16-bit unsigned | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B05 | Integer | 24-bit signed/unsigned | `X` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B06 | Integer | 32-bit signed | `N` | `N` | `N` | `A` | `N` | `N` | `N` | `?` |
| B07 | Integer | 32-bit unsigned | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B08 | Integer | 64-bit signed | `N` | `N` | `N` | `A` | `N` | `N` | `N` | `?` |
| B09 | Integer | 64-bit unsigned | `N` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B10 | Integer | 128-bit signed | `N` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B11 | Integer | 128-bit unsigned | `N` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B12 | Integer | Arbitrary-precision integer | `V` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B13 | Key generation | Identity column (declaration, not a type) | `R` | `R` | `R` | `R` | `R` | `R` | `R` | `?` |
| B14 | Key generation | SERIAL shorthand | `X` | `R` | `R` | `X` | `R` | `R` | `X` | `X` |
| B15 | Exact numeric | Fixed-point DECIMAL(p,s) | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B16 | Exact numeric | Decimal floating point | `X` | `X` | `X` | `V` | `X` | `X` | `N` | `?` |
| B17 | Exact numeric | Money / currency | `X` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B18 | Approximate numeric | 32-bit IEEE float | `N` | `N` | `N` | `A` | `N` | `N` | `N` | `?` |
| B19 | Approximate numeric | 64-bit IEEE double | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B20 | Boolean | Boolean | `N` | `N` | `A` | `N` | `A` | `N` | `V` | `?` |
| B21 | Character | Fixed-length CHAR(n) | `A` | `N` | `N` | `A` | `N` | `N` | `N` | `?` |
| B22 | Character | Variable-length VARCHAR(n) | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B23 | Character | Unbounded / long text | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B24 | Character | National fixed NCHAR(n) | `X` | `A` | `A` | `A` | `A` | `A` | `A` | `?` |
| B25 | Character | National varying NVARCHAR(n) | `X` | `A` | `A` | `A` | `A` | `A` | `A` | `?` |
| B26 | Character | National / Unicode LOB | `X` | `X` | `X` | `X` | `X` | `X` | `N` | `?` |
| B27 | Character | Unicode-specific (UTF-16) types | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B28 | Character | Double-byte GRAPHIC types | `X` | `X` | `X` | `X` | `X` | `X` | `N` | `X` |
| B29 | Character | Vendor-specialized strings | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B30 | Character | System-name alias | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B31 | Character | Case-insensitive text | `X` | `E` | `X` | `X` | `X` | `E` | `X` | `X` |
| B32 | Binary | Fixed-length BINARY(n) | `A` | `X` | `N` | `A` | `N` | `X` | `N` | `?` |
| B33 | Binary | Variable-length VARBINARY(n) | `A` | `A` | `N` | `N` | `N` | `A` | `N` | `?` |
| B34 | Binary | Binary LOB | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B35 | Binary | Custom BLOB subtypes | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B36 | Binary | External file reference | `X` | `X` | `X` | `V` | `X` | `X` | `X` | `X` |
| B37 | Binary | Bit string BIT(n) / BIT VARYING | `N` | `N` | `N` | `X` | `N` | `N` | `X` | `X` |
| B38 | Temporal | Date only | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B39 | Temporal | Time of day | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B40 | Temporal | Time with time zone | `N` | `N` | `X` | `X` | `X` | `N` | `X` | `?` |
| B41 | Temporal | Timestamp without time zone | `N` | `N` | `N` | `N` | `N` | `N` | `N` | `?` |
| B42 | Temporal | Timestamp with time zone (offset/zone retained) | `X` | `X` | `X` | `N` | `X` | `X` | `X` | `?` |
| B43 | Temporal | Instant (UTC-normalized, session-zone rendered) | `N` | `N` | `A` | `N` | `A` | `N` | `X` | `X` |
| B44 | Temporal | Interval year–month | `N` | `N` | `X` | `X` | `X` | `N` | `X` | `?` |
| B45 | Temporal | Interval day–second | `N` | `N` | `X` | `X` | `A` | `N` | `X` | `?` |
| B46 | Temporal | Year | `X` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B47 | Temporal | Row version (not a time) | `X` | `X` | `X` | `X` | `X` | `X` | `R` | `X` |
| B48 | Identifier | UUID / GUID | `N` | `N` | `X` | `V` | `X` | `N` | `X` | `X` |
| B49 | Identifier | Physical row id | `X` | `X` | `X` | `X` | `X` | `R` | `?` | `X` |
| B50 | Identifier | Security label | `X` | `X` | `X` | `X` | `X` | `X` | `N` | `X` |
| B51 | Network | IP address / CIDR | `E` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B52 | Network | MAC address | `X` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B53 | Semi-structured | JSON (text-preserving) | `E` | `N` | `N` | `A` | `N` | `N` | `R` | `?` |
| B54 | Semi-structured | Binary JSON | `X` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B55 | Semi-structured | BSON | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B56 | Semi-structured | XML | `X` | `X` | `X` | `R` | `X` | `N` | `N` | `?` |
| B57 | Semi-structured | Variant / dynamic | `X` | `X` | `X` | `N` | `X` | `X` | `X` | `X` |
| B58 | Composite | Array | `N` | `N` | `X` | `N` | `X` | `N` | `R` | `?` |
| B59 | Composite | Struct / row / composite | `N` | `N` | `X` | `N` | `X` | `N` | `R` | `?` |
| B60 | Composite | Map | `N` | `E` | `X` | `N` | `X` | `E` | `X` | `X` |
| B61 | Composite | Tagged union | `N` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B62 | Composite | Enumeration | `N` | `N` | `N` | `X` | `N` | `N` | `X` | `X` |
| B63 | Composite | Multi-valued set | `X` | `X` | `N` | `X` | `N` | `X` | `X` | `X` |
| B64 | Composite | Range | `X` | `?` | `X` | `X` | `X` | `N` | `X` | `X` |
| B65 | Composite | Multirange | `X` | `?` | `X` | `X` | `X` | `V` | `X` | `X` |
| B66 | User-defined | Domain / distinct type | `X` | `N` | `X` | `X` | `X` | `N` | `N` | `?` |
| B67 | User-defined | Opaque / CLR / extension base type | `X` | `X` | `X` | `X` | `X` | `R` | `N` | `X` |
| B68 | Spatial | Planar geometry | `E` | `E` | `X` | `N` | `N` | `E` | `E` | `X` |
| B69 | Spatial | Geodetic geography | `X` | `E` | `X` | `N` | `A` | `E` | `X` | `X` |
| B70 | Spatial | Built-in 2-D geometric primitives | `X` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B71 | Vector / AI | Vector embedding | `R` | `E` | `V` | `N` | `X` | `E` | `?` | `X` |
| B72 | Text search | Full-text document/query | `X` | `N` | `X` | `X` | `X` | `N` | `X` | `X` |
| B73 | Hierarchy | Hierarchy path | `X` | `X` | `X` | `X` | `X` | `E` | `X` | `X` |
| B74 | System | Object id / reg* / xid / lsn / snapshot | `X` | `?` | `X` | `X` | `X` | `N` | `X` | `X` |
| B75 | System | Pseudo / non-column types | `X` | `X` | `X` | `X` | `X` | `R` | `R` | `X` |
| B76 | System | Commit timestamp | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B77 | Access-only | Hyperlink / Attachment / Calculated / OLE | `X` | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| A26 | Character | set-valued enum | `?` | `?` | `N` | `?` | `V` | `?` | `?` | `?` |
| A30 | Binary | legacy long binary | `?` | `?` | `N` | `?` | `V` | `?` | `?` | `?` |
| A38 | Temporal | second-precision datetime | `?` | `?` | `?` | `?` | `?` | `?` | `?` | `?` |
| A42 | Temporal | generic interval | `N` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A45 | Identifiers | object identifier / OID | `?` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A46 | Identifiers | transaction ID | `?` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A47 | Identifiers | WAL / log position | `?` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A53 | Network | IP address | `?` | `?` | `?` | `V` | `?` | `V` | `?` | `?` |
| A54 | Network | IP network / CIDR | `?` | `?` | `?` | `?` | `?` | `V` | `?` | `?` |
| A56 | Spatial | point geometry | `E` | `?` | `X` | `N` | `V` | `V` | `?` | `?` |
| A57 | Spatial | line / linestring geometry | `E` | `?` | `X` | `N` | `V` | `V` | `?` | `?` |
| A58 | Spatial | polygon geometry | `E` | `?` | `X` | `N` | `V` | `V` | `?` | `?` |
| A62 | Collections | fixed-length array | `N` | `A` | `?` | `A` | `?` | `A` | `?` | `?` |
| A63 | Collections | list / multiset / set collection | `N` | `A` | `?` | `A` | `?` | `A` | `?` | `?` |
| A74 | Specialized | opaque / special database type | `?` | `?` | `?` | `N` | `?` | `?` | `?` | `?` |
| A76 | Specialized | spatial raster / other extensions | `?` | `E` | `?` | `?` | `?` | `E` | `?` | `?` |
| A77 | Specialized | cursor / table variable | `?` | `?` | `?` | `?` | `?` | `?` | `A` | `?` |

### Databases 17–23

| ID | Category | Semantic concept | SS | ASE | SPAN | IDS | HANA | IB | ACE |
|---|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| B01 | Integer | 8-bit signed | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B02 | Integer | 8-bit unsigned | `N` | `N` | `X` | `X` | `N` | `X` | `N` |
| B03 | Integer | 16-bit signed | `N` | `N` | `X` | `N` | `N` | `N` | `N` |
| B04 | Integer | 16-bit unsigned | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B05 | Integer | 24-bit signed/unsigned | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B06 | Integer | 32-bit signed | `N` | `N` | `A` | `N` | `N` | `N` | `N` |
| B07 | Integer | 32-bit unsigned | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B08 | Integer | 64-bit signed | `N` | `N` | `N` | `N` | `N` | `?` | `V` |
| B09 | Integer | 64-bit unsigned | `N` | `N` | `X` | `X` | `X` | `X` | `X` |
| B10 | Integer | 128-bit signed | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B11 | Integer | 128-bit unsigned | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B12 | Integer | Arbitrary-precision integer | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B13 | Key generation | Identity column (declaration, not a type) | `R` | `R` | `?` | `R` | `R` | `X` | `R` |
| B14 | Key generation | SERIAL shorthand | `X` | `X` | `?` | `N` | `X` | `X` | `X` |
| B15 | Exact numeric | Fixed-point DECIMAL(p,s) | `N` | `N` | `R` | `N` | `N` | `N` | `N` |
| B16 | Exact numeric | Decimal floating point | `X` | `X` | `X` | `A` | `N` | `X` | `X` |
| B17 | Exact numeric | Money / currency | `X` | `N` | `X` | `N` | `X` | `X` | `N` |
| B18 | Approximate numeric | 32-bit IEEE float | `N` | `N` | `V` | `N` | `N` | `N` | `N` |
| B19 | Approximate numeric | 64-bit IEEE double | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B20 | Boolean | Boolean | `A` | `A` | `N` | `N` | `N` | `?` | `A` |
| B21 | Character | Fixed-length CHAR(n) | `N` | `N` | `X` | `N` | `?` | `N` | `N` |
| B22 | Character | Variable-length VARCHAR(n) | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B23 | Character | Unbounded / long text | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B24 | Character | National fixed NCHAR(n) | `?` | `N` | `X` | `N` | `X` | `A` | `A` |
| B25 | Character | National varying NVARCHAR(n) | `?` | `N` | `X` | `N` | `N` | `A` | `A` |
| B26 | Character | National / Unicode LOB | `X` | `N` | `X` | `X` | `N` | `X` | `X` |
| B27 | Character | Unicode-specific (UTF-16) types | `X` | `N` | `X` | `X` | `X` | `X` | `X` |
| B28 | Character | Double-byte GRAPHIC types | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B29 | Character | Vendor-specialized strings | `X` | `X` | `X` | `X` | `?` | `X` | `X` |
| B30 | Character | System-name alias | `X` | `A` | `X` | `X` | `X` | `X` | `X` |
| B31 | Character | Case-insensitive text | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B32 | Binary | Fixed-length BINARY(n) | `N` | `N` | `X` | `X` | `?` | `A` | `N` |
| B33 | Binary | Variable-length VARBINARY(n) | `N` | `N` | `A` | `X` | `N` | `A` | `N` |
| B34 | Binary | Binary LOB | `N` | `N` | `N` | `N` | `N` | `N` | `N` |
| B35 | Binary | Custom BLOB subtypes | `X` | `X` | `X` | `X` | `X` | `N` | `X` |
| B36 | Binary | External file reference | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B37 | Binary | Bit string BIT(n) / BIT VARYING | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B38 | Temporal | Date only | `N` | `N` | `N` | `N` | `N` | `N` | `A` |
| B39 | Temporal | Time of day | `N` | `N` | `X` | `A` | `N` | `N` | `A` |
| B40 | Temporal | Time with time zone | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B41 | Temporal | Timestamp without time zone | `N` | `N` | `X` | `N` | `N` | `N` | `N` |
| B42 | Temporal | Timestamp with time zone (offset/zone retained) | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B43 | Temporal | Instant (UTC-normalized, session-zone rendered) | `A` | `X` | `N` | `X` | `X` | `X` | `X` |
| B44 | Temporal | Interval year–month | `X` | `X` | `R` | `N` | `X` | `X` | `X` |
| B45 | Temporal | Interval day–second | `X` | `X` | `R` | `N` | `X` | `X` | `X` |
| B46 | Temporal | Year | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B47 | Temporal | Row version (not a time) | `X` | `N` | `X` | `X` | `X` | `X` | `X` |
| B48 | Identifier | UUID / GUID | `X` | `X` | `V` | `X` | `X` | `X` | `N` |
| B49 | Identifier | Physical row id | `X` | `X` | `X` | `R` | `X` | `X` | `X` |
| B50 | Identifier | Security label | `X` | `X` | `X` | `N` | `X` | `X` | `X` |
| B51 | Network | IP address / CIDR | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B52 | Network | MAC address | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B53 | Semi-structured | JSON (text-preserving) | `N` | `X` | `X` | `N` | `R` | `X` | `X` |
| B54 | Semi-structured | Binary JSON | `X` | `X` | `N` | `X` | `X` | `X` | `X` |
| B55 | Semi-structured | BSON | `V` | `X` | `X` | `N` | `X` | `X` | `X` |
| B56 | Semi-structured | XML | `X` | `R` | `X` | `X` | `X` | `X` | `X` |
| B57 | Semi-structured | Variant / dynamic | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B58 | Composite | Array | `X` | `X` | `N` | `N` | `N` | `N` | `X` |
| B59 | Composite | Struct / row / composite | `X` | `X` | `X` | `N` | `X` | `X` | `X` |
| B60 | Composite | Map | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B61 | Composite | Tagged union | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B62 | Composite | Enumeration | `N` | `X` | `X` | `X` | `X` | `X` | `X` |
| B63 | Composite | Multi-valued set | `N` | `X` | `X` | `A` | `X` | `X` | `R` |
| B64 | Composite | Range | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B65 | Composite | Multirange | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B66 | User-defined | Domain / distinct type | `X` | `X` | `X` | `N` | `X` | `N` | `X` |
| B67 | User-defined | Opaque / CLR / extension base type | `X` | `X` | `X` | `N` | `X` | `X` | `X` |
| B68 | Spatial | Planar geometry | `X` | `X` | `X` | `E` | `N` | `X` | `X` |
| B69 | Spatial | Geodetic geography | `N` | `X` | `X` | `X` | `A` | `X` | `X` |
| B70 | Spatial | Built-in 2-D geometric primitives | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B71 | Vector / AI | Vector embedding | `V` | `X` | `X` | `X` | `V` | `X` | `X` |
| B72 | Text search | Full-text document/query | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B73 | Hierarchy | Hierarchy path | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B74 | System | Object id / reg* / xid / lsn / snapshot | `X` | `X` | `X` | `X` | `X` | `X` | `X` |
| B75 | System | Pseudo / non-column types | `X` | `X` | `X` | `X` | `R` | `X` | `X` |
| B76 | System | Commit timestamp | `X` | `X` | `R` | `X` | `X` | `X` | `X` |
| B77 | Access-only | Hyperlink / Attachment / Calculated / OLE | `X` | `X` | `X` | `X` | `X` | `X` | `R` |
| A26 | Character | set-valued enum | `N` | `?` | `?` | `?` | `?` | `?` | `?` |
| A30 | Binary | legacy long binary | `N` | `N` | `?` | `N` | `?` | `?` | `N` |
| A38 | Temporal | second-precision datetime | `?` | `A` | `?` | `?` | `N` | `?` | `A` |
| A42 | Temporal | generic interval | `?` | `?` | `A` | `?` | `?` | `?` | `?` |
| A45 | Identifiers | object identifier / OID | `?` | `?` | `X` | `?` | `?` | `?` | `?` |
| A46 | Identifiers | transaction ID | `?` | `?` | `X` | `?` | `?` | `?` | `?` |
| A47 | Identifiers | WAL / log position | `?` | `?` | `X` | `?` | `?` | `?` | `?` |
| A53 | Network | IP address | `?` | `?` | `X` | `A` | `?` | `?` | `?` |
| A54 | Network | IP network / CIDR | `?` | `?` | `X` | `?` | `?` | `?` | `?` |
| A56 | Spatial | point geometry | `N` | `N` | `X` | `E` | `N` | `?` | `?` |
| A57 | Spatial | line / linestring geometry | `N` | `N` | `X` | `?` | `N` | `?` | `?` |
| A58 | Spatial | polygon geometry | `N` | `N` | `X` | `?` | `N` | `?` | `?` |
| A62 | Collections | fixed-length array | `?` | `?` | `X` | `?` | `?` | `N` | `?` |
| A63 | Collections | list / multiset / set collection | `?` | `?` | `A` | `N` | `?` | `?` | `?` |
| A74 | Specialized | opaque / special database type | `?` | `N` | `?` | `N` | `N` | `?` | `?` |
| A76 | Specialized | spatial raster / other extensions | `?` | `?` | `E` | `?` | `?` | `?` | `?` |
| A77 | Specialized | cursor / table variable | `?` | `A` | `?` | `?` | `A` | `?` | `?` |

## SQL declarations by semantic concept

Entries show the **primary workbook’s declaration**, where provided, with classification. Where that workbook has no corresponding concept, declarations come from the first workbook. A dash or `?` denotes absent/unverified information; it must not be read as a supported SQL declaration.

### B01 — Integer: 8-bit signed

**ISO SQL:** `—` · **CLR/DbType:** `sbyte / DbType.SByte` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — | [A] SMALLINT (No signed byte type) |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — | [A] INT2 (No int1) |
| MariaDB | [N] TINYINT |  |
| MySQL | [N] TINYINT |  |
| SQLite | [A] TINYINT → INTEGER affinity | [A] INTEGER storage (No distinct INT8 storage) |
| DuckDB | [N] TINYINT / INT1 |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] TINYINT |  |
| Snowflake | [A] TINYINT → NUMBER(38,0) | [A] NUMBER(3,0) (Precision semantics, not 8-bit storage) |
| Aurora MySQL | [N] TINYINT | [V] TINYINT (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] TINYINT |  |
| Sybase ASE | [X] — | [A] TINYINT is unsigned; use SMALLINT (No signed 8-bit native TINYINT) |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — | [A] SMALLINT (TINYINT is unsigned) |
| InterBase | [X] — |  |
| Access ACE | [X] — | [A] SMALLINT (BYTE is unsigned) |

### B02 — Integer: 8-bit unsigned

**ISO SQL:** `—` · **CLR/DbType:** `byte / DbType.Byte` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [N] tinyint |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] TINYINT UNSIGNED |  |
| MySQL | [N] TINYINT UNSIGNED |  |
| SQLite | [X] — |  |
| DuckDB | [N] UTINYINT |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] TINYINT UNSIGNED |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] TINYINT UNSIGNED | [V] TINYINT UNSIGNED (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] TINYINT UNSIGNED |  |
| Sybase ASE | [N] tinyint |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [N] TINYINT |  |
| InterBase | [X] — |  |
| Access ACE | [N] BYTE |  |

### B03 — Integer: 16-bit signed

**ISO SQL:** `SMALLINT` · **CLR/DbType:** `short / DbType.Int16` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] smallint / int2 |  |
| SQL Server | [N] smallint |  |
| Oracle | [A] SMALLINT → NUMBER(38) | [A] NUMBER(5,0) (NUMERIC representation; precision/range not necessarily identical) |
| Firebird | [N] SMALLINT |  |
| CockroachDB | [N] INT2 / SMALLINT |  |
| MariaDB | [N] SMALLINT |  |
| MySQL | [N] SMALLINT |  |
| SQLite | [A] SMALLINT → INTEGER affinity | [A] INTEGER storage (No distinct INT16 storage) |
| DuckDB | [N] SMALLINT / INT2 |  |
| YugabyteDB | [N] smallint / int2 |  |
| TiDB | [N] SMALLINT |  |
| Snowflake | [A] SMALLINT → NUMBER(38,0) | [A] NUMBER(5,0) (NUMERIC representation; precision/range not necessarily identical) |
| Aurora MySQL | [N] SMALLINT | [V] SMALLINT (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] smallint / int2 | [V] SMALLINT (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] SMALLINT |  |
| pengdows.flatfile | [?] SMALLINT |  |
| SingleStore | [N] SMALLINT |  |
| Sybase ASE | [N] smallint |  |
| Spanner PG | [X] — (bigint only) | [A] INT8 (No int2 storage) |
| Informix | [N] SMALLINT |  |
| SAP HANA | [N] SMALLINT |  |
| InterBase | [N] SMALLINT |  |
| Access ACE | [N] SMALLINT (DAO 'Integer') |  |

### B04 — Integer: 16-bit unsigned

**ISO SQL:** `—` · **CLR/DbType:** `ushort / DbType.UInt16` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] SMALLINT UNSIGNED |  |
| MySQL | [N] SMALLINT UNSIGNED |  |
| SQLite | [X] — |  |
| DuckDB | [N] USMALLINT |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] SMALLINT UNSIGNED |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] SMALLINT UNSIGNED | [V] SMALLINT UNSIGNED (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] SMALLINT UNSIGNED |  |
| Sybase ASE | [N] unsigned smallint |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B05 — Integer: 24-bit signed/unsigned

**ISO SQL:** `—` · **CLR/DbType:** `int / uint` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] MEDIUMINT [UNSIGNED] / INT3 |  |
| MySQL | [N] MEDIUMINT [UNSIGNED] |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] MEDIUMINT [UNSIGNED] |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] MEDIUMINT [UNSIGNED] |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] MEDIUMINT [UNSIGNED] |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B06 — Integer: 32-bit signed

**ISO SQL:** `INTEGER` · **CLR/DbType:** `int / DbType.Int32` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] integer / int4 / int |  |
| SQL Server | [N] int |  |
| Oracle | [A] INTEGER → NUMBER(38) | [A] NUMBER(10,0) (NUMERIC representation; precision/range not necessarily identical) |
| Firebird | [N] INTEGER |  |
| CockroachDB | [N] INT4 (INT = INT8 unless default_int_size=4) | [A] INT4 (alias subject to width behavior) |
| MariaDB | [N] INT / INTEGER |  |
| MySQL | [N] INT / INTEGER |  |
| SQLite | [A] INT → INTEGER affinity | [A] INTEGER storage (No distinct INT32 storage) |
| DuckDB | [N] INTEGER / INT4 |  |
| YugabyteDB | [N] integer / int4 |  |
| TiDB | [N] INT / INTEGER |  |
| Snowflake | [A] INT → NUMBER(38,0) | [A] NUMBER(10,0) (NUMERIC representation; precision/range not necessarily identical) |
| Aurora MySQL | [N] INT / INTEGER | [V] INT (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] integer / int4 / int | [V] INTEGER (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] INTEGER |  |
| pengdows.flatfile | [?] INTEGER |  |
| SingleStore | [N] INT / INTEGER |  |
| Sybase ASE | [N] int |  |
| Spanner PG | [A] INT → bigint (64-bit) | [A] INT8 (INT is 64-bit) |
| Informix | [N] INTEGER |  |
| SAP HANA | [N] INTEGER |  |
| InterBase | [N] INTEGER |  |
| Access ACE | [N] INTEGER / LONG |  |

### B07 — Integer: 32-bit unsigned

**ISO SQL:** `—` · **CLR/DbType:** `uint / DbType.UInt32` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] INT UNSIGNED |  |
| MySQL | [N] INT UNSIGNED |  |
| SQLite | [X] — |  |
| DuckDB | [N] UINTEGER |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] INT UNSIGNED |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] INT UNSIGNED | [V] INT UNSIGNED (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] INT UNSIGNED |  |
| Sybase ASE | [N] unsigned int |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B08 — Integer: 64-bit signed

**ISO SQL:** `BIGINT` · **CLR/DbType:** `long / DbType.Int64` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] bigint / int8 |  |
| SQL Server | [N] bigint |  |
| Oracle | [X] — (use NUMBER(19)) | [A] NUMBER(19,0) (NUMERIC representation; precision/range not necessarily identical) |
| Firebird | [N] BIGINT (dialect 3) |  |
| CockroachDB | [N] INT8 / INT / BIGINT |  |
| MariaDB | [N] BIGINT |  |
| MySQL | [N] BIGINT |  |
| SQLite | [N] INTEGER (storage class is 64-bit) | [N] INTEGER storage |
| DuckDB | [N] BIGINT / INT8 |  |
| YugabyteDB | [N] bigint / int8 |  |
| TiDB | [N] BIGINT |  |
| Snowflake | [A] BIGINT → NUMBER(38,0) | [A] NUMBER(19,0) (NUMERIC representation; precision/range not necessarily identical) |
| Aurora MySQL | [N] BIGINT | [V] BIGINT (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] bigint / int8 | [V] BIGINT (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] BIGINT |  |
| pengdows.flatfile | [?] BIGINT |  |
| SingleStore | [N] BIGINT |  |
| Sybase ASE | [N] bigint (15.0+) |  |
| Spanner PG | [N] bigint / int8 | [N] INT8 / BIGINT / INT |
| Informix | [N] BIGINT / INT8 |  |
| SAP HANA | [N] BIGINT |  |
| InterBase | [?] NUMERIC(18,0) (INT64 storage); BIGINT keyword unverified | [N] BIGINT |
| Access ACE | [V] BIGINT 'Large Number' (ACE 16+, opt-in) |  |

### B09 — Integer: 64-bit unsigned

**ISO SQL:** `—` · **CLR/DbType:** `ulong / DbType.UInt64` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] BIGINT UNSIGNED |  |
| MySQL | [N] BIGINT UNSIGNED |  |
| SQLite | [X] — |  |
| DuckDB | [N] UBIGINT |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] BIGINT UNSIGNED |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] BIGINT UNSIGNED | [V] BIGINT UNSIGNED (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] BIGINT UNSIGNED |  |
| Sybase ASE | [N] unsigned bigint |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B10 — Integer: 128-bit signed

**ISO SQL:** `—` · **CLR/DbType:** `Int128 (.NET 7+) / BigInteger` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — | [A] NUMBER(38,0) (Not full signed 128-bit integer range) |
| Firebird | [V] INT128 (4.0+) |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] HUGEINT / INT128 |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — | [A] NUMBER(38,0) (Not full signed 128-bit integer range) |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B11 — Integer: 128-bit unsigned

**ISO SQL:** `—` · **CLR/DbType:** `UInt128 (.NET 7+)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] UHUGEINT |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B12 — Integer: Arbitrary-precision integer

**ISO SQL:** `—` · **CLR/DbType:** `BigInteger` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [V] BIGNUM (formerly VARINT) — verify name per version | [N] BIGNUM |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B13 — Key generation: Identity column (declaration, not a type)

**ISO SQL:** `GENERATED … AS IDENTITY` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [R] GENERATED … AS IDENTITY (10+) | [A] SERIAL / IDENTITY (Column generation syntax, not distinct storage type) |
| SQL Server | [R] IDENTITY(s,i) | [A] IDENTITY (Column generation syntax, not distinct storage type) |
| Oracle | [R] GENERATED … AS IDENTITY (12c+) | [A] GENERATED AS IDENTITY (Column generation syntax, not distinct storage type) |
| Firebird | [R] GENERATED BY DEFAULT AS IDENTITY (3.0+) | [A] GENERATED BY DEFAULT AS IDENTITY (Column generation syntax, not distinct storage type) |
| CockroachDB | [R] GENERATED … AS IDENTITY | [A] SERIAL / IDENTITY (Column generation syntax, not distinct storage type) |
| MariaDB | [R] AUTO_INCREMENT | [A] AUTO_INCREMENT (Column generation syntax, not distinct storage type) |
| MySQL | [R] AUTO_INCREMENT | [A] AUTO_INCREMENT (Column generation syntax, not distinct storage type) |
| SQLite | [R] INTEGER PRIMARY KEY [AUTOINCREMENT] | [A] INTEGER PRIMARY KEY [AUTOINCREMENT] (Rowid behavior) |
| DuckDB | [R] sequence + DEFAULT nextval() | [A] SEQUENCE + DEFAULT nextval (Column generation syntax, not distinct storage type) |
| YugabyteDB | [R] GENERATED … AS IDENTITY | [A] SERIAL / IDENTITY (Column generation syntax, not distinct storage type) |
| TiDB | [R] AUTO_INCREMENT / AUTO_RANDOM | [A] AUTO_INCREMENT (Column generation syntax, not distinct storage type) |
| Snowflake | [R] IDENTITY / AUTOINCREMENT | [A] IDENTITY / AUTOINCREMENT (Column generation syntax, not distinct storage type) |
| Aurora MySQL | [R] AUTO_INCREMENT | [A] AUTO_INCREMENT (Column generation syntax, not distinct storage type) |
| Aurora PostgreSQL | [R] GENERATED … AS IDENTITY (10+) | [A] SERIAL / IDENTITY (Column generation syntax, not distinct storage type) |
| Db2 LUW | [R] GENERATED … AS IDENTITY | [A] GENERATED AS IDENTITY (Column generation syntax, not distinct storage type) |
| pengdows.flatfile | [?] GENERATED … AS IDENTITY |  |
| SingleStore | [R] AUTO_INCREMENT | [A] AUTO_INCREMENT (Column generation syntax, not distinct storage type) |
| Sybase ASE | [R] IDENTITY on numeric(n,0) | [A] IDENTITY (Column generation syntax, not distinct storage type) |
| Spanner PG | [?] GENERATED BY DEFAULT AS IDENTITY (bit-reversed) — verify | [X] — (No PostgreSQL SERIAL / auto increment semantics) |
| Informix | [R] SERIAL / SERIAL8 / BIGSERIAL | [A] SERIAL / SERIAL8 / BIGSERIAL (Column generation syntax, not distinct storage type) |
| SAP HANA | [R] GENERATED … AS IDENTITY | [A] GENERATED AS IDENTITY (Column generation syntax, not distinct storage type) |
| InterBase | [X] — (generator + trigger) | [A] GENERATED BY DEFAULT AS IDENTITY (Column generation syntax, not distinct storage type) |
| Access ACE | [R] COUNTER / AUTOINCREMENT | [A] COUNTER / AUTOINCREMENT (Column generation syntax, not distinct storage type) |

### B14 — Key generation: SERIAL shorthand

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [R] smallserial / serial / bigserial |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [R] SERIAL (unique_rowid by default) |  |
| MariaDB | [R] SERIAL |  |
| MySQL | [R] SERIAL |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [R] smallserial / serial / bigserial |  |
| TiDB | [R] SERIAL |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [R] SERIAL |  |
| Aurora PostgreSQL | [R] smallserial / serial / bigserial |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [?] syntax accepted in limited cases — verify |  |
| Informix | [N] SERIAL / SERIAL8 / BIGSERIAL (real types) |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B15 — Exact numeric: Fixed-point DECIMAL(p,s)

**ISO SQL:** `DECIMAL / NUMERIC` · **CLR/DbType:** `decimal / DbType.Decimal` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] numeric(p≤1000,s) / decimal | [N] DECIMAL(p,s) |
| SQL Server | [N] decimal / numeric (p≤38) | [N] DECIMAL(p,s) |
| Oracle | [N] NUMBER(p≤38,s) | [N] NUMBER(p,s) |
| Firebird | [N] DECIMAL / NUMERIC (p≤18; 38 in 4.0+) | [N] DECIMAL(p,s) |
| CockroachDB | [N] DECIMAL / NUMERIC |  |
| MariaDB | [N] DECIMAL (p≤65) / DEC / NUMERIC / FIXED | [N] DECIMAL(p,s) |
| MySQL | [N] DECIMAL (p≤65) / NUMERIC / FIXED | [N] DECIMAL(p,s) |
| SQLite | [A] NUMERIC affinity (stored INT or REAL) |  |
| DuckDB | [N] DECIMAL (p≤38) | [N] DECIMAL(p,s) |
| YugabyteDB | [N] numeric / decimal |  |
| TiDB | [N] DECIMAL (p≤65) | [N] DECIMAL(p,s) |
| Snowflake | [N] NUMBER (p≤38) / DECIMAL | [N] NUMBER(p,s) |
| Aurora MySQL | [N] DECIMAL (p≤65) / NUMERIC / FIXED | [V] DECIMAL(p,s) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] numeric(p≤1000,s) / decimal | [V] DECIMAL(p,s) (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] DECIMAL (p≤31) | [N] DECIMAL(p,s) |
| pengdows.flatfile | [?] DECIMAL / NUMERIC |  |
| SingleStore | [N] DECIMAL (p≤65) | [N] DECIMAL(p,s) |
| Sybase ASE | [N] decimal / numeric (p≤38) | [N] DECIMAL(p,s) |
| Spanner PG | [R] numeric (no (p,s) in DDL) | [N] NUMERIC |
| Informix | [N] DECIMAL(p,s) (p≤32) |  |
| SAP HANA | [N] DECIMAL(p≤38,s) | [N] DECIMAL(p,s) |
| InterBase | [N] DECIMAL / NUMERIC (p≤18) | [N] DECIMAL(p,s) |
| Access ACE | [N] DECIMAL (p≤28) | [N] DECIMAL(p,s) |

### B16 — Exact numeric: Decimal floating point

**ISO SQL:** `DECFLOAT` · **CLR/DbType:** `decimal (lossy >28 digits)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [A] NUMBER (no p,s) = 38-digit decimal float |  |
| Firebird | [V] DECFLOAT(16/34) (4.0+) | [V] DECFLOAT(16/34) (Firebird 4+) |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [V] DECFLOAT |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [N] DECFLOAT(16/34) | [N] DECFLOAT(16/34) |
| pengdows.flatfile | [?] DECFLOAT (SQL:2016) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [A] DECIMAL(p) without scale = floating decimal |  |
| SAP HANA | [N] SMALLDECIMAL; DECIMAL without p |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B17 — Exact numeric: Money / currency

**ISO SQL:** `—` · **CLR/DbType:** `decimal` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] money |  |
| SQL Server | [N] money / smallmoney |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [N] money |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] money | [V] MONEY (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [N] money / smallmoney |  |
| Spanner PG | [X] — |  |
| Informix | [N] MONEY(p,s) |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [N] CURRENCY / MONEY |  |

### B18 — Approximate numeric: 32-bit IEEE float

**ISO SQL:** `REAL` · **CLR/DbType:** `float / DbType.Single` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] real / float4 |  |
| SQL Server | [N] real / float(1-24) |  |
| Oracle | [N] BINARY_FLOAT (REAL = NUMBER-based!) |  |
| Firebird | [N] FLOAT / REAL |  |
| CockroachDB | [N] REAL / FLOAT4 |  |
| MariaDB | [N] FLOAT | [N] REAL |
| MySQL | [N] FLOAT | [N] REAL |
| SQLite | [A] REAL affinity (stored 64-bit) |  |
| DuckDB | [N] FLOAT / REAL / FLOAT4 |  |
| YugabyteDB | [N] real / float4 |  |
| TiDB | [N] FLOAT | [N] REAL |
| Snowflake | [A] FLOAT4 / REAL → 64-bit FLOAT |  |
| Aurora MySQL | [N] FLOAT | [V] REAL (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] real / float4 | [V] REAL (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] REAL |  |
| pengdows.flatfile | [?] REAL |  |
| SingleStore | [N] FLOAT | [N] REAL |
| Sybase ASE | [N] real |  |
| Spanner PG | [V] real / float4 | [N] FLOAT4 |
| Informix | [N] SMALLFLOAT / REAL |  |
| SAP HANA | [N] REAL |  |
| InterBase | [N] FLOAT | [N] REAL |
| Access ACE | [N] REAL / SINGLE |  |

### B19 — Approximate numeric: 64-bit IEEE double

**ISO SQL:** `DOUBLE PRECISION` · **CLR/DbType:** `double / DbType.Double` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] double precision / float8 |  |
| SQL Server | [N] float / float(25-53) | [N] DOUBLE PRECISION |
| Oracle | [N] BINARY_DOUBLE (DOUBLE PRECISION = NUMBER-based!) |  |
| Firebird | [N] DOUBLE PRECISION |  |
| CockroachDB | [N] FLOAT / FLOAT8 / DOUBLE PRECISION |  |
| MariaDB | [N] DOUBLE / DOUBLE PRECISION / REAL* |  |
| MySQL | [N] DOUBLE / DOUBLE PRECISION / REAL* |  |
| SQLite | [N] REAL |  |
| DuckDB | [N] DOUBLE / FLOAT8 | [N] DOUBLE PRECISION |
| YugabyteDB | [N] double precision / float8 |  |
| TiDB | [N] DOUBLE / DOUBLE PRECISION / REAL* |  |
| Snowflake | [N] FLOAT / DOUBLE / FLOAT8 |  |
| Aurora MySQL | [N] DOUBLE / DOUBLE PRECISION / REAL* | [V] DOUBLE PRECISION (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] double precision / float8 | [V] DOUBLE PRECISION (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] DOUBLE / FLOAT | [N] DOUBLE PRECISION |
| pengdows.flatfile | [?] DOUBLE PRECISION |  |
| SingleStore | [N] DOUBLE / DOUBLE PRECISION / REAL* |  |
| Sybase ASE | [N] double precision / float |  |
| Spanner PG | [N] double precision / float8 |  |
| Informix | [N] FLOAT / DOUBLE PRECISION |  |
| SAP HANA | [N] DOUBLE / FLOAT(n) | [N] DOUBLE PRECISION |
| InterBase | [N] DOUBLE PRECISION |  |
| Access ACE | [N] FLOAT / DOUBLE |  |

### B20 — Boolean: Boolean

**ISO SQL:** `BOOLEAN` · **CLR/DbType:** `bool / DbType.Boolean` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] boolean / bool |  |
| SQL Server | [A] bit (0/1, nullable) |  |
| Oracle | [V] BOOLEAN (23ai+) |  |
| Firebird | [V] BOOLEAN (3.0+) | [N] BOOLEAN |
| CockroachDB | [N] BOOL / BOOLEAN |  |
| MariaDB | [A] BOOL / BOOLEAN → TINYINT(1) | [A] BOOL / BOOLEAN -> TINYINT(1) |
| MySQL | [A] BOOL / BOOLEAN → TINYINT(1) | [A] BOOL / BOOLEAN -> TINYINT(1) |
| SQLite | [A] BOOLEAN → NUMERIC affinity (0/1) | [A] INTEGER (0/1) (No dedicated storage class) |
| DuckDB | [N] BOOLEAN / BOOL |  |
| YugabyteDB | [N] boolean |  |
| TiDB | [A] BOOL / BOOLEAN → TINYINT(1) | [A] BOOL / BOOLEAN -> TINYINT(1) |
| Snowflake | [N] BOOLEAN |  |
| Aurora MySQL | [A] BOOL / BOOLEAN → TINYINT(1) | [A] BOOL / BOOLEAN -> TINYINT(1) |
| Aurora PostgreSQL | [N] boolean / bool | [V] BOOLEAN (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [V] BOOLEAN (11.1.1.1+) | [N] BOOLEAN |
| pengdows.flatfile | [?] BOOLEAN |  |
| SingleStore | [A] BOOL / BOOLEAN → TINYINT(1) | [A] BOOL / BOOLEAN -> TINYINT(1) |
| Sybase ASE | [A] bit (cannot be NULL) |  |
| Spanner PG | [N] bool / boolean |  |
| Informix | [N] BOOLEAN |  |
| SAP HANA | [N] BOOLEAN |  |
| InterBase | [?] BOOLEAN (InterBase 7+) — verify | [N] BOOLEAN |
| Access ACE | [A] YESNO / BIT (True = -1) |  |

### B21 — Character: Fixed-length CHAR(n)

**ISO SQL:** `CHARACTER(n)` · **CLR/DbType:** `string / DbType.AnsiStringFixedLength` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] char(n) / bpchar |  |
| SQL Server | [N] char(n) |  |
| Oracle | [N] CHAR(n BYTE/CHAR) | [N] CHAR(n) |
| Firebird | [N] CHAR(n) |  |
| CockroachDB | [N] CHAR(n) (STRING family) |  |
| MariaDB | [N] CHAR(n) |  |
| MySQL | [N] CHAR(n) |  |
| SQLite | [A] CHAR → TEXT affinity (n ignored) |  |
| DuckDB | [A] CHAR / BPCHAR → VARCHAR (n ignored) | [A] CHAR(n) -> VARCHAR |
| YugabyteDB | [N] char(n) |  |
| TiDB | [N] CHAR(n) |  |
| Snowflake | [A] CHAR → VARCHAR (no padding) | [N] CHAR(n) |
| Aurora MySQL | [N] CHAR(n) | [V] CHAR(n) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] char(n) / bpchar | [V] CHAR(n) (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] CHAR(n) |  |
| pengdows.flatfile | [?] CHAR(n) |  |
| SingleStore | [N] CHAR(n) |  |
| Sybase ASE | [N] char(n) |  |
| Spanner PG | [X] — (fixed CHAR unsupported) |  |
| Informix | [N] CHAR(n) |  |
| SAP HANA | [?] not in HANA Cloud type list — verify | [A] VARCHAR(n) [no fixed CHAR semantics] |
| InterBase | [N] CHAR(n) |  |
| Access ACE | [N] CHAR(n) |  |

### B22 — Character: Variable-length VARCHAR(n)

**ISO SQL:** `CHARACTER VARYING(n)` · **CLR/DbType:** `string / DbType.AnsiString` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] varchar(n) |  |
| SQL Server | [N] varchar(n/max) | [N] VARCHAR(n) |
| Oracle | [N] VARCHAR2(n) ('' = NULL) |  |
| Firebird | [N] VARCHAR(n) |  |
| CockroachDB | [N] VARCHAR(n) / STRING(n) |  |
| MariaDB | [N] VARCHAR(n) |  |
| MySQL | [N] VARCHAR(n) |  |
| SQLite | [A] VARCHAR → TEXT affinity (n ignored) |  |
| DuckDB | [N] VARCHAR (n ignored) | [N] VARCHAR(n) |
| YugabyteDB | [N] varchar(n) |  |
| TiDB | [N] VARCHAR(n) |  |
| Snowflake | [N] VARCHAR(n) |  |
| Aurora MySQL | [N] VARCHAR(n) | [V] VARCHAR(n) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] varchar(n) | [V] VARCHAR(n) (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] VARCHAR(n) |  |
| pengdows.flatfile | [?] VARCHAR(n) |  |
| SingleStore | [N] VARCHAR(n) |  |
| Sybase ASE | [N] varchar(n) |  |
| Spanner PG | [N] varchar(n) / character varying |  |
| Informix | [N] VARCHAR(n≤255) / LVARCHAR | [N] VARCHAR(n) |
| SAP HANA | [N] VARCHAR(n) |  |
| InterBase | [N] VARCHAR(n) |  |
| Access ACE | [N] VARCHAR(n≤255) / TEXT(n) | [N] VARCHAR(n) |

### B23 — Character: Unbounded / long text

**ISO SQL:** `CLOB / CHARACTER LARGE OBJECT` · **CLR/DbType:** `string / DbType.String` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] text |  |
| SQL Server | [N] varchar(max) (text deprecated) | [A] VARCHAR(MAX) |
| Oracle | [N] CLOB (LONG legacy) | [A] CLOB |
| Firebird | [N] BLOB SUB_TYPE TEXT | [A] BLOB SUB_TYPE TEXT |
| CockroachDB | [N] STRING / TEXT |  |
| MariaDB | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | [A] LONGTEXT |
| MySQL | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | [A] LONGTEXT |
| SQLite | [N] TEXT |  |
| DuckDB | [N] VARCHAR / TEXT / STRING |  |
| YugabyteDB | [N] text |  |
| TiDB | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | [A] LONGTEXT |
| Snowflake | [N] STRING / TEXT (= VARCHAR max) |  |
| Aurora MySQL | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | [A] LONGTEXT |
| Aurora PostgreSQL | [N] text | [V] TEXT (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] CLOB (LONG VARCHAR deprecated) | [A] CLOB |
| pengdows.flatfile | [?] CLOB |  |
| SingleStore | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | [A] LONGTEXT |
| Sybase ASE | [N] text | [A] VARCHAR(MAX) |
| Spanner PG | [N] text |  |
| Informix | [N] TEXT (legacy) / CLOB (smart LOB) | [A] LVARCHAR / TEXT |
| SAP HANA | [N] CLOB / NCLOB | [A] NCLOB |
| InterBase | [N] BLOB SUB_TYPE 1 / TEXT | [A] BLOB SUB_TYPE TEXT |
| Access ACE | [N] LONGTEXT / MEMO | [A] LONGTEXT / MEMO |

### B24 — Character: National fixed NCHAR(n)

**ISO SQL:** `NATIONAL CHARACTER(n)` · **CLR/DbType:** `string / DbType.StringFixedLength` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [A] national character → char |  |
| SQL Server | [N] nchar(n) |  |
| Oracle | [N] NCHAR(n) |  |
| Firebird | [A] NCHAR → CHAR CHARACTER SET ISO8859_1 |  |
| CockroachDB | [X] — |  |
| MariaDB | [A] NCHAR → CHAR utf8mb3 |  |
| MySQL | [A] NCHAR → CHAR utf8mb3 |  |
| SQLite | [A] NCHAR → TEXT affinity |  |
| DuckDB | [X] — |  |
| YugabyteDB | [A] national character → char |  |
| TiDB | [A] NCHAR → CHAR |  |
| Snowflake | [A] NCHAR → VARCHAR |  |
| Aurora MySQL | [A] NCHAR → CHAR utf8mb3 |  |
| Aurora PostgreSQL | [A] national character → char |  |
| Db2 LUW | [A] NCHAR → GRAPHIC (Unicode DB) |  |
| pengdows.flatfile | [?] NATIONAL CHARACTER(n) |  |
| SingleStore | [?] verify |  |
| Sybase ASE | [N] nchar(n) |  |
| Spanner PG | [X] — |  |
| Informix | [N] NCHAR(n) (locale collation) |  |
| SAP HANA | [X] — |  |
| InterBase | [A] NCHAR → CHAR CHARACTER SET ISO8859_1 |  |
| Access ACE | [A] NCHAR → TEXT (Unicode) |  |

### B25 — Character: National varying NVARCHAR(n)

**ISO SQL:** `NATIONAL CHARACTER VARYING(n)` · **CLR/DbType:** `string / DbType.String` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [A] national character varying → varchar |  |
| SQL Server | [N] nvarchar(n/max) | [N] NCHAR / NVARCHAR |
| Oracle | [N] NVARCHAR2(n) | [N] NCHAR / NVARCHAR2 |
| Firebird | [A] NCHAR VARYING → ISO8859_1 | [N] NCHAR / NATIONAL CHAR |
| CockroachDB | [X] — |  |
| MariaDB | [A] NVARCHAR → VARCHAR utf8mb3 | [N] NATIONAL CHAR |
| MySQL | [A] NVARCHAR → VARCHAR utf8mb3 | [N] NATIONAL CHAR |
| SQLite | [A] NVARCHAR → TEXT affinity |  |
| DuckDB | [X] — |  |
| YugabyteDB | [A] → varchar |  |
| TiDB | [A] NVARCHAR → VARCHAR | [N] NATIONAL CHAR |
| Snowflake | [A] NVARCHAR → VARCHAR |  |
| Aurora MySQL | [A] NVARCHAR → VARCHAR utf8mb3 | [V] NATIONAL CHAR (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [A] national character varying → varchar |  |
| Db2 LUW | [A] NVARCHAR → VARGRAPHIC (Unicode DB) | [N] GRAPHIC / VARGRAPHIC |
| pengdows.flatfile | [?] NATIONAL CHARACTER VARYING(n) |  |
| SingleStore | [?] verify | [N] NATIONAL CHAR |
| Sybase ASE | [N] nvarchar(n) | [N] NCHAR / NVARCHAR |
| Spanner PG | [X] — |  |
| Informix | [N] NVARCHAR(n) | [N] NCHAR / NVARCHAR |
| SAP HANA | [N] NVARCHAR(n) |  |
| InterBase | [A] NCHAR VARYING → ISO8859_1 | [N] NCHAR / NATIONAL CHAR |
| Access ACE | [A] NVARCHAR → TEXT | [N] Unicode text fields |

### B26 — Character: National / Unicode LOB

**ISO SQL:** `NCLOB` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [N] nvarchar(max) (ntext deprecated) | [N] VARCHAR(MAX) / NVARCHAR(MAX) |
| Oracle | [N] NCLOB | [N] CLOB / NCLOB / LONG |
| Firebird | [X] — | [N] BLOB SUB_TYPE TEXT |
| CockroachDB | [X] — |  |
| MariaDB | [X] — | [N] LONGTEXT |
| MySQL | [X] — | [N] LONGTEXT |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — | [N] LONGTEXT |
| Snowflake | [X] — | [N] VARCHAR |
| Aurora MySQL | [X] — | [V] LONGTEXT (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [N] DBCLOB | [N] CLOB / DBCLOB |
| pengdows.flatfile | [?] NCLOB |  |
| SingleStore | [X] — | [N] LONGTEXT |
| Sybase ASE | [N] unitext | [N] VARCHAR(MAX) / NVARCHAR(MAX) |
| Spanner PG | [X] — |  |
| Informix | [X] — | [N] CLOB / TEXT |
| SAP HANA | [N] NCLOB | [N] CLOB / NCLOB |
| InterBase | [X] — | [N] BLOB SUB_TYPE TEXT |
| Access ACE | [X] — | [N] LONGTEXT / MEMO |

### B27 — Character: Unicode-specific (UTF-16) types

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [N] unichar / univarchar / unitext |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B28 — Character: Double-byte GRAPHIC types

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [N] GRAPHIC / VARGRAPHIC / LONG VARGRAPHIC (deprecated) |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B29 — Character: Vendor-specialized strings

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [?] ALPHANUM / SHORTTEXT / TEXT / BINTEXT (on-prem 2.0 only?) — verify |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B30 — Character: System-name alias

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [A] sysname → nvarchar(128) |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [A] sysname / longsysname |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B31 — Character: Case-insensitive text

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] citext |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [?] CITEXT (recent versions) — verify |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [E] citext |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [E] citext |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B32 — Binary: Fixed-length BINARY(n)

**ISO SQL:** `BINARY(n)` · **CLR/DbType:** `byte[] / DbType.Binary` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — | [A] BYTEA (no fixed-length binary) |
| SQL Server | [N] binary(n) |  |
| Oracle | [A] RAW(n) (variable) | [N] RAW(n) |
| Firebird | [V] BINARY(n) → CHAR OCTETS (4.0+) |  |
| CockroachDB | [X] — | [A] BYTEA (no fixed-length binary) |
| MariaDB | [N] BINARY(n) |  |
| MySQL | [N] BINARY(n) |  |
| SQLite | [A] BINARY → NUMERIC affinity (!) |  |
| DuckDB | [A] BINARY → BLOB | [A] BLOB (no fixed semantics) |
| YugabyteDB | [X] — | [A] BYTEA (no fixed-length binary) |
| TiDB | [N] BINARY(n) |  |
| Snowflake | [A] BINARY(n) (variable) | [N] BINARY(n) |
| Aurora MySQL | [N] BINARY(n) | [V] BINARY(n) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — | [A] BYTEA (no fixed-length binary) |
| Db2 LUW | [N] BINARY(n) |  |
| pengdows.flatfile | [?] BINARY(n) |  |
| SingleStore | [N] BINARY(n) |  |
| Sybase ASE | [N] binary(n) |  |
| Spanner PG | [X] — | [A] BYTEA (no fixed-length binary) |
| Informix | [X] — | [A] BYTE / BLOB |
| SAP HANA | [?] verify | [A] VARBINARY(n) |
| InterBase | [A] CHAR(n) CHARACTER SET OCTETS | [A] BLOB SUB_TYPE 0 (Not Firebird BINARY column semantics) |
| Access ACE | [N] BINARY(n≤255) | [N] BINARY(n) |

### B33 — Binary: Variable-length VARBINARY(n)

**ISO SQL:** `BINARY VARYING(n)` · **CLR/DbType:** `byte[] / DbType.Binary` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [A] bytea (no length) |  |
| SQL Server | [N] varbinary(n/max) | [N] VARBINARY(n) |
| Oracle | [N] RAW(n) | [A] RAW(n) |
| Firebird | [V] VARBINARY(n) (4.0+) |  |
| CockroachDB | [A] BYTES | [A] BYTEA |
| MariaDB | [N] VARBINARY(n) |  |
| MySQL | [N] VARBINARY(n) |  |
| SQLite | [A] VARBINARY → NUMERIC affinity (!) | [A] BLOB storage (No fixed binary definition) |
| DuckDB | [A] VARBINARY → BLOB |  |
| YugabyteDB | [A] bytea |  |
| TiDB | [N] VARBINARY(n) |  |
| Snowflake | [N] VARBINARY / BINARY | [N] BINARY / VARBINARY |
| Aurora MySQL | [N] VARBINARY(n) | [V] VARBINARY(n) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [A] bytea (no length) |  |
| Db2 LUW | [N] VARBINARY(n) |  |
| pengdows.flatfile | [?] VARBINARY(n) |  |
| SingleStore | [N] VARBINARY(n) |  |
| Sybase ASE | [N] varbinary(n) |  |
| Spanner PG | [A] bytea | [N] BYTEA |
| Informix | [X] — | [A] BYTE / BLOB |
| SAP HANA | [N] VARBINARY(n) |  |
| InterBase | [A] VARCHAR(n) CHARACTER SET OCTETS | [A] BLOB SUB_TYPE 0 (Not Firebird VARBINARY) |
| Access ACE | [N] VARBINARY(n) |  |

### B34 — Binary: Binary LOB

**ISO SQL:** `BLOB / BINARY LARGE OBJECT` · **CLR/DbType:** `byte[] / Stream` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] bytea | [A] BYTEA |
| SQL Server | [N] varbinary(max) (image deprecated) | [A] VARBINARY(MAX) |
| Oracle | [N] BLOB (LONG RAW legacy) |  |
| Firebird | [N] BLOB SUB_TYPE BINARY |  |
| CockroachDB | [N] BYTES / BYTEA | [A] BYTEA |
| MariaDB | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | [A] LONGBLOB |
| MySQL | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | [A] LONGBLOB |
| SQLite | [N] BLOB |  |
| DuckDB | [N] BLOB / BYTEA |  |
| YugabyteDB | [N] bytea | [A] BYTEA |
| TiDB | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | [A] LONGBLOB |
| Snowflake | [N] BINARY (size-capped) | [A] BINARY |
| Aurora MySQL | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | [A] LONGBLOB |
| Aurora PostgreSQL | [N] bytea | [A] BYTEA |
| Db2 LUW | [N] BLOB |  |
| pengdows.flatfile | [?] BLOB |  |
| SingleStore | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | [A] LONGBLOB |
| Sybase ASE | [N] image | [A] VARBINARY(MAX) |
| Spanner PG | [N] bytea | [A] BYTEA |
| Informix | [N] BYTE (legacy) / BLOB (smart LOB) |  |
| SAP HANA | [N] BLOB |  |
| InterBase | [N] BLOB SUB_TYPE 0 / BINARY |  |
| Access ACE | [N] LONGBINARY / OLEOBJECT | [A] LONGBINARY / OLEOBJECT |

### B35 — Binary: Custom BLOB subtypes

**ISO SQL:** `—` · **CLR/DbType:** `byte[]` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [N] BLOB SUB_TYPE n |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [N] BLOB SUB_TYPE n |  |
| Access ACE | [X] — |  |

### B36 — Binary: External file reference

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [N] BFILE |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [V] FILE | [N] FILE |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B37 — Binary: Bit string BIT(n) / BIT VARYING

**ISO SQL:** `— (removed in SQL:2003)` · **CLR/DbType:** `BitArray / ulong` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] bit(n) / varbit(n) | [N] BIT / VARBIT |
| SQL Server | [X] — | [A] BIT (0/1) |
| Oracle | [X] — | [A] RAW(n) [bitstring substitute] |
| Firebird | [X] — | [A] BINARY(n) [bitstring substitute] |
| CockroachDB | [N] BIT(n) / VARBIT(n) |  |
| MariaDB | [N] BIT(1-64) | [N] BIT(n) |
| MySQL | [N] BIT(1-64) | [N] BIT(n) |
| SQLite | [X] — |  |
| DuckDB | [N] BIT / BITSTRING |  |
| YugabyteDB | [N] bit(n) / varbit(n) |  |
| TiDB | [N] BIT(1-64) | [N] BIT(n) |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] BIT(1-64) | [V] BIT(n) (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] bit(n) / varbit(n) | [V] BIT / VARBIT (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (removed from ISO) |  |
| SingleStore | [N] BIT(1-64) | [N] BIT(n) |
| Sybase ASE | [X] — | [A] BIT (0/1) |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — | [A] BINARY(n) [bitstring substitute] |
| Access ACE | [X] — |  |

### B38 — Temporal: Date only

**ISO SQL:** `DATE` · **CLR/DbType:** `DateOnly / DateTime / DbType.Date` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] date |  |
| SQL Server | [N] date |  |
| Oracle | [A] DATE (includes time to 1 s!) |  |
| Firebird | [N] DATE (dialect 3) |  |
| CockroachDB | [N] DATE |  |
| MariaDB | [N] DATE |  |
| MySQL | [N] DATE |  |
| SQLite | [A] DATE → NUMERIC affinity (TEXT/REAL/INT by convention) | [A] TEXT / REAL / INTEGER (No native DATE storage) |
| DuckDB | [N] DATE |  |
| YugabyteDB | [N] date |  |
| TiDB | [N] DATE |  |
| Snowflake | [N] DATE |  |
| Aurora MySQL | [N] DATE | [V] DATE (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] date | [V] DATE (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] DATE |  |
| pengdows.flatfile | [?] DATE |  |
| SingleStore | [N] DATE |  |
| Sybase ASE | [N] date |  |
| Spanner PG | [N] date |  |
| Informix | [N] DATE |  |
| SAP HANA | [N] DATE |  |
| InterBase | [N] DATE (dialect 3) |  |
| Access ACE | [A] DATE → DATETIME | [N] DATE |

### B39 — Temporal: Time of day

**ISO SQL:** `TIME(p)` · **CLR/DbType:** `TimeOnly / TimeSpan / DbType.Time` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] time(p≤6) |  |
| SQL Server | [N] time(p≤7) |  |
| Oracle | [X] — (no TIME type) |  |
| Firebird | [N] TIME |  |
| CockroachDB | [N] TIME |  |
| MariaDB | [N] TIME(p) (duration range) |  |
| MySQL | [N] TIME(p) (duration range) |  |
| SQLite | [A] → NUMERIC affinity | [A] TEXT / REAL / INTEGER (No native TIME storage) |
| DuckDB | [N] TIME |  |
| YugabyteDB | [N] time |  |
| TiDB | [N] TIME(p) |  |
| Snowflake | [N] TIME(p≤9) |  |
| Aurora MySQL | [N] TIME(p) (duration range) | [V] TIME (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] time(p≤6) | [V] TIME (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] TIME (whole seconds) |  |
| pengdows.flatfile | [?] TIME(p) |  |
| SingleStore | [N] TIME / TIME(6) |  |
| Sybase ASE | [N] time / bigtime |  |
| Spanner PG | [X] — |  |
| Informix | [A] DATETIME HOUR TO FRACTION(n) | [N] TIME |
| SAP HANA | [N] TIME |  |
| InterBase | [N] TIME |  |
| Access ACE | [A] TIME → DATETIME | [N] TIME |

### B40 — Temporal: Time with time zone

**ISO SQL:** `TIME WITH TIME ZONE` · **CLR/DbType:** `DateTimeOffset (no CLR equivalent)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] time with time zone / timetz |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [V] TIME WITH TIME ZONE (4.0+) |  |
| CockroachDB | [N] TIMETZ |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] TIMETZ |  |
| YugabyteDB | [N] timetz |  |
| TiDB | [X] — |  |
| Snowflake | [X] — | [A] TIME (no zone; no direct match) |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] time with time zone / timetz | [V] TIME WITH TIME ZONE / TIMETZ (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [?] TIME WITH TIME ZONE |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B41 — Temporal: Timestamp without time zone

**ISO SQL:** `TIMESTAMP(p)` · **CLR/DbType:** `DateTime (Kind=Unspecified) / DbType.DateTime2` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] timestamp(p≤6) |  |
| SQL Server | [N] datetime2(p≤7) / datetime / smalldatetime |  |
| Oracle | [N] TIMESTAMP(p≤9) |  |
| Firebird | [N] TIMESTAMP (100 µs) |  |
| CockroachDB | [N] TIMESTAMP |  |
| MariaDB | [N] DATETIME(p≤6) |  |
| MySQL | [N] DATETIME(p≤6) |  |
| SQLite | [A] DATETIME → NUMERIC affinity | [A] TEXT / REAL / INTEGER (No native DATETIME storage) |
| DuckDB | [N] TIMESTAMP (µs) / TIMESTAMP_S / _MS / _NS |  |
| YugabyteDB | [N] timestamp |  |
| TiDB | [N] DATETIME(p≤6) |  |
| Snowflake | [N] TIMESTAMP_NTZ (TIMESTAMP = mapping param) |  |
| Aurora MySQL | [N] DATETIME(p≤6) | [V] DATETIME (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] timestamp(p≤6) | [V] TIMESTAMP (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] TIMESTAMP(p≤12) |  |
| pengdows.flatfile | [?] TIMESTAMP(p) |  |
| SingleStore | [N] DATETIME / DATETIME(6) |  |
| Sybase ASE | [N] datetime / smalldatetime / bigdatetime |  |
| Spanner PG | [X] — (timestamptz only) |  |
| Informix | [N] DATETIME YEAR TO FRACTION(n≤5) |  |
| SAP HANA | [N] TIMESTAMP / SECONDDATE |  |
| InterBase | [N] TIMESTAMP |  |
| Access ACE | [N] DATETIME; V: DATETIME2 'Date/Time Extended' |  |

### B42 — Temporal: Timestamp with time zone (offset/zone retained)

**ISO SQL:** `TIMESTAMP WITH TIME ZONE` · **CLR/DbType:** `DateTimeOffset / DbType.DateTimeOffset` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — (timestamptz normalizes to UTC; see next row) | [N] TIMESTAMPTZ |
| SQL Server | [N] datetimeoffset(p) |  |
| Oracle | [N] TIMESTAMP WITH TIME ZONE |  |
| Firebird | [V] TIMESTAMP WITH TIME ZONE (4.0+) |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — | [N] TIMESTAMPTZ |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [N] TIMESTAMP_TZ |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — (timestamptz normalizes to UTC; see next row) | [V] TIMESTAMPTZ (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — (LUW lacks it; z/OS only) | [N] TIMESTAMP WITH TIME ZONE |
| pengdows.flatfile | [?] TIMESTAMP WITH TIME ZONE |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — | [N] DATETIMEOFFSET |
| Spanner PG | [X] — | [N] TIMESTAMPTZ |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B43 — Temporal: Instant (UTC-normalized, session-zone rendered)

**ISO SQL:** `—` · **CLR/DbType:** `DateTime (Kind=Utc) / DateTimeOffset(+0)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] timestamptz |  |
| SQL Server | [X] — |  |
| Oracle | [N] TIMESTAMP WITH LOCAL TIME ZONE |  |
| Firebird | [X] — |  |
| CockroachDB | [N] TIMESTAMPTZ |  |
| MariaDB | [A] TIMESTAMP (session-TZ converted; range-limited) |  |
| MySQL | [A] TIMESTAMP (session-TZ converted; 2038 limit) |  |
| SQLite | [X] — |  |
| DuckDB | [N] TIMESTAMPTZ |  |
| YugabyteDB | [N] timestamptz |  |
| TiDB | [A] TIMESTAMP |  |
| Snowflake | [N] TIMESTAMP_LTZ |  |
| Aurora MySQL | [A] TIMESTAMP (session-TZ converted; 2038 limit) |  |
| Aurora PostgreSQL | [N] timestamptz |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [A] TIMESTAMP |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [N] timestamptz (ns) |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B44 — Temporal: Interval year–month

**ISO SQL:** `INTERVAL YEAR TO MONTH` · **CLR/DbType:** `— (no CLR type)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] interval (combined) | [N] INTERVAL YEAR TO MONTH |
| SQL Server | [X] — |  |
| Oracle | [N] INTERVAL YEAR TO MONTH |  |
| Firebird | [X] — |  |
| CockroachDB | [N] INTERVAL |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] INTERVAL | [A] INTERVAL |
| YugabyteDB | [N] interval |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] interval (combined) | [V] INTERVAL YEAR TO MONTH (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [?] INTERVAL YEAR TO MONTH |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [R] interval (query-only) |  |
| Informix | [N] INTERVAL YEAR TO MONTH |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B45 — Temporal: Interval day–second

**ISO SQL:** `INTERVAL DAY TO SECOND` · **CLR/DbType:** `TimeSpan` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] interval | [N] INTERVAL DAY TO SECOND |
| SQL Server | [X] — |  |
| Oracle | [N] INTERVAL DAY TO SECOND |  |
| Firebird | [X] — |  |
| CockroachDB | [N] INTERVAL |  |
| MariaDB | [A] TIME holds ±838 h durations |  |
| MySQL | [A] TIME holds ±838 h durations |  |
| SQLite | [X] — |  |
| DuckDB | [N] INTERVAL | [A] INTERVAL |
| YugabyteDB | [N] interval |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [A] TIME holds ±838 h durations |  |
| Aurora PostgreSQL | [N] interval | [V] INTERVAL DAY TO SECOND (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [?] INTERVAL DAY TO SECOND |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [R] interval (query-only) |  |
| Informix | [N] INTERVAL DAY TO FRACTION |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B46 — Temporal: Year

**ISO SQL:** `—` · **CLR/DbType:** `short` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] YEAR |  |
| MySQL | [N] YEAR |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] YEAR |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] YEAR | [V] YEAR (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] YEAR |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — | [N] DATETIME YEAR TO YEAR |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B47 — Temporal: Row version (not a time)

**ISO SQL:** `—` · **CLR/DbType:** `byte[8] / ulong` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [N] rowversion / timestamp |  |
| Oracle | [R] ORA_ROWSCN pseudocolumn |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [R] ROW CHANGE TIMESTAMP column option |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [N] timestamp (varbinary(8)) |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — | [X] no direct SQL rowversion type |

### B48 — Identifier: UUID / GUID

**ISO SQL:** `—` · **CLR/DbType:** `Guid / DbType.Guid` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] uuid |  |
| SQL Server | [N] uniqueidentifier |  |
| Oracle | [X] — (RAW(16) convention) |  |
| Firebird | [X] — (CHAR(16) OCTETS + UUID_TO_CHAR) |  |
| CockroachDB | [N] UUID |  |
| MariaDB | [V] UUID (10.7+) |  |
| MySQL | [X] — (BINARY(16) + UUID_TO_BIN) |  |
| SQLite | [X] — (TEXT or BLOB; 'UUID' → NUMERIC affinity) |  |
| DuckDB | [N] UUID |  |
| YugabyteDB | [N] uuid |  |
| TiDB | [X] — |  |
| Snowflake | [V] UUID (drivers return string) |  |
| Aurora MySQL | [X] — (BINARY(16) + UUID_TO_BIN) |  |
| Aurora PostgreSQL | [N] uuid | [V] UUID (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [V] uuid | [N] UUID |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [N] GUID / UNIQUEIDENTIFIER (ReplicationID) |  |

### B49 — Identifier: Physical row id

**ISO SQL:** `—` · **CLR/DbType:** `string / byte[]` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [R] ctid (tid system column) |  |
| SQL Server | [X] — |  |
| Oracle | [N] ROWID / UROWID |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [R] rowid |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [R] ctid (tid system column) |  |
| Db2 LUW | [?] ROWID — verify LUW support | [N] ROWID |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [R] ROWID pseudocolumn |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B50 — Identifier: Security label

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [N] DB2SECURITYLABEL (LBAC) |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [N] IDSSECURITYLABEL |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B51 — Network: IP address / CIDR

**ISO SQL:** `—` · **CLR/DbType:** `IPAddress / (IPAddress,int)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] inet / cidr |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [N] INET |  |
| MariaDB | [V] INET4 / INET6 (10.10+ / 10.5+) |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [E] INET (inet extension) |  |
| YugabyteDB | [N] inet / cidr |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] inet / cidr |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B52 — Network: MAC address

**ISO SQL:** `—` · **CLR/DbType:** `PhysicalAddress` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] macaddr / macaddr8 |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [N] macaddr / macaddr8 |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] macaddr / macaddr8 | [V] MACADDR / MACADDR8 (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B53 — Semi-structured: JSON (text-preserving)

**ISO SQL:** `JSON (SQL:2023)` · **CLR/DbType:** `string / JsonDocument` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] json |  |
| SQL Server | [V] json (2025+; nvarchar before) | [V] JSON / NVARCHAR(MAX) (Native JSON in SQL Server 2025; earlier use NVARCHAR) |
| Oracle | [V] JSON (21c+; CLOB IS JSON before) |  |
| Firebird | [X] — |  |
| CockroachDB | [A] JSON → JSONB | [A] JSON -> JSONB (JSON alias to JSONB) |
| MariaDB | [A] JSON → LONGTEXT + JSON_VALID | [A] JSON -> LONGTEXT (CHECK JSON_VALID behavior is version dependent) |
| MySQL | [N] JSON (binary storage) |  |
| SQLite | [R] TEXT + JSON functions | [A] TEXT + JSON functions (Not a native JSON storage class) |
| DuckDB | [E] JSON (json extension) |  |
| YugabyteDB | [N] json |  |
| TiDB | [N] JSON |  |
| Snowflake | [A] VARIANT | [A] VARIANT / PARSE_JSON |
| Aurora MySQL | [N] JSON (binary storage) | [V] JSON (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] json | [V] JSON (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [R] no type (JSON/BSON functions over VARCHAR/BLOB) |  |
| pengdows.flatfile | [?] JSON (SQL:2023) |  |
| SingleStore | [N] JSON |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — (jsonb only) | [A] JSONB (No JSON text type) |
| Informix | [N] JSON (opaque) |  |
| SAP HANA | [R] no column type (JSON Document Store) | [A] NVARCHAR/CLOB + JSON functions |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B54 — Semi-structured: Binary JSON

**ISO SQL:** `—` · **CLR/DbType:** `string / JsonDocument` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] jsonb |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [N] JSONB |  |
| MariaDB | [X] — |  |
| MySQL | [X] — | [A] JSON (binary storage) |
| SQLite | [V] JSONB as BLOB (3.45+) |  |
| DuckDB | [X] — |  |
| YugabyteDB | [N] jsonb |  |
| TiDB | [X] — | [A] JSON (binary storage) |
| Snowflake | [X] — | [A] VARIANT |
| Aurora MySQL | [X] — | [A] JSON (binary storage) |
| Aurora PostgreSQL | [N] jsonb | [V] JSONB (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — | [N] JSON (SingleStore-specific JSON storage) |
| Sybase ASE | [X] — |  |
| Spanner PG | [N] jsonb |  |
| Informix | [X] — | [A] BSON |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B55 — Semi-structured: BSON

**ISO SQL:** `—` · **CLR/DbType:** `byte[]` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — | [A] VARIANT (not BSON) |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [V] BSON | [N] BSON |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [N] BSON (opaque) |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B56 — Semi-structured: XML

**ISO SQL:** `XML` · **CLR/DbType:** `string / SqlXml / XmlReader` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] xml |  |
| SQL Server | [N] xml |  |
| Oracle | [N] XMLTYPE |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [R] VARIANT via PARSE_XML | [A] PARSE_XML -> VARIANT |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] xml | [V] XML (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] XML (pureXML) |  |
| pengdows.flatfile | [?] XML (SQL/XML) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [R] no type (XML services over text/image) |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B57 — Semi-structured: Variant / dynamic

**ISO SQL:** `—` · **CLR/DbType:** `object` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [N] sql_variant |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [R] ANY (STRICT tables only) |  |
| DuckDB | [X] — | [N] VARIANT |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [N] VARIANT |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — | [N] SQL_VARIANT |
| Spanner PG | [X] — |  |
| Informix | [X] — | [A] opaque types |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B58 — Composite: Array

**ISO SQL:** `ARRAY (SQL:1999)` · **CLR/DbType:** `T[] / List<T>` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] type[] (multi-dim) |  |
| SQL Server | [X] — |  |
| Oracle | [R] VARRAY / nested table (CREATE TYPE) |  |
| Firebird | [N] fixed-dim arrays (weak driver support) | [A] type[n] (legacy fixed array) |
| CockroachDB | [N] type[] (1-D) |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] LIST type[] (var) / ARRAY type[n] (fixed) |  |
| YugabyteDB | [N] type[] |  |
| TiDB | [X] — |  |
| Snowflake | [N] ARRAY / ARRAY(type) |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] type[] | [V] type[] (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [R] ARRAY UDT (SQL PL only) |  |
| pengdows.flatfile | [?] ARRAY |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [N] type[] (1-D) | [N] type[] (1 dimension) (No multidimensional arrays or array elements) |
| Informix | [N] LIST / SET / MULTISET | [A] LIST / SET / MULTISET |
| SAP HANA | [N] ARRAY | [A] ARRAY (SQLScript) |
| InterBase | [N] fixed-dim arrays | [A] type[n] (legacy fixed array) |
| Access ACE | [X] — |  |

### B59 — Composite: Struct / row / composite

**ISO SQL:** `ROW (SQL:1999)` · **CLR/DbType:** `— (custom mapping)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] composite (CREATE TYPE … AS) | [N] CREATE TYPE AS (...) |
| SQL Server | [X] — |  |
| Oracle | [N] OBJECT types | [N] OBJECT / ROW |
| Firebird | [X] — |  |
| CockroachDB | [?] user-defined composite (recent) — verify |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] STRUCT |  |
| YugabyteDB | [N] composite |  |
| TiDB | [X] — |  |
| Snowflake | [N] OBJECT(...) structured |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] composite | [V] CREATE TYPE AS (...) (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [R] ROW (SQL PL) / STRUCTURED UDT | [N] ROW / structured type |
| pengdows.flatfile | [?] ROW |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [N] ROW (named / unnamed) |  |
| SAP HANA | [X] — | [A] TABLE type / SQLScript |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B60 — Composite: Map

**ISO SQL:** `—` · **CLR/DbType:** `Dictionary<K,V>` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] hstore |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] MAP(k,v) | [N] MAP(key,value) |
| YugabyteDB | [E] hstore |  |
| TiDB | [X] — |  |
| Snowflake | [N] MAP(k,v) structured | [N] MAP(key,value) |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [E] hstore |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — | [A] ROW / collections (not MAP) |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B61 — Composite: Tagged union

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — | [A] SQL_VARIANT |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [N] UNION | [N] UNION / VARIANT (Confirm target DuckDB release for VARIANT) |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — | [A] VARIANT |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — | [A] SQL_VARIANT |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B62 — Composite: Enumeration

**ISO SQL:** `—` · **CLR/DbType:** `string / enum` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] CREATE TYPE … AS ENUM | [N] CREATE TYPE ... AS ENUM |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [N] ENUM (CREATE TYPE) |  |
| MariaDB | [N] ENUM(...) |  |
| MySQL | [N] ENUM(...) |  |
| SQLite | [X] — |  |
| DuckDB | [N] ENUM |  |
| YugabyteDB | [N] enum |  |
| TiDB | [N] ENUM(...) |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] ENUM(...) | [V] ENUM (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [N] CREATE TYPE … AS ENUM | [V] CREATE TYPE ... AS ENUM (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] ENUM(...) |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B63 — Composite: Multi-valued set

**ISO SQL:** `—` · **CLR/DbType:** `string (comma list)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [N] SET(...) |  |
| MySQL | [N] SET(...) |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [N] SET(...) |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [N] SET(...) |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [N] SET(...) |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [A] SET collection (different semantics) |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [R] multivalued Lookup (DAO only) |  |

### B64 — Composite: Range

**ISO SQL:** `—` · **CLR/DbType:** `NpgsqlRange<T> (provider-specific)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] int4range / int8range / numrange / tsrange / tstzrange / daterange |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [?] range types — verify |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] range types | [V] INT4RANGE / INT8RANGE / NUMRANGE / TSRANGE / TSTZRANGE / DATERANGE (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B65 — Composite: Multirange

**ISO SQL:** `—` · **CLR/DbType:** `provider-specific` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [V] *multirange (14+) | [V] INT4MULTIRANGE / etc. (PostgreSQL 14+) |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [?] verify (PG15-based releases) |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [V] *multirange (14+) | [V] INT4MULTIRANGE / etc. (PostgreSQL 14+) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B66 — User-defined: Domain / distinct type

**ISO SQL:** `DOMAIN / DISTINCT (SQL:1999)` · **CLR/DbType:** `underlying type` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] DOMAIN | [N] CREATE TYPE ENUM / DOMAIN |
| SQL Server | [N] alias type (CREATE TYPE … FROM) |  |
| Oracle | [V] DOMAIN (23ai) | [N] CREATE TYPE |
| Firebird | [N] DOMAIN | [N] CREATE DOMAIN |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — | [N] ENUM |
| YugabyteDB | [N] DOMAIN |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] DOMAIN | [V] CREATE TYPE ENUM / DOMAIN (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] DISTINCT type | [N] CREATE DISTINCT TYPE |
| pengdows.flatfile | [?] DOMAIN |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [N] DISTINCT type | [N] DISTINCT / ROW |
| SAP HANA | [X] — |  |
| InterBase | [N] DOMAIN | [N] CREATE DOMAIN |
| Access ACE | [X] — |  |

### B67 — User-defined: Opaque / CLR / extension base type

**ISO SQL:** `—` · **CLR/DbType:** `provider-specific` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] CREATE TYPE (C base types) | [N] CREATE TYPE BASE / COMPOSITE |
| SQL Server | [N] CLR UDT | [N] CREATE TYPE / CLR UDT |
| Oracle | [N] object types | [N] CREATE TYPE OBJECT / VARRAY |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — | [A] CREATE TYPE alias |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — | [A] UDTF/structured types (restricted) |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [R] allow-listed extensions only | [V] CREATE TYPE BASE / COMPOSITE (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [N] STRUCTURED type | [N] CREATE TYPE STRUCTURED / DISTINCT |
| pengdows.flatfile | [X] — (not ISO) |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — | [N] CREATE TYPE / CLR UDT |
| Spanner PG | [X] — |  |
| Informix | [N] opaque types (DataBlade) | [N] OPAQUE / DISTINCT / ROW |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B68 — Spatial: Planar geometry

**ISO SQL:** `SQL/MM ST_Geometry` · **CLR/DbType:** `provider-specific (NTS)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] geometry (PostGIS) | [E] built-in geometric types / PostGIS geometry |
| SQL Server | [N] geometry |  |
| Oracle | [E] SDO_GEOMETRY |  |
| Firebird | [X] — |  |
| CockroachDB | [N] GEOMETRY |  |
| MariaDB | [N] GEOMETRY / POINT / LINESTRING / POLYGON / MULTI* / GEOMETRYCOLLECTION |  |
| MySQL | [N] GEOMETRY / POINT / LINESTRING / POLYGON / MULTI* / GEOMETRYCOLLECTION |  |
| SQLite | [E] SpatiaLite |  |
| DuckDB | [E] GEOMETRY (spatial extension) | [E] GEOMETRY extension |
| YugabyteDB | [E] PostGIS | [E] built-in geometric types / PostGIS geometry |
| TiDB | [X] — (spatial unsupported) |  |
| Snowflake | [N] GEOMETRY |  |
| Aurora MySQL | [N] GEOMETRY / POINT / LINESTRING / POLYGON / MULTI* / GEOMETRYCOLLECTION | [V] GEOMETRY (Depends on Aurora-compatible engine major version) |
| Aurora PostgreSQL | [E] PostGIS | [E] built-in geometric types / PostGIS geometry |
| Db2 LUW | [E] ST_GEOMETRY (spatial support) |  |
| pengdows.flatfile | [X] — (SQL/MM, not core) |  |
| SingleStore | [X] — | [N] GEOMETRY |
| Sybase ASE | [X] — | [N] GEOMETRY |
| Spanner PG | [X] — |  |
| Informix | [E] ST_GEOMETRY (Spatial DataBlade) |  |
| SAP HANA | [N] ST_GEOMETRY / ST_POINT |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B69 — Spatial: Geodetic geography

**ISO SQL:** `—` · **CLR/DbType:** `provider-specific (NTS)` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] geography (PostGIS) | [E] PostGIS geography |
| SQL Server | [N] geography |  |
| Oracle | [E] SDO_GEOMETRY (geodetic SRID) |  |
| Firebird | [X] — |  |
| CockroachDB | [N] GEOGRAPHY |  |
| MariaDB | [X] — |  |
| MySQL | [A] GEOMETRY with geographic SRID (8.0+) |  |
| SQLite | [X] — |  |
| DuckDB | [X] — | [E] GEOMETRY extension |
| YugabyteDB | [E] PostGIS | [E] PostGIS geography |
| TiDB | [X] — |  |
| Snowflake | [N] GEOGRAPHY |  |
| Aurora MySQL | [A] GEOMETRY with geographic SRID (8.0+) |  |
| Aurora PostgreSQL | [E] PostGIS | [E] PostGIS geography |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [N] GEOGRAPHY / GEOGRAPHYPOINT |  |
| Sybase ASE | [X] — | [N] GEOGRAPHY |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [A] ST_GEOMETRY with round-earth SRS | [N] ST_GEOMETRY |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B70 — Spatial: Built-in 2-D geometric primitives

**ISO SQL:** `—` · **CLR/DbType:** `NpgsqlPoint etc.` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] point / line / lseg / box / path / polygon / circle |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [R] BOX2D only |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [N] point / line / lseg / box / path / polygon / circle |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] point / line / lseg / box / path / polygon / circle |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B71 — Vector / AI: Vector embedding

**ISO SQL:** `—` · **CLR/DbType:** `float[] / ReadOnlyMemory<float>` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] vector / halfvec / sparsevec (pgvector) |  |
| SQL Server | [V] vector(n) (2025+) |  |
| Oracle | [V] VECTOR (23ai+) |  |
| Firebird | [X] — |  |
| CockroachDB | [?] VECTOR (24.x+) — verify |  |
| MariaDB | [V] VECTOR(n) (11.7+) |  |
| MySQL | [V] VECTOR(n) (9.0+, not 8.4 LTS) | [V] VECTOR (not in standard MySQL 8.4) |
| SQLite | [E] sqlite-vec |  |
| DuckDB | [R] FLOAT[n] + vss extension | [A] FLOAT[dimension] (No dedicated VECTOR type in baseline) |
| YugabyteDB | [E] pgvector |  |
| TiDB | [V] VECTOR(n) (8.4+/8.5) — verify |  |
| Snowflake | [N] VECTOR(FLOAT/INT, n) | [V] VECTOR(FLOAT,dimension) / VECTOR(INT,dimension) (Version / account dependent) |
| Aurora MySQL | [X] — (Aurora MySQL 3 = MySQL 8.0) | [V] VECTOR (not in standard MySQL 8.4) |
| Aurora PostgreSQL | [E] pgvector |  |
| Db2 LUW | [?] VECTOR (12.1.x) — verify |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [V] VECTOR |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — (float8[] convention) |  |
| Informix | [X] — |  |
| SAP HANA | [V] REAL_VECTOR (HANA Cloud) | [V] REAL_VECTOR / HALF_VECTOR (HANA version dependent) |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B72 — Text search: Full-text document/query

**ISO SQL:** `—` · **CLR/DbType:** `string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] tsvector / tsquery |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [V] TSVECTOR / TSQUERY (23.1+) |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [N] tsvector / tsquery |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] tsvector / tsquery | [V] TSVECTOR / TSQUERY (Depends on Aurora-compatible engine major version) |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B73 — Hierarchy: Hierarchy path

**ISO SQL:** `—` · **CLR/DbType:** `SqlHierarchyId` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] ltree |  |
| SQL Server | [N] hierarchyid |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [E] ltree |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B74 — System: Object id / reg* / xid / lsn / snapshot

**ISO SQL:** `—` · **CLR/DbType:** `uint / string` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] oid / reg* / xid / xid8 / cid / tid / pg_lsn / pg_snapshot |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [R] OID + reg* (compat) |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [?] subset — verify |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [N] same as PG major |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B75 — System: Pseudo / non-column types

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [R] record / void / any* / trigger / cstring / internal |  |
| SQL Server | [R] cursor / table |  |
| Oracle | [R] PL/SQL-only (PLS_INTEGER, assoc. arrays) |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [R] record / void / any* / trigger / cstring / internal |  |
| Db2 LUW | [R] CURSOR / ROW |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [R] SQLScript-only types |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B76 — System: Commit timestamp

**ISO SQL:** `—` · **CLR/DbType:** `DateTime` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [R] spanner.commit_timestamp option on timestamptz |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [X] — |  |

### B77 — Access-only: Hyperlink / Attachment / Calculated / OLE

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** Pengdows workbook.

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [X] — |  |
| SQL Server | [X] — |  |
| Oracle | [X] — |  |
| Firebird | [X] — |  |
| CockroachDB | [X] — |  |
| MariaDB | [X] — |  |
| MySQL | [X] — |  |
| SQLite | [X] — |  |
| DuckDB | [X] — |  |
| YugabyteDB | [X] — |  |
| TiDB | [X] — |  |
| Snowflake | [X] — |  |
| Aurora MySQL | [X] — |  |
| Aurora PostgreSQL | [X] — |  |
| Db2 LUW | [X] — |  |
| pengdows.flatfile | [X] — |  |
| SingleStore | [X] — |  |
| Sybase ASE | [X] — |  |
| Spanner PG | [X] — |  |
| Informix | [X] — |  |
| SAP HANA | [X] — |  |
| InterBase | [X] — |  |
| Access ACE | [R] DAO-only field types |  |

### A26 — Character: set-valued enum

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [?] ? |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [N] SET |  |
| MySQL | [N] SET |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [N] SET |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [V] SET |  |
| Aurora PostgreSQL | [?] ? |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [N] SET |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [?] ? |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A30 — Binary: legacy long binary

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [?] ? |  |
| SQL Server | [N] IMAGE |  |
| Oracle | [N] LONG RAW |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [N] LONGBLOB |  |
| MySQL | [N] LONGBLOB |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [N] LONGBLOB |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [V] LONGBLOB |  |
| Aurora PostgreSQL | [?] ? |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [N] LONGBLOB |  |
| Sybase ASE | [N] IMAGE |  |
| Spanner PG | [?] ? |  |
| Informix | [N] BYTE |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [N] IMAGE / OLEOBJECT |  |

### A38 — Temporal: second-precision datetime

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [?] ? |  |
| SQL Server | [A] SMALLDATETIME |  |
| Oracle | [N] DATE |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [?] ? |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [A] SMALLDATETIME |  |
| Spanner PG | [?] ? |  |
| Informix | [?] ? |  |
| SAP HANA | [N] SECONDDATE |  |
| InterBase | [?] ? |  |
| Access ACE | [A] DATETIME |  |

### A42 — Temporal: generic interval

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] INTERVAL |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] INTERVAL |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [N] INTERVAL |  |
| YugabyteDB | [?] INTERVAL |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] INTERVAL |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [A] INTERVAL (query-only) |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A45 — Identifiers: object identifier / OID

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] OID / REGCLASS / REGTYPE / REG* |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] OID / REGCLASS / REGTYPE / REG* |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] OID / REGCLASS / REGTYPE / REG* |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A46 — Identifiers: transaction ID

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] XID / XID8 / CID |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [X] — |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] XID / XID8 / CID |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A47 — Identifiers: WAL / log position

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] PG_LSN |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [X] — |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] PG_LSN |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A53 — Network: IP address

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] INET |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] INET |  |
| MariaDB | [V] INET4 / INET6 |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] INET |  |
| TiDB | [?] ? |  |
| Snowflake | [V] IPADDRESS |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] INET |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [A] VARCHAR representation |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A54 — Network: IP network / CIDR

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] CIDR |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [A] INET |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] CIDR |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [V] CIDR |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A56 — Spatial: point geometry

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] POINT |  |
| SQL Server | [N] GEOMETRY::Point() |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [N] GEOMETRY(POINT) |  |
| MariaDB | [N] POINT |  |
| MySQL | [N] POINT |  |
| SQLite | [?] ? |  |
| DuckDB | [E] GEOMETRY extension |  |
| YugabyteDB | [?] POINT |  |
| TiDB | [X] — |  |
| Snowflake | [N] GEOMETRY |  |
| Aurora MySQL | [V] POINT |  |
| Aurora PostgreSQL | [V] POINT |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [N] POINT |  |
| Sybase ASE | [N] GEOMETRY::Point() |  |
| Spanner PG | [X] — |  |
| Informix | [E] spatial DataBlade |  |
| SAP HANA | [N] ST_POINT |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A57 — Spatial: line / linestring geometry

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] LINE / LSEG / PATH |  |
| SQL Server | [N] GEOMETRY |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [N] GEOMETRY(LINESTRING) |  |
| MariaDB | [N] LINESTRING |  |
| MySQL | [N] LINESTRING |  |
| SQLite | [?] ? |  |
| DuckDB | [E] GEOMETRY extension |  |
| YugabyteDB | [?] LINE / LSEG / PATH |  |
| TiDB | [X] — |  |
| Snowflake | [N] GEOMETRY |  |
| Aurora MySQL | [V] LINESTRING |  |
| Aurora PostgreSQL | [V] LINE / LSEG / PATH |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [N] LINESTRING |  |
| Sybase ASE | [N] GEOMETRY |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [N] ST_GEOMETRY |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A58 — Spatial: polygon geometry

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [N] POLYGON |  |
| SQL Server | [N] GEOMETRY |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [N] GEOMETRY(POLYGON) |  |
| MariaDB | [N] POLYGON |  |
| MySQL | [N] POLYGON |  |
| SQLite | [?] ? |  |
| DuckDB | [E] GEOMETRY extension |  |
| YugabyteDB | [?] POLYGON |  |
| TiDB | [X] — |  |
| Snowflake | [N] GEOMETRY |  |
| Aurora MySQL | [V] POLYGON |  |
| Aurora PostgreSQL | [V] POLYGON |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [N] POLYGON |  |
| Sybase ASE | [N] GEOMETRY |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [N] ST_GEOMETRY |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A62 — Collections: fixed-length array

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [A] type[n] (not enforced as fixed) |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [N] type[n] |  |
| CockroachDB | [A] type[n] (not enforced as fixed) |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [N] type[n] / ARRAY |  |
| YugabyteDB | [A] type[n] (not enforced as fixed) |  |
| TiDB | [?] ? |  |
| Snowflake | [A] ARRAY(type) |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [A] type[n] (not enforced as fixed) |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [X] — |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [N] type[n] |  |
| Access ACE | [?] ? |  |

### A63 — Collections: list / multiset / set collection

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [A] type[] |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [A] type[] |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [N] LIST |  |
| YugabyteDB | [A] type[] |  |
| TiDB | [?] ? |  |
| Snowflake | [A] ARRAY |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [A] type[] |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [A] type[] |  |
| Informix | [N] LIST / SET / MULTISET |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A74 — Specialized: opaque / special database type

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [?] ? |  |
| SQL Server | [N] SQL_VARIANT |  |
| Oracle | [N] BFILE / ANYDATA |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [?] ? |  |
| Snowflake | [N] FILE |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [?] ? |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [N] SQL_VARIANT |  |
| Spanner PG | [?] ? |  |
| Informix | [N] OPAQUE / IDSSECURITYLABEL |  |
| SAP HANA | [N] ALPHANUM / SHORTTEXT |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A76 — Specialized: spatial raster / other extensions

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [E] PostGIS raster / extension-defined |  |
| SQL Server | [?] ? |  |
| Oracle | [?] ? |  |
| Firebird | [?] ? |  |
| CockroachDB | [E] PostGIS raster / extension-defined |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [E] PostGIS raster / extension-defined |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [E] PostGIS raster / extension-defined |  |
| Db2 LUW | [?] ? |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [?] ? |  |
| Spanner PG | [E] PostGIS raster / extension-defined |  |
| Informix | [?] ? |  |
| SAP HANA | [?] ? |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

### A77 — Specialized: cursor / table variable

**ISO SQL:** `—` · **CLR/DbType:** `—` · **Source:** First workbook (distinct concept).

| Database | Classification and SQL declaration | Alternate source / caveat |
|---|---|---|
| PostgreSQL | [?] ? |  |
| SQL Server | [A] CURSOR / TABLE (variable only) |  |
| Oracle | [A] REF CURSOR (PL/SQL) |  |
| Firebird | [?] ? |  |
| CockroachDB | [?] ? |  |
| MariaDB | [?] ? |  |
| MySQL | [?] ? |  |
| SQLite | [?] ? |  |
| DuckDB | [?] ? |  |
| YugabyteDB | [?] ? |  |
| TiDB | [?] ? |  |
| Snowflake | [?] ? |  |
| Aurora MySQL | [?] ? |  |
| Aurora PostgreSQL | [?] ? |  |
| Db2 LUW | [A] CURSOR / ROW (restricted) |  |
| pengdows.flatfile | [?] ? |  |
| SingleStore | [?] ? |  |
| Sybase ASE | [A] CURSOR / TABLE (variable only) |  |
| Spanner PG | [?] ? |  |
| Informix | [?] ? |  |
| SAP HANA | [A] TABLE TYPE (SQLScript) |  |
| InterBase | [?] ? |  |
| Access ACE | [?] ? |  |

## Classification disagreement register

Disagreements are not necessarily engine bugs: some arise because one workbook calls an alternate representation native and the other calls it an alias, or because the versions differ. These require review before conformance assertions.

| Concept | Database | Pengdows workbook | First workbook | First-workbook caveat |
|---|---|---|---|---|
| 8-bit signed | SQL Server | [X] — | A SMALLINT | No signed byte type |
| 8-bit signed | CockroachDB | [X] — | A INT2 | No int1 |
| 8-bit signed | Aurora MySQL | [N] TINYINT | V TINYINT | Depends on Aurora-compatible engine major version |
| 8-bit signed | Sybase ASE | [X] — | A TINYINT is unsigned; use SMALLINT | No signed 8-bit native TINYINT |
| 8-bit signed | SAP HANA | [X] — | A SMALLINT | TINYINT is unsigned |
| 8-bit signed | Access ACE | [X] — | A SMALLINT | BYTE is unsigned |
| 8-bit unsigned | Aurora MySQL | [N] TINYINT UNSIGNED | V TINYINT UNSIGNED | Depends on Aurora-compatible engine major version |
| 16-bit signed | Aurora MySQL | [N] SMALLINT | V SMALLINT | Depends on Aurora-compatible engine major version |
| 16-bit signed | Aurora PostgreSQL | [N] smallint / int2 | V SMALLINT | Depends on Aurora-compatible engine major version |
| 16-bit signed | Spanner PG | [X] — (bigint only) | A INT8 | No int2 storage |
| 16-bit unsigned | Aurora MySQL | [N] SMALLINT UNSIGNED | V SMALLINT UNSIGNED | Depends on Aurora-compatible engine major version |
| 32-bit signed | CockroachDB | [N] INT4 (INT = INT8 unless default_int_size=4) | A INT4 (alias subject to width behavior) |  |
| 32-bit signed | Aurora MySQL | [N] INT / INTEGER | V INT | Depends on Aurora-compatible engine major version |
| 32-bit signed | Aurora PostgreSQL | [N] integer / int4 / int | V INTEGER | Depends on Aurora-compatible engine major version |
| 32-bit unsigned | Aurora MySQL | [N] INT UNSIGNED | V INT UNSIGNED | Depends on Aurora-compatible engine major version |
| 64-bit signed | Oracle | [X] — (use NUMBER(19)) | A NUMBER(19,0) | NUMERIC representation; precision/range not necessarily identical |
| 64-bit signed | Aurora MySQL | [N] BIGINT | V BIGINT | Depends on Aurora-compatible engine major version |
| 64-bit signed | Aurora PostgreSQL | [N] bigint / int8 | V BIGINT | Depends on Aurora-compatible engine major version |
| 64-bit unsigned | Aurora MySQL | [N] BIGINT UNSIGNED | V BIGINT UNSIGNED | Depends on Aurora-compatible engine major version |
| 128-bit signed | Oracle | [X] — | A NUMBER(38,0) | Not full signed 128-bit integer range |
| 128-bit signed | Snowflake | [X] — | A NUMBER(38,0) | Not full signed 128-bit integer range |
| Arbitrary-precision integer | DuckDB | [V] BIGNUM (formerly VARINT) — verify name per version | N BIGNUM |  |
| Identity column (declaration, not a type) | PostgreSQL | [R] GENERATED … AS IDENTITY (10+) | A SERIAL / IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | SQL Server | [R] IDENTITY(s,i) | A IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Oracle | [R] GENERATED … AS IDENTITY (12c+) | A GENERATED AS IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Firebird | [R] GENERATED BY DEFAULT AS IDENTITY (3.0+) | A GENERATED BY DEFAULT AS IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | CockroachDB | [R] GENERATED … AS IDENTITY | A SERIAL / IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | MariaDB | [R] AUTO_INCREMENT | A AUTO_INCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | MySQL | [R] AUTO_INCREMENT | A AUTO_INCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | SQLite | [R] INTEGER PRIMARY KEY [AUTOINCREMENT] | A INTEGER PRIMARY KEY [AUTOINCREMENT] | Rowid behavior |
| Identity column (declaration, not a type) | DuckDB | [R] sequence + DEFAULT nextval() | A SEQUENCE + DEFAULT nextval | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | YugabyteDB | [R] GENERATED … AS IDENTITY | A SERIAL / IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | TiDB | [R] AUTO_INCREMENT / AUTO_RANDOM | A AUTO_INCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Snowflake | [R] IDENTITY / AUTOINCREMENT | A IDENTITY / AUTOINCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Aurora MySQL | [R] AUTO_INCREMENT | A AUTO_INCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Aurora PostgreSQL | [R] GENERATED … AS IDENTITY (10+) | A SERIAL / IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Db2 LUW | [R] GENERATED … AS IDENTITY | A GENERATED AS IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | SingleStore | [R] AUTO_INCREMENT | A AUTO_INCREMENT | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Sybase ASE | [R] IDENTITY on numeric(n,0) | A IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Informix | [R] SERIAL / SERIAL8 / BIGSERIAL | A SERIAL / SERIAL8 / BIGSERIAL | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | SAP HANA | [R] GENERATED … AS IDENTITY | A GENERATED AS IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | InterBase | [X] — (generator + trigger) | A GENERATED BY DEFAULT AS IDENTITY | Column generation syntax, not distinct storage type |
| Identity column (declaration, not a type) | Access ACE | [R] COUNTER / AUTOINCREMENT | A COUNTER / AUTOINCREMENT | Column generation syntax, not distinct storage type |
| Fixed-point DECIMAL(p,s) | Aurora MySQL | [N] DECIMAL (p≤65) / NUMERIC / FIXED | V DECIMAL(p,s) | Depends on Aurora-compatible engine major version |
| Fixed-point DECIMAL(p,s) | Aurora PostgreSQL | [N] numeric(p≤1000,s) / decimal | V DECIMAL(p,s) | Depends on Aurora-compatible engine major version |
| Fixed-point DECIMAL(p,s) | Spanner PG | [R] numeric (no (p,s) in DDL) | N NUMERIC |  |
| Money / currency | Aurora PostgreSQL | [N] money | V MONEY | Depends on Aurora-compatible engine major version |
| 32-bit IEEE float | Aurora MySQL | [N] FLOAT | V REAL | Depends on Aurora-compatible engine major version |
| 32-bit IEEE float | Aurora PostgreSQL | [N] real / float4 | V REAL | Depends on Aurora-compatible engine major version |
| 32-bit IEEE float | Spanner PG | [V] real / float4 | N FLOAT4 |  |
| 64-bit IEEE double | Aurora MySQL | [N] DOUBLE / DOUBLE PRECISION / REAL* | V DOUBLE PRECISION | Depends on Aurora-compatible engine major version |
| 64-bit IEEE double | Aurora PostgreSQL | [N] double precision / float8 | V DOUBLE PRECISION | Depends on Aurora-compatible engine major version |
| Boolean | Firebird | [V] BOOLEAN (3.0+) | N BOOLEAN |  |
| Boolean | Aurora PostgreSQL | [N] boolean / bool | V BOOLEAN | Depends on Aurora-compatible engine major version |
| Boolean | Db2 LUW | [V] BOOLEAN (11.1.1.1+) | N BOOLEAN |  |
| Fixed-length CHAR(n) | Snowflake | [A] CHAR → VARCHAR (no padding) | N CHAR(n) |  |
| Fixed-length CHAR(n) | Aurora MySQL | [N] CHAR(n) | V CHAR(n) | Depends on Aurora-compatible engine major version |
| Fixed-length CHAR(n) | Aurora PostgreSQL | [N] char(n) / bpchar | V CHAR(n) | Depends on Aurora-compatible engine major version |
| Variable-length VARCHAR(n) | Aurora MySQL | [N] VARCHAR(n) | V VARCHAR(n) | Depends on Aurora-compatible engine major version |
| Variable-length VARCHAR(n) | Aurora PostgreSQL | [N] varchar(n) | V VARCHAR(n) | Depends on Aurora-compatible engine major version |
| Unbounded / long text | SQL Server | [N] varchar(max) (text deprecated) | A VARCHAR(MAX) |  |
| Unbounded / long text | Oracle | [N] CLOB (LONG legacy) | A CLOB |  |
| Unbounded / long text | Firebird | [N] BLOB SUB_TYPE TEXT | A BLOB SUB_TYPE TEXT |  |
| Unbounded / long text | MariaDB | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | A LONGTEXT |  |
| Unbounded / long text | MySQL | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | A LONGTEXT |  |
| Unbounded / long text | TiDB | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | A LONGTEXT |  |
| Unbounded / long text | Aurora MySQL | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | A LONGTEXT |  |
| Unbounded / long text | Aurora PostgreSQL | [N] text | V TEXT | Depends on Aurora-compatible engine major version |
| Unbounded / long text | Db2 LUW | [N] CLOB (LONG VARCHAR deprecated) | A CLOB |  |
| Unbounded / long text | SingleStore | [N] TINYTEXT / TEXT / MEDIUMTEXT / LONGTEXT | A LONGTEXT |  |
| Unbounded / long text | Sybase ASE | [N] text | A VARCHAR(MAX) |  |
| Unbounded / long text | Informix | [N] TEXT (legacy) / CLOB (smart LOB) | A LVARCHAR / TEXT |  |
| Unbounded / long text | SAP HANA | [N] CLOB / NCLOB | A NCLOB |  |
| Unbounded / long text | InterBase | [N] BLOB SUB_TYPE 1 / TEXT | A BLOB SUB_TYPE TEXT |  |
| Unbounded / long text | Access ACE | [N] LONGTEXT / MEMO | A LONGTEXT / MEMO |  |
| National varying NVARCHAR(n) | Firebird | [A] NCHAR VARYING → ISO8859_1 | N NCHAR / NATIONAL CHAR |  |
| National varying NVARCHAR(n) | MariaDB | [A] NVARCHAR → VARCHAR utf8mb3 | N NATIONAL CHAR |  |
| National varying NVARCHAR(n) | MySQL | [A] NVARCHAR → VARCHAR utf8mb3 | N NATIONAL CHAR |  |
| National varying NVARCHAR(n) | TiDB | [A] NVARCHAR → VARCHAR | N NATIONAL CHAR |  |
| National varying NVARCHAR(n) | Aurora MySQL | [A] NVARCHAR → VARCHAR utf8mb3 | V NATIONAL CHAR | Depends on Aurora-compatible engine major version |
| National varying NVARCHAR(n) | Db2 LUW | [A] NVARCHAR → VARGRAPHIC (Unicode DB) | N GRAPHIC / VARGRAPHIC |  |
| National varying NVARCHAR(n) | InterBase | [A] NCHAR VARYING → ISO8859_1 | N NCHAR / NATIONAL CHAR |  |
| National varying NVARCHAR(n) | Access ACE | [A] NVARCHAR → TEXT | N Unicode text fields |  |
| National / Unicode LOB | Firebird | [X] — | N BLOB SUB_TYPE TEXT |  |
| National / Unicode LOB | MariaDB | [X] — | N LONGTEXT |  |
| National / Unicode LOB | MySQL | [X] — | N LONGTEXT |  |
| National / Unicode LOB | TiDB | [X] — | N LONGTEXT |  |
| National / Unicode LOB | Snowflake | [X] — | N VARCHAR |  |
| National / Unicode LOB | Aurora MySQL | [X] — | V LONGTEXT | Depends on Aurora-compatible engine major version |
| National / Unicode LOB | SingleStore | [X] — | N LONGTEXT |  |
| National / Unicode LOB | Informix | [X] — | N CLOB / TEXT |  |
| National / Unicode LOB | InterBase | [X] — | N BLOB SUB_TYPE TEXT |  |
| National / Unicode LOB | Access ACE | [X] — | N LONGTEXT / MEMO |  |
| Fixed-length BINARY(n) | PostgreSQL | [X] — | A BYTEA (no fixed-length binary) |  |
| Fixed-length BINARY(n) | Oracle | [A] RAW(n) (variable) | N RAW(n) |  |
| Fixed-length BINARY(n) | CockroachDB | [X] — | A BYTEA (no fixed-length binary) |  |
| Fixed-length BINARY(n) | YugabyteDB | [X] — | A BYTEA (no fixed-length binary) |  |
| Fixed-length BINARY(n) | Snowflake | [A] BINARY(n) (variable) | N BINARY(n) |  |
| Fixed-length BINARY(n) | Aurora MySQL | [N] BINARY(n) | V BINARY(n) | Depends on Aurora-compatible engine major version |
| Fixed-length BINARY(n) | Aurora PostgreSQL | [X] — | A BYTEA (no fixed-length binary) |  |
| Fixed-length BINARY(n) | Spanner PG | [X] — | A BYTEA (no fixed-length binary) |  |
| Fixed-length BINARY(n) | Informix | [X] — | A BYTE / BLOB |  |
| Variable-length VARBINARY(n) | Oracle | [N] RAW(n) | A RAW(n) |  |
| Variable-length VARBINARY(n) | Aurora MySQL | [N] VARBINARY(n) | V VARBINARY(n) | Depends on Aurora-compatible engine major version |
| Variable-length VARBINARY(n) | Spanner PG | [A] bytea | N BYTEA |  |
| Variable-length VARBINARY(n) | Informix | [X] — | A BYTE / BLOB |  |
| Binary LOB | PostgreSQL | [N] bytea | A BYTEA |  |
| Binary LOB | SQL Server | [N] varbinary(max) (image deprecated) | A VARBINARY(MAX) |  |
| Binary LOB | CockroachDB | [N] BYTES / BYTEA | A BYTEA |  |
| Binary LOB | MariaDB | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | A LONGBLOB |  |
| Binary LOB | MySQL | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | A LONGBLOB |  |
| Binary LOB | YugabyteDB | [N] bytea | A BYTEA |  |
| Binary LOB | TiDB | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | A LONGBLOB |  |
| Binary LOB | Snowflake | [N] BINARY (size-capped) | A BINARY |  |
| Binary LOB | Aurora MySQL | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | A LONGBLOB |  |
| Binary LOB | Aurora PostgreSQL | [N] bytea | A BYTEA |  |
| Binary LOB | SingleStore | [N] TINYBLOB / BLOB / MEDIUMBLOB / LONGBLOB | A LONGBLOB |  |
| Binary LOB | Sybase ASE | [N] image | A VARBINARY(MAX) |  |
| Binary LOB | Spanner PG | [N] bytea | A BYTEA |  |
| Binary LOB | Access ACE | [N] LONGBINARY / OLEOBJECT | A LONGBINARY / OLEOBJECT |  |
| External file reference | Snowflake | [V] FILE | N FILE |  |
| Bit string BIT(n) / BIT VARYING | SQL Server | [X] — | A BIT (0/1) |  |
| Bit string BIT(n) / BIT VARYING | Oracle | [X] — | A RAW(n) [bitstring substitute] |  |
| Bit string BIT(n) / BIT VARYING | Firebird | [X] — | A BINARY(n) [bitstring substitute] |  |
| Bit string BIT(n) / BIT VARYING | Aurora MySQL | [N] BIT(1-64) | V BIT(n) | Depends on Aurora-compatible engine major version |
| Bit string BIT(n) / BIT VARYING | Aurora PostgreSQL | [N] bit(n) / varbit(n) | V BIT / VARBIT | Depends on Aurora-compatible engine major version |
| Bit string BIT(n) / BIT VARYING | Sybase ASE | [X] — | A BIT (0/1) |  |
| Bit string BIT(n) / BIT VARYING | InterBase | [X] — | A BINARY(n) [bitstring substitute] |  |
| Date only | Aurora MySQL | [N] DATE | V DATE | Depends on Aurora-compatible engine major version |
| Date only | Aurora PostgreSQL | [N] date | V DATE | Depends on Aurora-compatible engine major version |
| Date only | Access ACE | [A] DATE → DATETIME | N DATE |  |
| Time of day | Aurora MySQL | [N] TIME(p) (duration range) | V TIME | Depends on Aurora-compatible engine major version |
| Time of day | Aurora PostgreSQL | [N] time(p≤6) | V TIME | Depends on Aurora-compatible engine major version |
| Time of day | Informix | [A] DATETIME HOUR TO FRACTION(n) | N TIME |  |
| Time of day | Access ACE | [A] TIME → DATETIME | N TIME |  |
| Time with time zone | Snowflake | [X] — | A TIME (no zone; no direct match) |  |
| Time with time zone | Aurora PostgreSQL | [N] time with time zone / timetz | V TIME WITH TIME ZONE / TIMETZ | Depends on Aurora-compatible engine major version |
| Timestamp without time zone | Aurora MySQL | [N] DATETIME(p≤6) | V DATETIME | Depends on Aurora-compatible engine major version |
| Timestamp without time zone | Aurora PostgreSQL | [N] timestamp(p≤6) | V TIMESTAMP | Depends on Aurora-compatible engine major version |
| Timestamp with time zone (offset/zone retained) | PostgreSQL | [X] — (timestamptz normalizes to UTC; see next row) | N TIMESTAMPTZ |  |
| Timestamp with time zone (offset/zone retained) | DuckDB | [X] — | N TIMESTAMPTZ |  |
| Timestamp with time zone (offset/zone retained) | Aurora PostgreSQL | [X] — (timestamptz normalizes to UTC; see next row) | V TIMESTAMPTZ | Depends on Aurora-compatible engine major version |
| Timestamp with time zone (offset/zone retained) | Db2 LUW | [X] — (LUW lacks it; z/OS only) | N TIMESTAMP WITH TIME ZONE |  |
| Timestamp with time zone (offset/zone retained) | Sybase ASE | [X] — | N DATETIMEOFFSET |  |
| Timestamp with time zone (offset/zone retained) | Spanner PG | [X] — | N TIMESTAMPTZ |  |
| Interval year–month | DuckDB | [N] INTERVAL | A INTERVAL |  |
| Interval year–month | Aurora PostgreSQL | [N] interval (combined) | V INTERVAL YEAR TO MONTH | Depends on Aurora-compatible engine major version |
| Interval day–second | DuckDB | [N] INTERVAL | A INTERVAL |  |
| Interval day–second | Aurora PostgreSQL | [N] interval | V INTERVAL DAY TO SECOND | Depends on Aurora-compatible engine major version |
| Year | Aurora MySQL | [N] YEAR | V YEAR | Depends on Aurora-compatible engine major version |
| Year | Informix | [X] — | N DATETIME YEAR TO YEAR |  |
| UUID / GUID | Aurora PostgreSQL | [N] uuid | V UUID | Depends on Aurora-compatible engine major version |
| UUID / GUID | Spanner PG | [V] uuid | N UUID |  |
| MAC address | Aurora PostgreSQL | [N] macaddr / macaddr8 | V MACADDR / MACADDR8 | Depends on Aurora-compatible engine major version |
| JSON (text-preserving) | SQLite | [R] TEXT + JSON functions | A TEXT + JSON functions | Not a native JSON storage class |
| JSON (text-preserving) | Aurora MySQL | [N] JSON (binary storage) | V JSON | Depends on Aurora-compatible engine major version |
| JSON (text-preserving) | Aurora PostgreSQL | [N] json | V JSON | Depends on Aurora-compatible engine major version |
| JSON (text-preserving) | Spanner PG | [X] — (jsonb only) | A JSONB | No JSON text type |
| JSON (text-preserving) | SAP HANA | [R] no column type (JSON Document Store) | A NVARCHAR/CLOB + JSON functions |  |
| Binary JSON | MySQL | [X] — | A JSON (binary storage) |  |
| Binary JSON | TiDB | [X] — | A JSON (binary storage) |  |
| Binary JSON | Snowflake | [X] — | A VARIANT |  |
| Binary JSON | Aurora MySQL | [X] — | A JSON (binary storage) |  |
| Binary JSON | Aurora PostgreSQL | [N] jsonb | V JSONB | Depends on Aurora-compatible engine major version |
| Binary JSON | SingleStore | [X] — | N JSON | SingleStore-specific JSON storage |
| Binary JSON | Informix | [X] — | A BSON |  |
| BSON | Snowflake | [X] — | A VARIANT (not BSON) |  |
| BSON | SingleStore | [V] BSON | N BSON |  |
| XML | Snowflake | [R] VARIANT via PARSE_XML | A PARSE_XML -> VARIANT |  |
| XML | Aurora PostgreSQL | [N] xml | V XML | Depends on Aurora-compatible engine major version |
| Variant / dynamic | DuckDB | [X] — | N VARIANT |  |
| Variant / dynamic | Sybase ASE | [X] — | N SQL_VARIANT |  |
| Variant / dynamic | Informix | [X] — | A opaque types |  |
| Array | Firebird | [N] fixed-dim arrays (weak driver support) | A type[n] (legacy fixed array) |  |
| Array | Aurora PostgreSQL | [N] type[] | V type[] | Depends on Aurora-compatible engine major version |
| Array | Informix | [N] LIST / SET / MULTISET | A LIST / SET / MULTISET |  |
| Array | SAP HANA | [N] ARRAY | A ARRAY (SQLScript) |  |
| Array | InterBase | [N] fixed-dim arrays | A type[n] (legacy fixed array) |  |
| Struct / row / composite | Aurora PostgreSQL | [N] composite | V CREATE TYPE AS (...) | Depends on Aurora-compatible engine major version |
| Struct / row / composite | Db2 LUW | [R] ROW (SQL PL) / STRUCTURED UDT | N ROW / structured type |  |
| Struct / row / composite | SAP HANA | [X] — | A TABLE type / SQLScript |  |
| Map | Informix | [X] — | A ROW / collections (not MAP) |  |
| Tagged union | SQL Server | [X] — | A SQL_VARIANT |  |
| Tagged union | Snowflake | [X] — | A VARIANT |  |
| Tagged union | Sybase ASE | [X] — | A SQL_VARIANT |  |
| Enumeration | Aurora MySQL | [N] ENUM(...) | V ENUM | Depends on Aurora-compatible engine major version |
| Enumeration | Aurora PostgreSQL | [N] CREATE TYPE … AS ENUM | V CREATE TYPE ... AS ENUM | Depends on Aurora-compatible engine major version |
| Range | Aurora PostgreSQL | [N] range types | V INT4RANGE / INT8RANGE / NUMRANGE / TSRANGE / TSTZRANGE / DATERANGE | Depends on Aurora-compatible engine major version |
| Domain / distinct type | Oracle | [V] DOMAIN (23ai) | N CREATE TYPE |  |
| Domain / distinct type | DuckDB | [X] — | N ENUM |  |
| Domain / distinct type | Aurora PostgreSQL | [N] DOMAIN | V CREATE TYPE ENUM / DOMAIN | Depends on Aurora-compatible engine major version |
| Opaque / CLR / extension base type | DuckDB | [X] — | A CREATE TYPE alias |  |
| Opaque / CLR / extension base type | Snowflake | [X] — | A UDTF/structured types (restricted) |  |
| Opaque / CLR / extension base type | Aurora PostgreSQL | [R] allow-listed extensions only | V CREATE TYPE BASE / COMPOSITE | Depends on Aurora-compatible engine major version |
| Opaque / CLR / extension base type | Sybase ASE | [X] — | N CREATE TYPE / CLR UDT |  |
| Planar geometry | Aurora MySQL | [N] GEOMETRY / POINT / LINESTRING / POLYGON / MULTI* / GEOMETRYCOLLECTION | V GEOMETRY | Depends on Aurora-compatible engine major version |
| Planar geometry | SingleStore | [X] — | N GEOMETRY |  |
| Planar geometry | Sybase ASE | [X] — | N GEOMETRY |  |
| Geodetic geography | DuckDB | [X] — | E GEOMETRY extension |  |
| Geodetic geography | Sybase ASE | [X] — | N GEOGRAPHY |  |
| Geodetic geography | SAP HANA | [A] ST_GEOMETRY with round-earth SRS | N ST_GEOMETRY |  |
| Vector embedding | DuckDB | [R] FLOAT[n] + vss extension | A FLOAT[dimension] | No dedicated VECTOR type in baseline |
| Vector embedding | Snowflake | [N] VECTOR(FLOAT/INT, n) | V VECTOR(FLOAT,dimension) / VECTOR(INT,dimension) | Version / account dependent |
| Vector embedding | Aurora MySQL | [X] — (Aurora MySQL 3 = MySQL 8.0) | V VECTOR (not in standard MySQL 8.4) |  |
| Full-text document/query | Aurora PostgreSQL | [N] tsvector / tsquery | V TSVECTOR / TSQUERY | Depends on Aurora-compatible engine major version |

## Corrections and evidence supplied in the Pengdows workbook

These notes were provided in the source workbook. Confidence is the source author’s value, not a verification score from this merge.

| # | Original claim | Proposed correction | Evidence | Source confidence |
|---|---|---|---|---|
| 1 | Db2 LUW: 'Zoned timestamps — TIMESTAMP WITH TIME ZONE' | Db2 LUW has no TIMESTAMP WITH TIME ZONE column type; only Db2 for z/OS (V11+) does. | IBM Ideas DB24LUW-I-495 (opened 2015, still 'Future consideration' as of Feb 2025): https://ibm-data-and-ai.ideas.ibm.com/ideas/DB24LUW-I-495 | 0.9 |
| 2 | MySQL: 'no native general-purpose UUID, ARRAY, or INTERVAL'; VECTOR not listed | MySQL 9.0+ has VECTOR(N) (4-byte floats, default 2048, max 16383 entries; not usable as a key). 8.0/8.4 LTS do not. | MySQL 9.0/9.4 Reference Manual, 'The VECTOR Type': https://dev.mysql.com/doc/refman/9.0/en/vector.html | 0.95 |
| 3 | CockroachDB INT listed alongside INT4 with no width warning | INT is an alias for INT8 (64-bit) by default — same hazard the source flagged only for Spanner. Controlled by default_int_size. | CockroachDB INT docs: https://www.cockroachlabs.com/docs/stable/int | 0.95 |
| 4 | SQLite affinity table: BOOLEAN under both INTEGER and NUMERIC; unrecognized declarations → BLOB | BOOLEAN → NUMERIC only. BLOB affinity applies only when the declared type contains 'BLOB' or is empty; any other unrecognized name (BINARY, VARBINARY, UUID, DATE) gets NUMERIC. | SQLite 'Datatypes In SQLite' §3.1 affinity rules 1–5 (from knowledge; not re-fetched this pass) | 0.9 |
| 5 | PostgreSQL: 'PostgreSQL 18 also has native uuid support' | uuid has been a native type since PG 8.3. PG 18 added the uuidv7() generator function, not the type. | PostgreSQL release history (from knowledge) | 0.9 |
| 6 | Oracle numeric aliases REAL / DOUBLE PRECISION / FLOAT listed as floating point | These are NUMBER-based decimal FLOAT(b) types, not IEEE 754. Only BINARY_FLOAT / BINARY_DOUBLE are IEEE (NaN/Inf). Providers return decimal for the aliases. | Oracle SQL Language Reference, ANSI type conversion table (from knowledge) | 0.9 |
| 7 | Snowflake UUID (unsourced in original) | Confirmed as a column type; Snowflake drivers treat values as text strings and the type is unsupported in hybrid tables and Snowpark → ADO.NET will report string, not Guid. | Snowflake docs, UUID data type: https://docs.snowflake.com/sql-reference/data-types-uuid | 0.95 |
| 8 | ASE: 'XML functionality, including XML-related type support' | ASE has no XML column datatype; XML is stored in text/image/varchar and queried through XML Services functions. | SAP ASE XML Services guide (from knowledge) | 0.75 |
| 9 | InterBase: BIGINT listed; BOOLEAN missing | I'm not aware of a BIGINT keyword in InterBase (64-bit is via NUMERIC(18,0) in dialect 3); InterBase does have BOOLEAN (added in 7.x). Both need confirming against Embarcadero's current type reference. | From knowledge; not verified this pass | 0.6 |
| 10 | SAP HANA: ALPHANUM, SHORTTEXT, TEXT, BINTEXT as current types | These are deprecated / restricted to HANA 2.0 on-prem column store and are not in the HANA Cloud type list. Gate by deployment. | From knowledge; not verified this pass | 0.7 |
| 11 | Count of products: '22 named' then 'correction: 23 named plus Unknown'; Aurora MySQL/PG numbered 13–14 but also listed as 'variants without separate enum members' | The source contradicts itself. Resolve against SupportedDatabase.cs; this workbook keeps Aurora as columns because the source listed them as members. | Internal inconsistency in the pasted text | 1 |
| 12 | Access BOOLEAN/INTEGER (implicit) | Access SQL INTEGER is 32-bit (DAO 'Long'); SMALLINT is DAO 'Integer'. YESNO stores True as -1, which breaks naive bool conversions. | Microsoft Access SQL data types (from knowledge) | 0.85 |
| 13 | ASE BIT (implicit) | ASE bit columns cannot be NULL — unlike SQL Server bit. A nullable bool round-trip test must expect a DDL failure on ASE. | SAP ASE Reference Manual (from knowledge) | 0.85 |

## Vendor documentation links carried forward from source workbook

| Product/family | Reference | Notes |
|---|---|---|
| pg | https://www.postgresql.org/docs/current/datatype.html | Vendor documentation; version must be selected |
| mysql | https://dev.mysql.com/doc/refman/8.4/en/data-types.html | Vendor documentation; version must be selected |
| tsql | https://learn.microsoft.com/en-us/sql/t-sql/data-types/data-types-transact-sql | Vendor documentation; version must be selected |
| fb | https://www.firebirdsql.org/file/documentation/html/en/refdocs/fblangref50/firebird-50-language-reference.html | Vendor documentation; version must be selected |
| oracle | https://docs.oracle.com/en/database/oracle/oracle-database/23/sqlrf/Data-Types.html | Vendor documentation; version must be selected |
| sqlite | https://www.sqlite.org/datatype3.html | Vendor documentation; version must be selected |
| duck | https://duckdb.org/docs/current/sql/data_types/overview | Vendor documentation; version must be selected |
| snow | https://docs.snowflake.com/en/sql-reference-data-types | Vendor documentation; version must be selected |
| db2 | https://www.ibm.com/docs/en/db2/11.5?topic=elements-data-types | Vendor documentation; version must be selected |
| informix | https://www.ibm.com/docs/en/informix-servers/14.10.0?topic=types-built-in-data | Vendor documentation; version must be selected |
| hana | https://help.sap.com/docs/SAP_HANA_PLATFORM | Vendor documentation; version must be selected |
| access | https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/sql-data-types | Vendor documentation; version must be selected |
| flat | https://github.com/pengdows/pengdows.flatfile | Vendor documentation; version must be selected |
| cockroach | https://www.cockroachlabs.com/docs/stable/data-types | Vendor documentation; version must be selected |
| spanner | https://docs.cloud.google.com/spanner/docs/reference/postgresql/data-types | Vendor documentation; version must be selected |
| maria | https://mariadb.com/docs/server/reference/data-types | Vendor documentation; version must be selected |
| singlestore | https://docs.singlestore.com/cloud/reference/sql-reference/data-types/ | Vendor documentation; version must be selected |
| yugabyte | https://docs.yugabyte.com/stable/api/ysql/datatypes/ | Vendor documentation; version must be selected |
| tidb | https://docs.pingcap.com/tidb/stable/data-type-overview/ | Vendor documentation; version must be selected |
| interbase | https://docwiki.embarcadero.com/InterBase/15/en/Data_Types_(Language_Reference_Guide) | Vendor documentation; version must be selected |
| sybase | https://help.sap.com/docs/SAP_ASE | Vendor documentation; version must be selected |
| TimescaleDB | https://www.postgresql.org/docs/current/datatype.html | PostgreSQL extension; extension-defined types not exhaustively enumerated |
| Citus | https://www.postgresql.org/docs/current/datatype.html | PostgreSQL extension; distributed differences need testing |
| Fujitsu Enterprise Postgres | https://www.postgresql.org/docs/current/datatype.html | PostgreSQL derived; check Fujitsu-specific supported versions |
| Percona Server | https://dev.mysql.com/doc/refman/8.4/en/data-types.html | MySQL-compatible distribution; check matching MySQL major release |
| Aurora PostgreSQL | https://www.postgresql.org/docs/current/datatype.html | Match AWS Aurora engine release to PostgreSQL major release |
| Aurora MySQL | https://dev.mysql.com/doc/refman/8.4/en/data-types.html | Match AWS Aurora release to MySQL compatible release |
| Spanner Omni | https://docs.cloud.google.com/spanner/docs/reference/postgresql/data-types | Spanner PostgreSQL interface via compatible adapter; see product docs |
| SupportedDatabase enum | (user-provided file) | Product list and exclusions; Unknown is not a database |

## Next validation step

For each supported engine/version: enumerate native type catalog, compare to `DbConnection.GetSchema("DataTypes")`, create a column or parameter where allowed, and test null handling plus min/max/precision/scale and round-trip fidelity. Keep SQL support, ADO.NET metadata exposure, and ADO.NET fidelity as **separate test outcomes**.
