#!/usr/bin/env python3
# Generates the integration type-fidelity sources from the live probe results.
#
#   TypeFidelityDeclarations.cs  per database: the first declaration (candidate order) the server accepted for each
#                                fidelity type, with the provider type reported; and, for each type the database has
#                                no column for, the server's refusal.
#   TypeFidelityCells.cs         one entity per fidelity type (the shared row class), one per database where a native
#                                type is declared (its NativeType), the list of all types, and the row each database uses.
#
# The per-database row entities of earlier generations are not generated: a cell's table holds one column of its own type.
# Decisions recorded in code (not probe output): WIDENED (no unsigned type -> the type the library binds) and
# PREFER (a BitArray is variable-length). See docs/datatype-clr-mapping-proposal.md.
import glob, json, os, sys

S = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1]

# (column, semantic type from the probe, CLR type, DbType, C# sample value, comparison kind)
SPEC = [
    ("signed_8_bit", "signed 8-bit", "sbyte", "SByte", "(sbyte)-5", "eq"),
    ("signed_16_bit", "signed 16-bit", "short", "Int16", "(short)-300", "eq"),
    ("signed_32_bit", "signed 32-bit", "int", "Int32", "-42", "eq"),
    ("signed_64_bit", "signed 64-bit", "long", "Int64", "long.MinValue", "eq"),
    ("signed_128_bit", "signed 128-bit", "Int128", "Object", "(Int128)long.MaxValue * 4", "eq"),
    ("unsigned_8_bit", "unsigned 8-bit", "byte", "Byte", "(byte)200", "eq"),
    ("unsigned_16_bit", "unsigned 16-bit", "ushort", "UInt16", "(ushort)60000", "eq"),
    ("unsigned_32_bit", "unsigned 32-bit", "uint", "UInt32", "4000000000u", "eq"),
    ("unsigned_64_bit", "unsigned 64-bit", "ulong", "UInt64", "ulong.MaxValue", "eq"),
    ("arbitrary_integer", "arbitrary-size integer", "BigInteger", "Object",
     "BigInteger.Parse(\"123456789012345678901234567890\")", "eq"),
    ("binary_float32", "binary float32", "float", "Single", "1.5f", "eq"),
    ("binary_float64", "binary float64", "double", "Double", "0.1", "eq"),
    ("fixed_decimal", "fixed decimal", "decimal", "Decimal", "123.4500m", "decimal"),
    ("boolean", "boolean", "bool", "Boolean", "true", "eq"),
    ("bit_string", "bit / bitstring", "BitArray", "Object", "new BitArray(new[] { true, false, true })", "bits"),
    ("varying_character", "varying character", "string", "String", "\"héllo\"", "eq"),
    ("unbounded_text", "unbounded text", "string", "String", "\"héllo, world\"", "eq"),
    ("uuid", "UUID / GUID", "Guid", "Guid", "new Guid(\"0f8fad5b-d9cb-469f-a165-70867728950e\")", "eq"),
    ("calendar_date", "calendar date", "DateOnly", "Date", "new DateOnly(2026, 10, 5)", "eq"),
    ("time_of_day", "time without zone", "TimeOnly", "Time", "new TimeOnly(13, 45, 30)", "eq"),
    ("time_span", "time without zone", "TimeSpan", "Time", "new TimeSpan(13, 45, 30)", "eq"),
    ("day_time_interval", "day-time interval", "IntervalDaySecond", "Object",
     "IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4))", "interval"),
    ("timestamp", "timestamp without zone", "DateTime", "DateTime2",
     "new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc)", "instant"),
    ("timestamp_with_zone", "timestamp with zone", "DateTimeOffset", "DateTimeOffset",
     "new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2))", "instant"),
    ("binary", "binary LOB", "byte[]", "Binary", "new byte[] { 1, 2, 255 }", "bytes"),
    ("json", "JSON text / JSON value", "JsonDocument", "Object", "JsonDocument.Parse(\"{\\\"a\\\":1}\")", "json"),
    ("ip_address", "IP address", "IPAddress", "Object", "IPAddress.Parse(\"192.168.1.10\")", "text"),
    ("int_array", "SQL array", "int[]", "Object", "new[] { 1, 2, 3 }", "sequence"),
    # --- every other type with a CLR column form (docs/datatype-clr-mapping-proposal.md) ---
    ("unsigned_128_bit", "unsigned 128-bit", "UInt128", "Object", "UInt128.MaxValue", "eq"),
    ("decimal_float", "decimal floating point", "decimal", "Decimal", "1.5m", "decimal"),
    ("money", "money / currency", "decimal", "Decimal", "12.34m", "decimal"),
    ("fixed_character", "fixed character", "string", "String", "\"abcdefghij\"", "eq"),
    ("national_text", "national / Unicode character", "string", "String", "\"naïve ünicode\"", "eq"),
    ("long_text", "long text LOB", "string", "String", "\"a longer text value\"", "eq"),
    ("enumeration", "enumeration", "string", "String", "\"active\"", "eq"),
    ("set_value", "set-valued enum", "string", "String", "\"a,b\"", "eq"),
    ("fixed_binary", "fixed binary", "byte[]", "Binary", "new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }", "bytes"),
    ("varying_binary", "varying binary", "byte[]", "Binary", "new byte[] { 9, 8, 7 }", "bytes"),
    ("legacy_binary", "legacy long binary", "byte[]", "Binary", "new byte[] { 4, 5 }", "bytes"),
    ("rowversion", "rowversion / timestamp token", "byte[]", "Binary", "new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 }", "bytes"),
    ("local_timestamp", "local-zone timestamp", "DateTime", "DateTime", "new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc)", "instant"),
    ("second_datetime", "second-precision datetime", "DateTime", "DateTime", "new DateTime(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc)", "instant"),
    ("time_zoned", "time with zone", "DateTimeOffset", "Time", "new DateTimeOffset(2026, 10, 5, 13, 45, 30, TimeSpan.FromHours(2))", "instant"),
    ("year_only", "year-only", "short", "Int16", "(short)2026", "eq"),
    ("year_month_interval", "year-month interval", "IntervalYearMonth", "Object", "new IntervalYearMonth(2, 3)", "eq"),
    ("generic_interval", "generic interval", "PostgreSqlInterval", "Object", "new PostgreSqlInterval(14, 3, 1234567)", "eq"),
    ("row_identifier", "row identifier / ROWID", "string", "String", "\"AAAAAAAAAAAAAAAAAA\"", "eq"),
    ("oid", "object identifier / OID", "uint", "UInt32", "16384u", "eq"),
    ("transaction_id", "transaction ID", "ulong", "UInt64", "4294967296ul", "eq"),
    ("wal_position", "WAL / log position", "string", "String", "\"0/16B3748\"", "eq"),
    # A native binary JSON column (PostgreSQL jsonb) is a JSON document: its text is normalised by the database (key
    # spacing, key order), so the value is compared as a document, as the JSON cell is.
    ("binary_json", "native binary JSON", "JsonDocument", "Object", "JsonDocument.Parse(\"{\\\"b\\\":2}\")", "json"),
    ("xml", "XML", "string", "String", "\"<a>1</a>\"", "eq"),
    ("bson", "BSON", "byte[]", "Binary", "new byte[] { 5, 0, 0, 0, 0 }", "bytes"),
    ("variant", "variant / dynamic value", "object", "Object", "(object)\"variant text\"", "eq"),
    ("cidr", "IP network / CIDR", "Cidr", "Object", "Cidr.Parse(\"10.0.0.0/8\")", "eq"),
    ("mac_address", "MAC address", "MacAddress", "Object", "MacAddress.Parse(\"08:00:2b:01:02:03\")", "eq"),
    ("point", "point geometry", "Geometry", "Object", "Geometry.FromWellKnownText(\"POINT (1 2)\", 4326)", "eq"),
    ("linestring", "line / linestring geometry", "Geometry", "Object", "Geometry.FromWellKnownText(\"LINESTRING (0 0, 1 1)\", 4326)", "eq"),
    ("polygon", "polygon geometry", "Geometry", "Object", "Geometry.FromWellKnownText(\"POLYGON ((0 0, 1 0, 1 1, 0 0))\", 4326)", "eq"),
    ("geometry", "general geometry", "Geometry", "Object", "Geometry.FromWellKnownText(\"POINT (1 2)\", 4326)", "eq"),
    ("geography", "geography / geodetic", "Geography", "Object", "Geography.FromWellKnownText(\"POINT (1 2)\", 4326)", "eq"),
    ("fixed_array", "fixed-length array", "int[]", "Object", "new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }", "sequence"),
    # A collection is an array in the library (docs/advanced-types.md: Informix LIST / SET / MULTISET map to T[]).
    ("list_collection", "list / multiset / set collection", "int[]", "Object", "new int[] { 1, 2, 3 }", "sequence"),
    ("map", "map / dictionary", "Dictionary<string,object>", "Object", "new Dictionary<string, object> { [\"a\"] = \"1\" }", "sequence"),
    ("range", "range", "Range<int>", "Object", "new Range<int>(1, 10, true, false)", "eq"),
    ("multirange", "multirange", "Range<int>[]", "Object", "new[] { new Range<int>(1, 5, true, false), new Range<int>(7, 9, true, false) }", "sequence"),
    ("domain", "user-defined enum / domain", "string", "String", "\"domain text\"", "eq"),
    # The canonical tsquery text: PostgreSQL quotes each lexeme, so "fat & rat" reads back as "'fat' & 'rat'".
    ("full_text", "full-text search vector / query", "string", "String", "\"'fat' & 'rat'\"", "eq"),
    ("native_point", "native point", "PostgreSqlPoint", "Object", "new PostgreSqlPoint(1, 2)", "eq"),
    ("native_polygon", "native polygon", "PostgreSqlPolygon", "Object", "PostgreSqlPolygon.Parse(\"((0,0),(1,0),(1,1),(0,0))\")", "eq"),
    ("xid32", "32-bit transaction ID", "uint", "UInt32", "4000000000u", "eq"),
    ("vector", "vector embedding", "float[]", "Object", "new float[] { 1f, 2.5f, 3f }", "sequence"),
    ("hierarchy", "hierarchical path", "HierarchyId", "Object", "HierarchyId.Parse(\"/1/2/\")", "eq"),
    ("file_reference", "external-file reference", "string", "String", "\"DATA_DIR/file.bin\"", "eq"),
    ("security_label", "specialized security label", "string", "String", "\"label\"", "eq"),
]

# Where the database has no unsigned type, the column is the next signed type up: the type the library binds the
# value as on that database (BoundDbTypes.txt). Used only when the probe accepted no native declaration.
WIDENED = {
    "unsigned 8-bit": "SMALLINT",
    "unsigned 16-bit": "INTEGER",
    "unsigned 32-bit": "BIGINT",
    "unsigned 64-bit": "NUMERIC(20,0)",
}

# Where the first accepted declaration would store the value at a fixed size the sample does not fit, the column is
# the declared shape that matches the CLR type. A BitArray has a variable length: BIT(8) refuses a 3-bit value.
PREFER = {
    "bit / bitstring": ("BIT VARYING", "VARBIT"),
    # ROWVERSION is the native version type where the server takes it (SQL Server, SQLite); BINARY(8) is the fallback.
    "rowversion / timestamp token": ("ROWVERSION",),
}

# Where a database's column affinity would change the stored value, the declaration is one whose affinity keeps it. SQLite
# gives a column named BIT, or a wide integer, NUMERIC affinity: "101" becomes 101 (the leading zeros are lost), and a
# 39-digit integer becomes a REAL (lossy). It also accepts any type name, so HUGEINT or BIGINT UNSIGNED get integer
# affinity (64-bit) and are no place for a 128-bit value. A TEXT-affinity column keeps the text the library writes exactly.
# A declaration the library's storage format needs: the column must be this one, or the cell is refused. The value is the
# prefix of the declaration the probe must have accepted. Without the match nothing else is written, so a column the
# library cannot read back is never declared in its place.
DB_REQUIRED = {
    # InformixDialect maps DbType.Time to DATETIME HOUR TO FRACTION(5); the HOUR TO SECOND form drops the fractional seconds.
    ("Informix", "time without zone"): "DATETIME HOUR TO FRACTION(5)",
    # InterBase's GuidFormat is Binary: the library writes the 16 bytes, which only an OCTETS column holds.
    ("InterBase", "UUID / GUID"): "CHAR(16) CHARACTER SET OCTETS",
    # HANA's GuidFormat is String: the library writes the GUID's text.
    ("SapHana", "UUID / GUID"): "CHAR(36)",
    # The fidelity sample is SRID 4326; a HANA column without an SRID is SRID 0 and refuses it.
    ("SapHana", "point geometry"): "ST_GEOMETRY(4326)",
    ("SapHana", "line / linestring geometry"): "ST_GEOMETRY(4326)",
    ("SapHana", "polygon geometry"): "ST_GEOMETRY(4326)",
    ("SapHana", "general geometry"): "ST_GEOMETRY(4326)",
    ("SapHana", "geography / geodetic"): "ST_GEOMETRY(4326)",
    # pengdows.flatfile's INTERVAL YEAR TO MONTH is text (ISO 8601), since months have no fixed length.
    ("FlatFile", "year-month interval"): "INTERVAL YEAR TO MONTH",
    # InterBase's TIMESTAMP keeps the time of day; its DATE does not.
    ("InterBase", "second-precision datetime"): "TIMESTAMP",
    # NCHAR is Latin-1 on InterBase; a VARCHAR on the NONE charset round-trips any text, as the driver's NONE is pinned to UTF-8.
    ("InterBase", "national / Unicode character"): "VARCHAR(100)",
}

DB_PREFER = {
    # SQLite accepts any type name, so the probe's XID is an unknown name that stores the value by affinity; the 32-bit
    # transaction ID is an unsigned 32-bit value, stored as BIGINT (as unsigned 32-bit is).
    ("Sqlite", "32-bit transaction ID"): ("BIGINT", "INTEGER"),
    # HANA's TINYINT holds 0..255: the probe accepts it at create, but a signed 8-bit value of -5 is out of its range.
    # SMALLINT holds the whole signed 8-bit range (the same choice as Informix's unsigned 8-bit above).
    ("SapHana", "signed 8-bit"): ("SMALLINT", "SMALLINT"),
    # Informix's BYTE is a binary blob type: the driver refuses an integer in it ("Illegal attempt to convert Text/Byte blob type",
    # confirmed live 2026-11-06). SMALLINT holds the 0..255 range of an unsigned 8-bit value.
    ("Informix", "unsigned 8-bit"): ("SMALLINT", "SMALLINT"),
    ("Oracle", "external-file reference"): ("BFILE", "bfile"),
    # Db2 LBAC's column type (CREATE TABLE ... DB2SECURITYLABEL SECURITY POLICY POL, live 2026-11-11).
    ("Db2", "specialized security label"): ("DB2SECURITYLABEL", "DB2SECURITYLABEL"),
    ("FlatFile", "second-precision datetime"): ("TIMESTAMP", "timestamp"),
    # A fixed-width BIT(3) for the 3-bit sample: the column keeps its width, which the entity's NativeType supplies on read.
    ("MySql", "bit / bitstring"): ("BIT(3)", "bit(3)"),
    ("MariaDb", "bit / bitstring"): ("BIT(3)", "bit(3)"),
    ("TiDb", "bit / bitstring"): ("BIT(3)", "bit(3)"),
    ("SingleStore", "bit / bitstring"): ("BIT(3)", "bit(3)"),
    # NCHAR is accepted by PostgreSQL too; its national text is VARCHAR (the unicode text type).
    ("PostgreSql", "national / Unicode character"): ("VARCHAR(100)", "character varying(100)"),
    ("PostgreSql", "native point"): ("POINT", "point"),
    ("PostgreSql", "native polygon"): ("POLYGON", "polygon"),
    ("PostgreSql", "transaction ID"): ("XID8", "xid8"),
    ("PostgreSql", "32-bit transaction ID"): ("XID", "xid"),
    ("PostgreSql", "full-text search vector / query"): ("TSQUERY", "tsquery"),
    ("YugabyteDb", "native point"): ("POINT", "point"),
    ("YugabyteDb", "native polygon"): ("POLYGON", "polygon"),
    ("YugabyteDb", "transaction ID"): ("XID8", "xid8"),
    ("YugabyteDb", "32-bit transaction ID"): ("XID", "xid"),
    ("YugabyteDb", "full-text search vector / query"): ("TSQUERY", "tsquery"),
    ("CockroachDb", "full-text search vector / query"): ("TSQUERY", "tsquery"),
    ("Sqlite", "bit / bitstring"): ("VARCHAR(100)", "varchar"),
    ("Sqlite", "arbitrary-size integer"): ("TEXT", "text"),
    ("Sqlite", "signed 128-bit"): ("TEXT", "text"),
    ("Sqlite", "unsigned 128-bit"): ("TEXT", "text"),
    ("Sqlite", "unsigned 64-bit"): ("TEXT", "text"),
    # A signed 8-bit value is negative: SQL Server's and Sybase's TINYINT is unsigned (-5 overflows), so the column is the
    # next signed type up. Spanner (its PostgreSQL interface) has no INT2 and no NUMERIC precision, so its column is BIGINT and NUMERIC.
    ("SqlServer", "signed 8-bit"): ("SMALLINT", "smallint"),
    ("SybaseASE", "signed 8-bit"): ("SMALLINT", "smallint"),
    # Access's TINYINT is its unsigned BYTE: -5 overflows, so the column is SMALLINT (a 16-bit integer).
    ("Access", "signed 8-bit"): ("SMALLINT", "DBTYPE_I2"),
    ("Spanner", "signed 8-bit"): ("BIGINT", "bigint"),
    ("Spanner", "unsigned 8-bit"): ("BIGINT", "bigint"),
    ("Spanner", "unsigned 64-bit"): ("NUMERIC", "numeric"),
    # Oracle has no unsigned type and refused the probe's declaration; NUMBER(10) holds every 32-bit unsigned value.
    ("Oracle", "unsigned 32-bit"): ("NUMBER(10,0)", "NUMBER"),
    # Oracle's geometry columns are SDO_GEOMETRY: the library's write builds one with SDO_GEOMETRY(wkt, srid) (the probe's
    # ST_* object types are not what it writes).
    ("Oracle", "general geometry"): ("SDO_GEOMETRY", "Object"),
    ("Oracle", "point geometry"): ("SDO_GEOMETRY", "Object"),
    ("Oracle", "line / linestring geometry"): ("SDO_GEOMETRY", "Object"),
    ("Oracle", "polygon geometry"): ("SDO_GEOMETRY", "Object"),
    ("Oracle", "geography / geodetic"): ("SDO_GEOMETRY", "Object"),
    # DuckDB's dictionary is stored as its JSON text (DuckDB.NET does not map a dictionary to its MAP type).
    ("DuckDB", "map / dictionary"): ("VARCHAR", "VARCHAR"),
    # An enumeration's values are the names the cell uses: the probe's ENUM('a','b') cannot hold "active".
    ("MySql", "enumeration"): ("ENUM('active','inactive')", "ENUM"),
    ("MariaDb", "enumeration"): ("ENUM('active','inactive')", "ENUM"),
    ("TiDb", "enumeration"): ("ENUM('active','inactive')", "ENUM"),
    ("SingleStore", "enumeration"): ("ENUM('active','inactive')", "ENUM"),
    # DuckDB's ENUM takes its values by name and the parameter binder sends the text as UINT8; an address and a network
    # are text on DuckDB, which refuses a varchar into INET or CIDR without a cast.
    ("DuckDB", "enumeration"): ("VARCHAR(20)", "VARCHAR"),
    ("DuckDB", "IP address"): ("VARCHAR(45)", "VARCHAR"),
    ("DuckDB", "IP network / CIDR"): ("VARCHAR(50)", "VARCHAR"),
    # A second-precision datetime keeps its time of day. Where the probe took DATE (PostgreSQL family, Firebird, Spanner) the
    # column drops the time; where it took SMALLDATETIME (SQL Server, Sybase) the column rounds to the minute. Oracle's DATE
    # holds seconds, so it stays as the probe found it.
    ("PostgreSql", "second-precision datetime"): ("TIMESTAMP(0)", "timestamp(0) without time zone"),
    ("CockroachDb", "second-precision datetime"): ("TIMESTAMP(0)", "timestamp(0) without time zone"),
    ("YugabyteDb", "second-precision datetime"): ("TIMESTAMP(0)", "timestamp(0) without time zone"),
    ("Firebird", "second-precision datetime"): ("TIMESTAMP", "TIMESTAMP"),
    ("Spanner", "second-precision datetime"): ("TIMESTAMPTZ", "timestamptz"),
    ("SqlServer", "second-precision datetime"): ("DATETIME2(0)", "datetime2"),
    ("SybaseASE", "second-precision datetime"): ("DATETIME", "datetime"),
    # A national string is variable-length text. The probe's fixed CHAR(10) cannot hold its 13-character sample, and a
    # fixed-width column pads every read with spaces, so these are the variable-length Unicode text types.
    # A GUID's column is the schema its dialect writes (GuidStorageFormat): Firebird's CHAR(16) OCTETS, Sybase's BINARY(16),
    # Oracle's text. The probe's first accepted type was a different shape (CHAR(36) for Firebird and Sybase, RAW(16) for
    # Oracle), which the dialect does not write.
    ("Firebird", "UUID / GUID"): ("CHAR(16) CHARACTER SET OCTETS", "CHAR"),
    ("SybaseASE", "UUID / GUID"): ("BINARY(16)", "binary"),
    ("Oracle", "UUID / GUID"): ("VARCHAR2(36)", "VARCHAR2"),
    # The Firebird driver reads a 16-octet column as a GUID and cannot write another length to it (DecodeGuid throws on an 8-
    # or 3-byte value): the binary columns are wider than 16 octets, so the driver keeps them as bytes.
    ("Firebird", "varying binary"): ("VARBINARY(32)", "CHAR"),
    # Oracle's JSON check constraint names its own column ("c IS JSON"), which the table does not have; the document is
    # stored as the CLOB, as the cell compares it.
    ("Oracle", "JSON text / JSON value"): ("CLOB", "Clob"),
    ("PostgreSql", "national / Unicode character"): ("VARCHAR(100)", "character varying(100)"),
    ("YugabyteDb", "national / Unicode character"): ("VARCHAR(100)", "character varying(100)"),
    ("Oracle", "national / Unicode character"): ("NVARCHAR2(100)", "NVarchar2"),
    ("Firebird", "national / Unicode character"): ("VARCHAR(100)", "VARCHAR"),
}

# A (database, type) the library documents as unsupported, with the reason. Its declaration is not written, so the cell is
# UNSUPPORTED with that reason (the same as a database that refused the column): it is a decision, not a fix.
DB_REFUSED = {
    # A Snowflake FILE value names a file on a stage. The fidelity test has no stage or staged file, so the cell is refused until a
    # stage fixture exists (the server accepts the type; the refusal is the test's missing fixture).
    # SAP ASE 16 refuses the spatial types (probe, results/SybaseASE.jsonl: "Can't find type"). No ASE spatial documentation was
    # found to confirm it against; the refusal rests on the probe.
    ("SybaseASE", "point geometry"): "probe: SAP ASE 16 refuses POINT (\"Can't find type\", results/SybaseASE.jsonl); no ASE spatial documentation was found to confirm it",
    ("SybaseASE", "line / linestring geometry"): "probe: SAP ASE 16 refuses LINESTRING (\"Can't find type\", results/SybaseASE.jsonl); no ASE spatial documentation was found to confirm it",
    ("SybaseASE", "polygon geometry"): "probe: SAP ASE 16 refuses POLYGON (\"Can't find type\", results/SybaseASE.jsonl); no ASE spatial documentation was found to confirm it",
    # YugabyteDB accepts the XML column declaration (results/YugabyteDb.jsonl), but an INSERT of an XML value fails with
    # "unsupported XML feature" (YugabyteDb suite): the values are refused, so the cell is refused.
    ("YugabyteDb", "XML"): "server: YugabyteDB accepts an XML column, but an INSERT of an XML value fails with 'unsupported XML feature' (YugabyteDb suite); the values are refused",
    # REAL_VECTOR is refused by the HANA Express builds we can pull: 2.00.088 (latest) and 2.00.082, whose catalogue has no vector
    # type (results/versions/SapHana-2.00.088-latest.jsonl). A newer HANA is needed to test the type.
    ("SapHana", "vector embedding"): "server: REAL_VECTOR is refused by SAP HANA Express 2.00.088 (latest) and 2.00.082, whose catalogue has no vector type (results/versions/SapHana-2.00.088-latest.jsonl); a newer HANA release is needed",
    # pgvector is an extension: the default testbed image lacks it. The pgvector image accepts vector(3) as float[]
    # (results/versions/PostgreSql-pgvector.jsonl). Enabling it means a pgvector testbed image, which is not the default.
    ("PostgreSql", "vector embedding"): "extension gap: pgvector is not in the default testbed image (extension \"vector\" is not available); the pgvector image accepts vector(3) as float[] (results/versions/PostgreSql-pgvector.jsonl)",
    # PostGIS is an extension: the default testbed image lacks it. The PostGIS image accepts geography(POINT,4326) as the library's
    # Geography (results/versions/PostgreSql-postgis.jsonl).
    ("PostgreSql", "geography / geodetic"): "extension gap: PostGIS is not in the default testbed image (extension \"postgis\" is not available); the PostGIS image accepts geography(POINT,4326) (results/versions/PostgreSql-postgis.jsonl)",
    # InterBase accepts INTEGER[10] (results/InterBase.jsonl): the same fixed-dimension array as Firebird, so there is no
    # variable-length SQL array to map.
    ("InterBase", "SQL array"): "by design: InterBase arrays have fixed dimensions (INTEGER[10] is accepted, results/InterBase.jsonl); there is no variable-length array to map",
    # Raw bytes have no cast to JSON on Informix (No cast from byte to json, confirmed live). Its BSON is written from JSON text by
    # the native binary JSON row (?::JSON::BSON), which is declared; this generic row is the raw-bytes form.
    ("Informix", "BSON"): "by design: raw bytes have no cast to JSON on Informix (No cast from byte to json, confirmed live); Informix BSON is written from JSON text, covered by the native binary JSON row",
    # back as ReadOnlyMemory<float>, which the library does not map yet, so no cell is declared.
    # read as BLOB (byte[]), which the library does not map to a vector yet.
    # Db2 refuses an ARRAY type in a column definition (SQL20441N, a probe result): an array is a parameter or PL/SQL type here.
    ("Db2", "SQL array"): "server: Db2 refuses an ARRAY type in a column definition (SQL20441N: a ARRAY data type is not supported in the context where it is being used, confirmed live)",
    # Firebird arrays have fixed dimensions (fblangref 4.0, array_dim): INTEGER[10] reads back as ten elements, padded, so a
    # variable-length SQL array is not what the server has. The fixed-length array is the Firebird array, and it is declared.
    ("Firebird", "SQL array"): "by design: Firebird arrays have fixed dimensions (fblangref 4.0, array_dim); INTEGER[10] reads back as ten elements, so there is no variable-length array to map",
    # native point and native polygon are PostgreSQL's types (CLR PostgreSqlPoint, PostgreSqlPolygon). Another database's POINT
    # or POLYGON is a geometry, which the point geometry and polygon geometry cells cover.
    **{(db, sem): "native point is PostgreSQL's point (CLR PostgreSqlPoint); this database's POINT is a geometry, covered by point geometry"
       for db in ("MySql", "MariaDb", "Oracle", "SqlServer", "Sqlite", "Db2", "Informix", "SapHana", "SingleStore", "TiDb", "DuckDB",
                  "FlatFile", "Firebird", "CockroachDb", "SybaseASE", "Spanner", "InterBase")
       for sem in ("native point",)},
    **{(db, sem): "native polygon is PostgreSQL's polygon (CLR PostgreSqlPolygon); this database's POLYGON is a geometry, covered by polygon geometry"
       for db in ("MySql", "MariaDb", "Oracle", "SqlServer", "Sqlite", "Db2", "Informix", "SapHana", "SingleStore", "TiDb", "DuckDB",
                  "FlatFile", "Firebird", "CockroachDb", "SybaseASE", "Spanner", "InterBase")
       for sem in ("native polygon",)},
    # The probe's "accepted" for ST_GEOMETRY (provider type SQLUDTVAR) is a false positive: the test database's catalog (sysxtdtypes)
    # has no st_geometry type, and a CREATE TABLE with it fails with "Type (st_geometry) not found". The cell is refused until a
    # spatial DataBlade is installed in that database.
    ("Informix", "geography / geodetic"): "no st_geometry type in the test database (sysxtdtypes; CREATE TABLE: Type (st_geometry) not found); the probe's acceptance was a false positive",
    # pengdows.flatfile stores an INTERVAL as a TimeSpan (the day-time interval is bound as one). Months have no fixed length,
    # so no TimeSpan holds a year-month or a generic interval (which carries months): they are refused on FlatFile.
    ("FlatFile", "generic interval"): "pengdows.flatfile stores an INTERVAL as a TimeSpan: a generic interval has months, which have no fixed length",
    # The probe accepts TIMESTAMP as a rowversion candidate. On InterBase it is a date-time, which cannot hold the eight token
    # bytes; InterBase has no rowversion type (every other rowversion candidate is refused by the server).
    ("InterBase", "rowversion / timestamp token"): "InterBase has no rowversion type: its TIMESTAMP is a date-time, which cannot hold the eight token bytes",
    ("PostgreSql", "point geometry"): "by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row)",
    ("PostgreSql", "polygon geometry"): "by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPolygon (the native polygon row)",
    ("YugabyteDb", "point geometry"): "by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPoint (the native point row)",
    ("YugabyteDb", "polygon geometry"): "by design: a PostGIS Geometry carries an SRID that a native point cannot hold; the native type is PostgreSqlPolygon (the native polygon row)",
    ("SqlServer", "bit / bitstring"): "by design: a BIT column holds one bit; a bit string of any length is not representable without a length-bearing column",
    ("Access", "bit / bitstring"): "by design: a BIT (YESNO) column holds one bit; a bit string of any length is not representable without a length-bearing column",
    ("Access", "arbitrary-size integer"): "by design: Access has no integer beyond DECIMAL(28), and NUMBER is a double that loses digits; a BigInteger is not a type the OLE DB provider can marshal",
    ("Oracle", "variant / dynamic value"): "by design: ANYDATA is built by its type's constructor functions (ANYDATA.ConvertVarchar2), not bound as a value",
    # Firebird's driver converts a numeric parameter to decimal, which holds 28 digits: a 30-digit value cannot be bound to a
    # NUMERIC (the 128-bit INT128 type is bound typed, and carries 39 digits).
    # FlatFile parses a numeric column as decimal (28 digits), so a 30-digit value cannot be stored in one.
    # Live: a decimal bound to a DECFLOAT parameter fails (InvalidCastException, decimal to FbDecFloat). The library can't tell a
    # DECFLOAT column from a NUMERIC one by CLR type, so the fix needs a declared column type (see the open decision).
    ("YugabyteDb", "transaction ID"): "upstream: YugabyteDB's xid8 adds high bits on write: 4294967296 (0x100000000) reads back as 18700287606784 (0x110200000000), so no raw integer round-trips",
    ("CockroachDb", "XML"): "server: this database has no working xml column type. Map the property as string and store it in a text column; the library writes the text as is, and de/serialization stays in the application",
    # FlatFile's only date type is DATE, which holds no time of day (its zoned time is a DateTimeOffset, keeping the offset).
    # A ROWID is a pseudo-column that identifies a row: a table column of that type keeps no value the library writes (ODP.NET
    # reads the written text back as NULL), so the cell is refused rather than passed on a NULL.
    ("Oracle", "row identifier / ROWID"): "by design: a ROWID is read as its text (ROWIDTOCHAR; live-checked on Oracle 23ai) and is NonInsertable/NonUpdateable, because the database assigns it; the fidelity cell writes a value, so it is refused for that reason only",
    # Informix's BIGINT and INT8 exclude -9223372036854775808, their NULL sentinel: the server refuses the value (confirmed live).
    ("Informix", "signed 64-bit"): "server: Informix's BIGINT and INT8 exclude -9223372036854775808, their NULL sentinel; the server refuses the value (Value will not fit in a BIGINT or an INT8, confirmed live 2026-11-06)",
    # Informix writes a BSON document from its JSON text (the JSON-to-BSON cast); the server refuses raw BSON bytes (No cast from byte to json).
    # A DateTimeOffset declared DbType.Time is a time with its offset (the PostgreSQL timetz and Firebird rule). DuckDB and
    # FlatFile have no time-with-zone type to hold the offset, so the parameter is refused at the validator.
}

# A (database, type) whose column the server generates (a rowversion token, a ROWID): the library never writes it, so the
# cell inserts the row without the column and reads the generated value back.
DB_GENERATED = {
    ("SqlServer", "rowversion / timestamp token"),
}

DB_ENUM = [
    "PostgreSql", "SqlServer", "Oracle", "Firebird", "CockroachDb", "MariaDb", "MySql", "Sqlite", "DuckDB",
    "YugabyteDb", "TiDb", "SingleStore", "SybaseASE", "Spanner", "FlatFile", "Access", "Db2", "Informix",
    "InterBase", "SapHana", "AuroraMySql", "AuroraPostgreSql", "Snowflake",
]


def probe_results():
    accepted = {}   # (db, type) -> [(declaration, provider type)] in candidate order
    refused = {}    # (db, type) -> the first refusal the server gave
    for path in sorted(glob.glob(os.path.join(S, "results", "*.jsonl"))):
        for line in open(path, encoding="utf-8"):
            r = json.loads(line)
            if "containerError" in r or r.get("notProbed"):
                continue
            key = (r["db"], r["type"])
            if r.get("accepted"):
                accepted.setdefault(key, []).append((r["decl"], r.get("providerType", "")))
            elif r.get("error") and key not in refused:
                refused[key] = r["error"]
    return accepted, refused


def pick(candidates, preferred):
    if not candidates:
        return None
    if preferred:
        for decl, ptype in candidates:
            if decl.upper().startswith(preferred):
                return (decl, ptype)
    return candidates[0]


def cs_string(s):
    return '"' + s.replace("\\", "\\\\").replace("\"", "\\\"") + '"'


def pascal(col):
    return "".join(p.capitalize() for p in col.split("_"))


def class_name(db):
    return "TypeFidelityRow_" + db


# A rowversion token is eight bytes the database keeps as they are. Where the first accepted declaration is a timestamp
# (PostgreSQL, MySQL and others, which store the 8 bytes as a date and refuse them) the column is the database's own
# variable-length binary type, the one the "varying binary" cell uses: a fixed-width BINARY(16) would pad the token.
ROWVERSION_AS_FIXED_BINARY = {
    "PostgreSql", "YugabyteDb", "CockroachDb", "DuckDB", "MariaDb", "MySql", "SingleStore", "TiDb", "Oracle",
    "Firebird", "FlatFile", "Access",
}


# A column whose declared native type a CLR type cannot say (a decimal is NUMERIC or DECFLOAT, a string is xml or a
# PostgreSQL vector): the NativeType its entity carries, per database. Each one gets its own row class, so a database's
# cast never sees another database's type. Only a dialect that casts a native type reads it.
PG_FAMILY = ("PostgreSql", "YugabyteDb", "CockroachDb")
NATIVE_TYPE = {
    # The default images have VECTOR (MySQL 9.7, MariaDB 11.8); the library writes and reads the native VECTOR(3) through its
    # conversion functions (STRING_TO_VECTOR / VECTOR_TO_STRING; VEC_FromText / VEC_ToText).
    ("MySql", "vector embedding"): "VECTOR(3)",
    ("MariaDb", "vector embedding"): "VECTOR(3)",
    # Snowflake's ARRAY column: written as PARSE_JSON of the array's JSON, read as TO_JSON (SnowflakeArrayTests).
    ("Snowflake", "SQL array"): "ARRAY",
    ("Snowflake", "list / multiset / set collection"): "ARRAY",
    # Snowflake's OBJECT and VARIANT columns: the value is written as its JSON through PARSE_JSON (SnowflakeSemiStructuredTests).
    ("Snowflake", "map / dictionary"): "OBJECT",
    # Db2's SECLABEL / SECLABEL_TO_CHAR take the policy name; the fixture creates the policy POL (TypeFidelityIntegrationTests).
    ("Db2", "specialized security label"): "DB2SECURITYLABEL(POL)",
    # A FILE column holds a staged file; the fidelity test creates the stage and the file (TypeFidelityIntegrationTests).
    ("Snowflake", "external-file reference"): "FILE",
    ("Snowflake", "variant / dynamic value"): "VARIANT",
    # The VARRAY's native type: the library builds it from the elements' JSON (OracleDialect.CollectionArgument).
    ("Oracle", "SQL array"): "probe_varray",
    ("Firebird", "decimal floating point"): "DECFLOAT",
    ("Oracle", "external-file reference"): "BFILE",
    **{(db, "bit / bitstring"): "BIT(3)" for db in ("MySql", "MariaDb", "TiDb", "SingleStore")},
    ("PostgreSql", "map / dictionary"): "hstore",
    ("PostgreSql", "hierarchical path"): "ltree",
    **{(db, "XML"): "xml" for db in PG_FAMILY},
    **{(db, "WAL / log position"): "pg_lsn" for db in PG_FAMILY},
    **{(db, "full-text search vector / query"): "tsquery" for db in PG_FAMILY},
    **{(db, "transaction ID"): "xid8" for db in ("PostgreSql", "YugabyteDb")},
    **{(db, "32-bit transaction ID"): "xid" for db in ("PostgreSql", "YugabyteDb")},
    **{(db, "native point"): "point" for db in ("PostgreSql", "YugabyteDb")},
    **{(db, "native polygon"): "polygon" for db in ("PostgreSql", "YugabyteDb")},
}


# Aurora runs its engine's server (MySQL, PostgreSQL), so its rules are the engine's; its declarations are still its own probe's.
ENGINE = {"AuroraMySql": "MySql", "AuroraPostgreSql": "PostgreSql"}

# The engine's entries apply to its Aurora alias too.
for _alias, _engine in ENGINE.items():
    NATIVE_TYPE.update({(_alias, _sem): _native for (_d, _sem), _native in list(NATIVE_TYPE.items()) if _d == _engine})
    if _engine in ROWVERSION_AS_FIXED_BINARY:
        ROWVERSION_AS_FIXED_BINARY.add(_alias)


def rules_db(db):
    return ENGINE.get(db, db)


# Extensions that Aurora PostgreSQL installs (AWS extension list) and whose cells come from Aurora's own probe, not the engine's
# (the default PostgreSQL image lacks them: its refusal stands).
OWN_EXTENSIONS = {("AuroraPostgreSql", "vector embedding"), ("AuroraPostgreSql", "geography / geodetic")}


def resolve(accepted, db, sem):
    """The (declaration, provider type) for a semantic type on a database, or None when it has no column there."""
    rdb = rules_db(db)
    if (rdb, sem) in DB_REFUSED and (db, sem) not in OWN_EXTENSIONS:
        return None
    if (rdb, sem) in DB_PREFER:
        return DB_PREFER[(rdb, sem)]
    if (rdb, sem) in DB_REQUIRED:
        required = DB_REQUIRED[(rdb, sem)].upper()
        return next((c for c in accepted.get((db, sem), []) if c[0].upper().startswith(required)), None)
    if sem == "rowversion / timestamp token" and rdb in ROWVERSION_AS_FIXED_BINARY:
        binary = resolve(accepted, db, "varying binary")
        if binary is not None:
            return binary
    candidates = accepted.get((db, sem), [])
    if sem == "unsigned 32-bit" and probed(accepted, db, "BIGINT"):
        # Where the library's bound type holds the value, that is the column (NUMERIC(18,0) only where BIGINT is refused).
        candidates = [c for c in candidates if c[0].upper() != "NUMERIC(18,0)"]
    hit = pick(candidates, PREFER.get(sem))
    if hit is not None and "." in hit[1]:
        # A type in a schema is named by its own name: the schema is the testbed's (aurora_pg_test_<ms>), which differs per run.
        hit = (hit[0], hit[1].rsplit(".", 1)[1])
    # A widened declaration is written only when the probe accepted it on this database (under some type): an unprobed
    # BIGINT or NUMERIC(20,0) is an assumption, and the fidelity test then fails on a type the server does not have.
    if hit is None and sem in WIDENED and probed(accepted, db, WIDENED[sem]):
        hit = (WIDENED[sem], "(widened)")
    return hit


# A native row whose CLR type is the library's value type for the database's native type, where the spec's shared CLR
# type is not one the database's native type takes (PostgreSQL hstore is HStore, docs/advanced-types.md).
NATIVE_CLR = {
    ("PostgreSql", "map / dictionary"): ("HStore", "HStore.Parse(\"a=>1\")"),
    # ltree labels are dot-separated, so its value is text in that form (HierarchyId's /1/2/ is not an ltree).
    ("PostgreSql", "hierarchical path"): ("string", "\"1.2\""),
}


# A Snowflake FILE holds a staged file, so its sample is a stage path: the fidelity test creates FIDELITY_STAGE and the file in it.
NATIVE_SAMPLE = {
    # Db2 LBAC: the fixture grants db2inst1 the LOW label; SECLABEL('POL', 'LOW') writes it and SECLABEL_TO_CHAR reads back LOW.
    ("Db2", "specialized security label"): "\"LOW\"",
    ("Snowflake", "external-file reference"): "\"@FIDELITY_STAGE/file.bin\"",
}


def native_clr(db, sem, clr, val):
    """The CLR type and sample of a native row: the library's value type where NATIVE_CLR names one, else the spec's."""
    clr, val = NATIVE_CLR.get((rules_db(db), sem), (clr, val))
    return clr, NATIVE_SAMPLE.get((rules_db(db), sem), val)


def prerequisites(prereqs, resolved):
    """The DDL to run before a cell: only where the cell declares the named type or domain that DDL creates."""
    out = {}
    for db, by_sem in prereqs.items():
        for sem, entry in by_sem.items():
            hit = resolved.get(db, {}).get(sem)
            if hit is not None and hit[0].upper() == entry["decl"].upper():
                out.setdefault(db, {})[sem] = list(entry["ddl"])
    return out


def probed(accepted, db, decl):
    """True when the probe accepted this exact declaration on the database, for any semantic type."""
    return any(d.upper() == decl.upper()
               for (kdb, _), rows in accepted.items() if kdb == db
               for d, _ in rows)


def refused_reason(refused, db, sem):
    text = refused.get((db, sem), "no candidate was tried")
    return " ".join(text.replace("Unknown failed on " + db + ": ", "").split())[:200]


def main():
    accepted, refused = probe_results()
    resolved = {db: {sem: resolve(accepted, db, sem) for _, sem, *_ in SPEC} for db in DB_ENUM}

    # --- TypeFidelityDeclarations.cs -------------------------------------------------------------
    d = [
        "// <auto-generated>",
        "// Generated by tools/datatype-probe/gen_fidelity.py from the live probe results. Do not edit by hand.",
        "// </auto-generated>",
        "using pengdows.crud.enums;",
        "",
        "namespace pengdows.crud.IntegrationTests.Core;",
        "",
        "internal static class TypeFidelityDeclarations",
        "{",
        "    // database -> semantic type -> (declaration, provider type reported by the server, or \"(widened)\")",
        "    public static readonly IReadOnlyDictionary<SupportedDatabase, IReadOnlyDictionary<string, (string Declaration, string ProviderType)>> ByDatabase =",
        "        new Dictionary<SupportedDatabase, IReadOnlyDictionary<string, (string Declaration, string ProviderType)>>",
        "        {",
    ]
    for db in DB_ENUM:
        d.append(f"            [SupportedDatabase.{db}] = new Dictionary<string, (string Declaration, string ProviderType)>")
        d.append("            {")
        for _, sem, *_ in SPEC:
            hit = resolved[db][sem]
            if hit:
                d.append(f"                [{cs_string(sem)}] = ({cs_string(hit[0])}, {cs_string(hit[1])}),")
        d.append("            },")
    d += [
        "        };",
        "",
        "    // database -> semantic type -> why the database has no column for it (the server's first refusal).",
        "    public static readonly IReadOnlyDictionary<SupportedDatabase, IReadOnlyDictionary<string, string>> UnsupportedByDatabase =",
        "        new Dictionary<SupportedDatabase, IReadOnlyDictionary<string, string>>",
        "        {",
    ]
    for db in DB_ENUM:
        gaps = [(sem, refused.get((db, sem), "no candidate was tried")) for _, sem, *_ in SPEC
                if resolved[db][sem] is None]
        if not gaps:
            continue
        d.append(f"            [SupportedDatabase.{db}] = new Dictionary<string, string>")
        d.append("            {")
        for sem, refusal in gaps:
            reason = DB_REFUSED.get((rules_db(db), sem)) or " ".join(refusal.replace("Unknown failed on " + db + ": ", "").split())[:200]
            d.append(f"                [{cs_string(sem)}] = {cs_string('refused: ' + reason)},")
        d.append("            },")
    d += ["        };", "",
          "    // (database, semantic type) whose column the server generates: the cell inserts the row without it and reads the value back.",
          "    public static readonly IReadOnlySet<(SupportedDatabase Database, string Semantic)> ServerGenerated =",
          "        new HashSet<(SupportedDatabase Database, string Semantic)>",
          "        {"]
    for db, sem in sorted(DB_GENERATED):
        d.append(f"            (SupportedDatabase.{db}, {cs_string(sem)}),")
    d += ["        };", ""]
    # DDL that must run before a cell's table: a named type or domain the cell's declaration uses (prerequisites.json).
    prereq_file = os.path.join(S, "prerequisites.json")
    prereqs = json.load(open(prereq_file, encoding="utf-8")) if os.path.exists(prereq_file) else {}
    needed = prerequisites(prereqs, resolved)
    d += ["    // database -> semantic type -> DDL that must run before the cell's table is created (a named type or domain).",
          "    public static readonly IReadOnlyDictionary<SupportedDatabase, IReadOnlyDictionary<string, string[]>> Prerequisites =",
          "        new Dictionary<SupportedDatabase, IReadOnlyDictionary<string, string[]>>",
          "        {"]
    for db in sorted(needed):
        d.append(f"            [SupportedDatabase.{db}] = new Dictionary<string, string[]>")
        d.append("            {")
        for sem, ddl in sorted(needed[db].items()):
            statements = ", ".join(cs_string(s) for s in ddl)
            d.append(f"                [{cs_string(sem)}] = new[] {{ {statements} }},")
        d.append("            },")
    d += ["        };", ""]
    # The CREATE TABLE the cells use on a database that needs a table option (Oracle's tablespace, SingleStore's rowstore).
    templates = {db: cfg["table_template"] for db, cfg in prereqs.items() if "table_template" in cfg}
    d += ["    // database -> CREATE TABLE template: {table} and {columns} are filled in; a database needs no entry for plain CREATE TABLE.",
          "    public static readonly IReadOnlyDictionary<SupportedDatabase, string> TableTemplates =",
          "        new Dictionary<SupportedDatabase, string>",
          "        {"]
    for db in sorted(templates):
        d.append(f"            [SupportedDatabase.{db}] = {cs_string(templates[db])},")
    d += ["        };", "}", ""]
    with open(os.path.join(OUT, "TypeFidelityDeclarations.cs"), "w", encoding="utf-8") as f:
        f.write("\n".join(d))

    # One entity per fidelity type: the test round-trips each type on each database in its own table, so one
    # type's failure can't hide another's.
    c = [
        "// <auto-generated>",
        "// Generated by tools/datatype-probe/gen_fidelity.py. Do not edit by hand.",
        "// </auto-generated>",
        "using System.Collections;",
        "using System.Collections.Generic;",
        "using System.Data;",
        "using System.Net;",
        "using System.Numerics;",
        "using System.Text.Json;",
        "using pengdows.crud.attributes;",
        "using pengdows.crud.enums;",
        "using pengdows.crud.types.valueobjects;",
        "",
        "namespace pengdows.crud.IntegrationTests.Core;",
        "",
    ]
    for i, (col, sem, clr, dbt, val, _cmp) in enumerate(SPEC):
        name = f"Cell_{pascal(col)}"
        c += [
            "[Table(\"type_fidelity\")]",
            f"public sealed class {name} : IFidelityRow<{name}>",
            "{",
            "    [Id(true)]",
            "    [Column(\"id\", DbType.Int32)]",
            "    public int Id { get; set; }",
            "",
            f"    [Column({cs_string(col)}, DbType.{dbt})]",
            f"    public {clr} Value {{ get; set; }}",
            "",
            "    public static IReadOnlyList<(string Column, string Property, string Semantic)> Columns { get; } =",
            f"        new[] {{ ({cs_string(col)}, \"Value\", {cs_string(sem)}) }};",
            "",
            f"    public static {name} Expected() => new() {{ Id = 1, Value = {val} }};",
            "}",
            "",
        ]
    # One row class per database that declares a native type for a semantic type, with that NativeType.
    spec_by_sem = {sem: (col, clr, dbt, val) for col, sem, clr, dbt, val, _cmp in SPEC}
    native_rows = []
    for (db, sem), native in NATIVE_TYPE.items():
        if sem not in spec_by_sem:
            continue
        col, clr, dbt, val = spec_by_sem[sem]
        clr, val = native_clr(db, sem, clr, val)
        name = f"Cell_{pascal(col)}_{db}"
        native_rows.append((db, sem, name))
        c += [
            "[Table(\"type_fidelity\")]",
            f"public sealed class {name} : IFidelityRow<{name}>",
            "{",
            "    [Id(true)]",
            "    [Column(\"id\", DbType.Int32)]",
            "    public int Id { get; set; }",
            "",
            f"    [Column({cs_string(col)}, DbType.{dbt}, NativeType = {cs_string(native)})]",
            f"    public {clr} Value {{ get; set; }}",
            "",
            "    public static IReadOnlyList<(string Column, string Property, string Semantic)> Columns { get; } =",
            f"        new[] {{ ({cs_string(col)}, \"Value\", {cs_string(sem)}) }};",
            "",
            f"    public static {name} Expected() => new() {{ Id = 1, Value = {val} }};",
            "}",
            "",
        ]
    c += [
        "// Every fidelity type, as the test runs it: its semantic name, its column, and its entity.",
        "internal static class TypeFidelityCells",
        "{",
        "    public static readonly IReadOnlyList<(string Semantic, string Column, Type Row)> All = new (string, string, Type)[]",
        "    {",
    ]
    for col, sem, *_ in SPEC:
        c.append(f"        ({cs_string(sem)}, {cs_string(col)}, typeof(Cell_{pascal(col)})),")
    c += [
        "    };",
        "",
        "    // The row class of a type on a database whose column declares a native type; null when the shared row applies.",
        "    public static Type NativeRowFor(SupportedDatabase database, string semantic) => (database, semantic) switch",
        "    {",
    ]
    for db, sem, name in native_rows:
        c.append(f"        (SupportedDatabase.{db}, {cs_string(sem)}) => typeof({name}),")
    c += ["        _ => null,", "    };", "}", ""]
    with open(os.path.join(OUT, "TypeFidelityCells.cs"), "w", encoding="utf-8") as f:
        f.write("\n".join(c))

    # The single shared entity of the previous generation is replaced by the per-database entities.
    stale = os.path.join(OUT, "TypeFidelityRow.cs")
    if os.path.exists(stale):
        os.remove(stale)

    # The full list, one row per fidelity type and database: for the doc and the SQLite store.
    import csv
    with open(os.path.join(S, "fidelity_types.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["semantic_type", "column", "clr_type", "db_type", "database", "status", "declaration",
                    "provider_type", "reason"])
        for col, sem, clr, dbt, _v, _c in SPEC:
            for db in DB_ENUM:
                hit = resolved[db][sem]
                if hit is None:
                    status, decl, ptype = "unsupported", "", ""
                    reason = refused_reason(refused, db, sem)
                else:
                    status = "widened" if hit[1] == "(widened)" else "declared"
                    decl, ptype, reason = hit[0], hit[1], ""
                w.writerow([sem, col, clr, dbt, db, status, decl, ptype, reason])

    declared = sum(1 for db in DB_ENUM for _, sem, *_ in SPEC if resolved[db][sem] is not None)
    print("databases:", len(DB_ENUM), "types:", len(SPEC), "declared cells:", declared)


if __name__ == "__main__":
    main()
