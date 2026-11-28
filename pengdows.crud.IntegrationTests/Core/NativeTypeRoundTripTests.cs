using System.Collections;
using System.Data;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using NpgsqlTypes;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.exceptions;
using pengdows.crud.types.valueobjects;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Native type round trip: for every type a database supports, one table column of that type is created, the
/// minimum, maximum and awkward values are written as text literals cast to the type, and read back through the
/// governed reader. Each value must come back equal to the expected .NET value. A case that fails is reported with
/// the values it got; nothing is skipped.
///
/// The cases are data, one list per database. PostgreSQL comes first; the other databases follow the same shape.
/// </summary>
[Collection("IntegrationTests")]
public class NativeTypeRoundTripTests : DatabaseTestBase
{
    public NativeTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    /// <summary>One column of one native type: the type's declaration, the text literal written, and the expected value.</summary>
    /// <summary>InsertSql, when set, is the value expression written in place of the default (a literal cast or quoted literal).</summary>
    public sealed record NativeCase(string NativeType, string Literal, object Expected, string Label, string? InsertSql = null, string? ColumnDefinition = null);

    public static IReadOnlyList<NativeCase> PostgreSqlCases { get; } = new List<NativeCase>
    {
        // Integers
        new("smallint", "-32768", (short)-32768, "smallint min"),
        new("smallint", "32767", (short)32767, "smallint max"),
        new("integer", "-2147483648", -2147483648, "integer min"),
        new("integer", "2147483647", 2147483647, "integer max"),
        new("bigint", "-9223372036854775808", long.MinValue, "bigint min"),
        new("bigint", "9223372036854775807", long.MaxValue, "bigint max"),

        // Exact and binary floating point
        new("numeric", "-79228162514264337593543950335", decimal.MinValue, "numeric decimal min"),
        new("numeric", "79228162514264337593543950335", decimal.MaxValue, "numeric decimal max"),
        new("numeric", "0.0000000000000000000000000001", 0.0000000000000000000000000001m, "numeric 1e-28"),
        new("real", "3.4028235e38", float.MaxValue, "real max"),
        new("real", "-3.4028235e38", float.MinValue, "real min"),
        new("double precision", "1.7976931348623157e308", double.MaxValue, "double max"),
        new("double precision", "-1.7976931348623157e308", double.MinValue, "double min"),
        new("double precision", "4.9406564584124654e-324", double.Epsilon, "double denormal"),
        new("money", "92233720368547758.07", 92233720368547758.07m, "money max"),

        // Boolean and character
        new("boolean", "true", true, "boolean true"),
        new("boolean", "false", false, "boolean false"),
        new("char(10)", "abc", "abc       ", "char pads to length"),
        new("varchar(100)", "hello", "hello", "varchar"),
        new("text", "héllo 漢字 'quoted'", "héllo 漢字 'quoted'", "text unicode and quote"),
        new("text", "", "", "text empty string"),

        // Binary and bits
        new("bytea", "\\x00ff", new byte[] { 0x00, 0xff }, "bytea"),
        new("bit(3)", "101", new BitArray(new[] { true, false, true }), "bit(3)"),
        new("varbit", "1010", new BitArray(new[] { true, false, true, false }), "varbit"),

        // Date and time
        new("date", "0001-01-01", new DateTime(1, 1, 1), "date min (.NET)"),
        new("date", "9999-12-31", new DateTime(9999, 12, 31), "date max (.NET)"),
        new("time", "00:00:00", TimeSpan.Zero, "time min"),
        new("time", "23:59:59.999999", new TimeSpan(0, 23, 59, 59, 999) + TimeSpan.FromTicks(9990), "time microseconds"),
        new("timestamp", "0001-01-01 00:00:00", DateTime.MinValue, "timestamp min"),
        new("timestamp", "9999-12-31 23:59:59.999999",
            new DateTime(9999, 12, 31, 23, 59, 59, 999).AddTicks(9990), "timestamp max microseconds"),
        new("timestamptz", "2024-06-01 12:00:00+00", new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            "timestamptz utc"),

        // Identifier, document and markup
        new("uuid", "ffffffff-ffff-ffff-ffff-ffffffffffff", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "uuid max"),
        new("json", "{\"a\":1}", "{\"a\":1}", "json text"),
        new("jsonb", "{\"a\": 1}", "{\"a\": 1}", "jsonb"),
        new("xml", "<a>1</a>", "<a>1</a>", "xml"),

        // Geometric
        new("point", "(1,2)", "(1,2)", "point"),
        new("lseg", "[(0,0),(1,1)]", "[(0,0),(1,1)]", "lseg"),
        new("box", "(1,1),(0,0)", "(1,1),(0,0)", "box"),
        new("path", "((0,0),(1,1))", "((0,0),(1,1))", "path"),
        new("polygon", "((0,0),(1,0),(1,1))", "((0,0),(1,0),(1,1))", "polygon"),
        new("circle", "<(0,0),1>", "<(0,0),1>", "circle"),

        // Network
        new("inet", "10.0.0.1/32", IPAddress.Parse("10.0.0.1"), "inet host (driver returns the address)"),
        new("cidr", "10.0.0.0/8", "10.0.0.0/8", "cidr"),
        new("macaddr", "08:00:2b:01:02:03", PhysicalAddress.Parse("08002B010203"), "macaddr"),
        new("macaddr8", "08:00:2b:01:02:03:04:05", PhysicalAddress.Parse("08002B0102030405"), "macaddr8"),

        // Ranges
        new("int4range", "[1,10)", "[1,10)", "int4range"),
        new("int8range", "[1,10)", "[1,10)", "int8range"),
        new("numrange", "[1.5,2.5]", "[1.5,2.5]", "numrange"),
        new("daterange", "[2024-01-01,2024-02-01)", new NpgsqlRange<DateTime>(new DateTime(2024, 1, 1), true, new DateTime(2024, 2, 1), false), "daterange"),
        new("tsrange", "[2024-01-01 00:00,2024-01-02 00:00)", new NpgsqlRange<DateTime>(new DateTime(2024, 1, 1), true, new DateTime(2024, 1, 2), false),
            "tsrange"),
        new("tstzrange", "[2024-01-01 00:00+00,2024-01-02 00:00+00)", new NpgsqlRange<DateTime>(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), true, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), false), "tstzrange"),
        new("int4multirange", "{[1,2),[3,4)}", new[] { new NpgsqlRange<int>(1, true, 2, false), new NpgsqlRange<int>(3, true, 4, false) }, "int4multirange"),

        // Identifiers and system types
        new("oid", "4294967295", uint.MaxValue, "oid max"),
        new("xid", "4294967295", uint.MaxValue, "xid max"),
        new("xid8", "18446744073709551615", ulong.MaxValue, "xid8 max"),
        new("pg_lsn", "FFFFFFFF/FFFFFFFF", "FFFFFFFF/FFFFFFFF", "pg_lsn max"),
        new("tid", "(4294967295,65535)", "(4294967295,65535)", "tid"),
                new("name", "abc", "abc", "name"),
        new("\"char\"", "a", "a", "internal char"),
        new("int2vector", "1 2 3", new short[] { 1, 2, 3 }, "int2vector"),
        new("oidvector", "1 2 3", new uint[] { 1, 2, 3 }, "oidvector"),
        new("refcursor", "c1", "c1", "refcursor"),
        new("jsonpath", "$.a", "$.\"a\"", "jsonpath (server normalises the key quoting)"),
        new("tsvector", "'fat':1 'cat':2", "'cat':2 'fat':1", "tsvector"),
        new("tsquery", "fat & cat", "'fat' & 'cat'", "tsquery"),
    };

    public static IReadOnlyList<NativeCase> MySqlCases { get; } = new List<NativeCase>
    {
        // Integers (signed and UNSIGNED)
        new("tinyint", "-128", (sbyte)-128, "tinyint min"),
        new("tinyint", "127", (sbyte)127, "tinyint max"),
        new("tinyint unsigned", "255", (byte)255, "tinyint unsigned max"),
        new("smallint", "-32768", (short)-32768, "smallint min"),
        new("smallint", "32767", (short)32767, "smallint max"),
        new("smallint unsigned", "65535", (ushort)65535, "smallint unsigned max"),
        new("mediumint", "-8388608", -8388608, "mediumint min"),
        new("mediumint", "8388607", 8388607, "mediumint max"),
        new("mediumint unsigned", "16777215", 16777215u, "mediumint unsigned max"),
        new("int", "-2147483648", -2147483648, "int min"),
        new("int", "2147483647", 2147483647, "int max"),
        new("int unsigned", "4294967295", 4294967295u, "int unsigned max"),
        new("bigint", "-9223372036854775808", long.MinValue, "bigint min"),
        new("bigint", "9223372036854775807", long.MaxValue, "bigint max"),
        new("bigint unsigned", "18446744073709551615", 18446744073709551615ul, "bigint unsigned max"),

        // Exact and binary floating point
        new("decimal(65,30)", "-79228162514264337593543950335", decimal.MinValue, "decimal(65,30) decimal min"),
        new("decimal(65,30)", "0.0000000000000000000000000001", 0.0000000000000000000000000001m,
            "decimal(65,30) 1e-28 (CLR decimal holds 28 places)"),
        new("float", "3.4028234663852886e38", 3.4028234663852886e38f, "float max"),
        new("double", "1.7976931348623157e308", double.MaxValue, "double max"),
        new("double", "-1.7976931348623157e308", double.MinValue, "double min"),

        // Bits and booleans
        new("bit(64)", "b'1111111111111111111111111111111111111111111111111111111111111111'",
            ulong.MaxValue, "bit(64) max", "b'1111111111111111111111111111111111111111111111111111111111111111'"),
        new("bit(3)", "b'101'", (ulong)5, "bit(3)", "b'101'"),
        new("bool", "1", true, "boolean (TINYINT(1), read as Boolean by the driver)"),

        // Character and binary
        new("char(10)", "abc", "abc", "char(10) (MySQL strips trailing spaces on read)"),
        new("varchar(100)", "héllo 漢字 'quoted'", "héllo 漢字 'quoted'", "varchar unicode and quote"),
        new("varchar(100)", "", "", "varchar empty string"),
        new("tinytext", "t", "t", "tinytext"),
        new("text", "x", "x", "text"),
        new("mediumtext", "m", "m", "mediumtext"),
        new("longtext", "l", "l", "longtext"),
        new("binary(4)", "0x00FF0102", new byte[] { 0x00, 0xFF, 0x01, 0x02 }, "binary(4)", "x'00FF0102'"),
        new("varbinary(100)", "0x00FF", new byte[] { 0x00, 0xFF }, "varbinary", "x'00FF'"),
        new("tinyblob", "0x01", new byte[] { 0x01 }, "tinyblob", "x'01'"),
        new("blob", "0x02", new byte[] { 0x02 }, "blob", "x'02'"),
        new("mediumblob", "0x03", new byte[] { 0x03 }, "mediumblob", "x'03'"),
        new("longblob", "0x04", new byte[] { 0x04 }, "longblob", "x'04'"),

        // Date and time
        new("date", "1000-01-01", new DateTime(1000, 1, 1), "date min"),
        new("date", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("time(6)", "-838:59:59.000000", -(TimeSpan.FromHours(838) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59)), "time min"),
        new("time(6)", "838:59:59.000000", TimeSpan.FromHours(838) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59), "time max"),
        new("datetime(6)", "1000-01-01 00:00:00.000000", new DateTime(1000, 1, 1), "datetime min"),
        new("datetime(6)", "9999-12-31 23:59:59.999999", new DateTime(9999, 12, 31, 23, 59, 59, 999).AddTicks(9990),
            "datetime(6) max microseconds"),
        new("timestamp(6)", "2024-06-01 12:00:00.000000", new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            "timestamp (UTC session)"),
        new("year", "2024", 2024, "year"),

        // Enumerated and document
        new("enum('a','b')", "b", "b", "enum"),
        new("set('a','b','c')", "a,c", "a,c", "set"),
        new("json", "{\"a\": 1}", "{\"a\": 1}", "json"),

        // Spatial
    };

    public static IReadOnlyList<KnownLimit> MySqlSpatialKnownLimits { get; } = new List<KnownLimit>
    {
        new("point", "POINT(1 2)", "ST_AsText(v)", "POINT(1 2)",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('POINT(1 2)')"),
        new("linestring", "LINESTRING(0 0,1 1)", "ST_AsText(v)", "LINESTRING(0 0,1 1)",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('LINESTRING(0 0,1 1)')"),
        new("polygon", "POLYGON((0 0,1 0,1 1,0 0))", "ST_AsText(v)", "POLYGON((0 0,1 0,1 1,0 0))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('POLYGON((0 0,1 0,1 1,0 0))')"),
        new("multipoint", "MULTIPOINT(1 2,3 4)", "ST_AsText(v)", "MULTIPOINT(1 2,3 4)",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTIPOINT(1 2,3 4)')"),
        new("multilinestring", "MULTILINESTRING((0 0,1 1))", "ST_AsText(v)", "MULTILINESTRING((0 0,1 1))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTILINESTRING((0 0,1 1))')"),
        new("multipolygon", "MULTIPOLYGON(((0 0,1 0,1 1,0 0)))", "ST_AsText(v)", "MULTIPOLYGON(((0 0,1 0,1 1,0 0)))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTIPOLYGON(((0 0,1 0,1 1,0 0)))')"),
        new("geometrycollection", "GEOMETRYCOLLECTION(POINT(1 2))", "ST_AsText(v)", "GEOMETRYCOLLECTION(POINT(1 2))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('GEOMETRYCOLLECTION(POINT(1 2))')"),
    };

    public static IReadOnlyList<KnownLimit> MySql8SpatialKnownLimits { get; } = new List<KnownLimit>
    {
        new("point", "POINT(1 2)", "ST_AsText(v)", "POINT(1 2)",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('POINT(1 2)')"),
        new("linestring", "LINESTRING(0 0,1 1)", "ST_AsText(v)", "LINESTRING(0 0,1 1)",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('LINESTRING(0 0,1 1)')"),
        new("polygon", "POLYGON((0 0,1 0,1 1,0 0))", "ST_AsText(v)", "POLYGON((0 0,1 0,1 1,0 0))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('POLYGON((0 0,1 0,1 1,0 0))')"),
        new("multipoint", "MULTIPOINT(1 2,3 4)", "ST_AsText(v)", "MULTIPOINT((1 2),(3 4))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTIPOINT(1 2,3 4)')"),
        new("multilinestring", "MULTILINESTRING((0 0,1 1))", "ST_AsText(v)", "MULTILINESTRING((0 0,1 1))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTILINESTRING((0 0,1 1))')"),
        new("multipolygon", "MULTIPOLYGON(((0 0,1 0,1 1,0 0)))", "ST_AsText(v)", "MULTIPOLYGON(((0 0,1 0,1 1,0 0)))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('MULTIPOLYGON(((0 0,1 0,1 1,0 0)))')"),
        new("geometrycollection", "GEOMETRYCOLLECTION(POINT(1 2))", "ST_AsText(v)", "GEOMETRYCOLLECTION(POINT(1 2))",
            "The MySQL driver returns a geometry as the server's binary form (SRID-prefixed WKB), not WKT. Read it with SELECT ST_AsText(v).",
            "ST_GeomFromText('GEOMETRYCOLLECTION(POINT(1 2))')"),
    };

    /// <summary>
    /// A declared provider limit: the column type cannot be read through the provider as itself. The case is
    /// reported as LIMIT with its reason; the test still checks that the recorded workaround query returns the
    /// expected value, and fails if it does not.
    /// </summary>
    public sealed record KnownLimit(string NativeType, string Literal, string WorkaroundProjection, object WorkaroundExpected, string Reason, string? InsertSql = null);

    public static IReadOnlyList<KnownLimit> PostgreSqlKnownLimits { get; } = new List<KnownLimit>
    {
        new("regclass", "pg_class", "v::oid", 1259u,
            "Npgsql 9 cannot read a regclass column through GetValue, GetString or GetFieldValue<uint> (InvalidCastException). " +
            "Read it with SELECT v::oid; the OID is exact, and the relation name would need a catalogue lookup."),
    };

    /// <summary>SingleStore's list: the MySQL cases without the spatial types, with three expectations SingleStore states differently.</summary>
    private static IReadOnlyList<NativeCase> SingleStoreCases() =>
        MySqlCases.Where(c => !c.NativeType.StartsWith("point") && !c.NativeType.StartsWith("linestring") &&
                              !c.NativeType.StartsWith("polygon") && !c.NativeType.StartsWith("multi") &&
                              !c.NativeType.StartsWith("geometrycollection") && c.Label != "float max")
            .Select(c => c.NativeType switch
            {
                "bool" => new NativeCase("bool", "1", (sbyte)1, "boolean (SingleStore returns TINYINT(1) as SByte)"),
                "json" => new NativeCase("json", "{\"a\": 1}", "{\"a\":1}", "json (SingleStore normalises the spacing)"),
                _ => c,
            }).ToList();

    public static IReadOnlyList<NativeCase> SqlServerCases { get; } = new List<NativeCase>
    {
        // Integers, exact and binary floating point
        new("tinyint", "255", (byte)255, "tinyint max (unsigned)"),
        new("smallint", "-32768", (short)-32768, "smallint min"),
        new("int", "-2147483648", -2147483648, "int min"),
        new("bigint", "-9223372036854775808", long.MinValue, "bigint min"),
        new("bigint", "9223372036854775807", long.MaxValue, "bigint max"),
        new("decimal(38,0)", "79228162514264337593543950335", decimal.MaxValue, "decimal(38,0) at the CLR decimal maximum"),
        new("numeric(38,10)", "1234.0123456789", 1234.0123456789m, "numeric(38,10)"),
        new("real", "3.4028235e38", float.MaxValue, "real max"),
        new("float", "1.7976931348623157e308", double.MaxValue, "float max"),
        new("float", "-1.7976931348623157e308", double.MinValue, "float min"),
        new("money", "922337203685477.5807", 922337203685477.5807m, "money max"),
        new("smallmoney", "214748.3647", 214748.3647m, "smallmoney max"),

        // Bit and booleans
        new("bit", "1", true, "bit"),

        // Character and Unicode
        new("char(10)", "abc", "abc       ", "char(10) pads"),
        new("varchar(max)", "h\u00e9llo", "h\u00e9llo", "varchar(max)"),
        new("nchar(10)", "\u6f22\u5b57", "\u6f22\u5b57        ", "nchar(10) pads", "N'\u6f22\u5b57'"),
        new("nvarchar(max)", "\u6f22\u5b57 'quoted'", "\u6f22\u5b57 'quoted'", "nvarchar(max) unicode", "N'\u6f22\u5b57 ''quoted'''"),
        new("text", "t", "t", "text (deprecated)"),
        new("ntext", "nt", "nt", "ntext (deprecated)"),
        new("sysname", "name", "name", "sysname"),

        // Binary
        new("binary(4)", "0x00FF0102", new byte[] { 0x00, 0xFF, 0x01, 0x02 }, "binary(4)", "0x00FF0102"),
        new("varbinary(max)", "0x00FF", new byte[] { 0x00, 0xFF }, "varbinary(max)", "0x00FF"),
        new("image", "0x01", new byte[] { 0x01 }, "image (deprecated)", "0x01"),

        // Date and time
        new("date", "0001-01-01", new DateTime(1, 1, 1), "date min"),
        new("date", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("time(7)", "23:59:59.9999999", new TimeSpan(23, 59, 59) + TimeSpan.FromTicks(9999999), "time(7) max"),
        new("datetime", "1753-01-01 00:00:00", new DateTime(1753, 1, 1), "datetime min"),
        new("smalldatetime", "1900-01-01 00:00:00", new DateTime(1900, 1, 1), "smalldatetime min"),
        new("datetime2(7)", "0001-01-01 00:00:00", DateTime.MinValue, "datetime2 min"),
        new("datetime2(7)", "9999-12-31 23:59:59.9999999", new DateTime(9999, 12, 31, 23, 59, 59).AddTicks(9999999), "datetime2(7) max"),
        new("datetimeoffset(7)", "2024-06-01 12:00:00.0000000 +05:30",
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(5.5)), "datetimeoffset keeps offset"),

        // Identifiers, markup and types
        new("uniqueidentifier", "ffffffff-ffff-ffff-ffff-ffffffffffff", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "uniqueidentifier max"),
        new("xml", "<a>1</a>", "<a>1</a>", "xml"),
        new("json", "{\"a\": 1}", "{\"a\":1}", "json (SQL Server normalises the spacing)"),
        new("hierarchyid", "/1/2/", "/1/2/", "hierarchyid"),
        new("sql_variant", "42", 42, "sql_variant holds an int", "CAST(42 AS int)"),
        new("geometry", "POINT(1 2)", Geometry.FromWellKnownBinary(Convert.FromHexString("0101000000000000000000F03F0000000000000040"), 0), "geometry (point)", "geometry::STGeomFromText('POINT(1 2)', 0)"),
        new("geography", "POINT(1 2)", Geography.FromWellKnownBinary(Convert.FromHexString("0101000000000000000000F03F0000000000000040"), 4326), "geography (point)", "geography::STGeomFromText('POINT(1 2)', 4326)"),
    };

    public static IReadOnlyList<NativeCase> OracleCases { get; } = new List<NativeCase>
    {
        // Numbers and floats
        new("NUMBER(38,0)", "79228162514264337593543950335", decimal.MaxValue, "NUMBER(38,0) at the CLR decimal maximum"),
        new("NUMBER", "0.0000000000000000000000000001", 0.0000000000000000000000000001m, "NUMBER unconstrained 1e-28"),
        new("BINARY_FLOAT", "3.4028234663852886e38", float.MaxValue, "BINARY_FLOAT max", "3.4028234663852886E38F"),
        new("BINARY_DOUBLE", "1.7976931348623157e308", double.MaxValue, "BINARY_DOUBLE max", "1.7976931348623157E308D"),

        // Characters
        new("CHAR(10)", "abc", "abc       ", "CHAR(10) pads"),
        new("VARCHAR2(4000)", "héllo", "héllo", "VARCHAR2 unicode"),
        new("NCHAR(10)", "漢字", "漢字        ", "NCHAR(10) pads", "N'漢字'"),
        new("NVARCHAR2(100)", "漢字 'quoted'", "漢字 'quoted'", "NVARCHAR2 unicode and quote", "N'漢字 ''quoted'''"),
        new("CLOB", "clob text", "clob text", "CLOB"),
        new("NCLOB", "nclob text", "nclob text", "NCLOB", "N'nclob text'"),
        new("LONG", "long text", "long text", "LONG (legacy)"),

        // Binary
        new("RAW(16)", "00FF", new byte[] { 0x00, 0xFF }, "RAW", "HEXTORAW('00FF')"),
        new("BLOB", "01", new byte[] { 0x01 }, "BLOB", "HEXTORAW('01')"),
        new("LONG RAW", "02", new byte[] { 0x02 }, "LONG RAW (legacy)", "HEXTORAW('02')"),

        // Dates and times
        new("DATE", "0001-01-01", new DateTime(1, 1, 1), "DATE min (.NET)", "DATE '0001-01-01'"),
        new("DATE", "9999-12-31", new DateTime(9999, 12, 31), "DATE max (.NET)", "DATE '9999-12-31'"),
        new("TIMESTAMP(9)", "2024-06-01 12:00:00.123456789", new DateTime(2024, 6, 1, 12, 0, 0).AddTicks(1234567),
            "TIMESTAMP(9) (truncated to .NET ticks)", "TIMESTAMP '2024-06-01 12:00:00.123456789'"),
        new("INTERVAL YEAR(4) TO MONTH", "+0002-03", 27L, "interval year to month (the driver returns total months)", "INTERVAL '2-3' YEAR TO MONTH"),
        new("INTERVAL DAY(2) TO SECOND(6)", "+01 02:03:04.000005", new TimeSpan(1, 2, 3, 4) + TimeSpan.FromTicks(50), "interval day to second",
            "INTERVAL '1 02:03:04.000005' DAY TO SECOND"),

        // Identifiers and markup
        new("UROWID", "AAAAAAAAAAAAAAAAAA", "00000000.0000.0000", "UROWID (the zero row address)", "CHARTOROWID('AAAAAAAAAAAAAAAAAA')"),
        new("XMLTYPE", "<a>1</a>", "<a>1</a>\n", "XMLTYPE (Oracle serialises it with a trailing newline)", "XMLTYPE('<a>1</a>')"),
    };

    public static IReadOnlyList<NativeCase> FirebirdCases { get; } = new List<NativeCase>
    {
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("SMALLINT", "32767", (short)32767, "smallint max"),
        new("INTEGER", "-2147483648", -2147483648, "integer min"),
        new("INTEGER", "2147483647", 2147483647, "integer max"),
        new("BIGINT", "-9223372036854775808", long.MinValue, "bigint min"),
        new("BIGINT", "9223372036854775807", long.MaxValue, "bigint max"),
        new("INT128", "-170141183460469231731687303715884105728", "-170141183460469231731687303715884105728", "int128 (text: the CLR Int128 is not read by the driver)"),
        new("NUMERIC(18,4)", "-99999999999999.9999", -99999999999999.9999m, "numeric(18,4) min"),
        new("DECIMAL(38,0)", "79228162514264337593543950335", decimal.MaxValue, "decimal(38,0) at the CLR maximum"),
        new("FLOAT", "3.402823e38", 3.402823e38f, "float (Firebird overflows the exact CLR float maximum; its largest FLOAT is about 3.402823E38)"),
        new("DOUBLE PRECISION", "1.7976931348623157e308", double.MaxValue, "double precision max"),
        new("BOOLEAN", "TRUE", true, "boolean"),
        new("CHAR(10)", "abc", "abc       ", "char(10) pads"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("VARCHAR(100)", "", "", "varchar empty string (Firebird keeps it distinct from NULL)"),
        new("BLOB SUB_TYPE TEXT", "clob", "clob", "blob sub_type text"),
        new("BLOB SUB_TYPE BINARY", "X'0001FF'", new byte[] { 0x00, 0x01, 0xFF }, "blob sub_type binary", "X'0001FF'"),
        new("DATE", "2024-06-01", new DateTime(2024, 6, 1), "date"),
        new("TIME", "12:34:56.7890", new TimeSpan(12, 34, 56) + TimeSpan.FromTicks(7890000), "time (0.1 ms)"),
        new("TIMESTAMP", "2024-06-01 12:34:56.7890", new DateTime(2024, 6, 1, 12, 34, 56) + TimeSpan.FromTicks(7890000), "timestamp (0.1 ms)"),
    };

    public static IReadOnlyList<NativeCase> Db2Cases { get; } = new List<NativeCase>
    {
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("INTEGER", "-2147483648", -2147483648, "integer min"),
        new("BIGINT", "9223372036854775807", long.MaxValue, "bigint max"),
        new("DECIMAL(31,0)", "79228162514264337593543950335", decimal.MaxValue, "decimal(31,0) at the CLR maximum"),
        new("REAL", "3.402823e38", 3.402823e38f, "real (Db2 overflows the exact CLR float maximum; its largest REAL is about 3.402823E38)"),
        new("DOUBLE", "1.7976931348623157e308", double.MaxValue, "double max"),
        new("DECFLOAT(34)", "1.5", 1.5m, "decfloat(34) (read as decimal)"),
        new("CHAR(10)", "abc", "abc       ", "char(10) pads"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("CLOB(1K)", "clob", "clob", "clob"),
        new("GRAPHIC(10)", "漢字", "漢字        ", "graphic(10) pads"),
        new("VARGRAPHIC(100)", "漢字", "漢字", "vargraphic"),
        new("CHAR(2) FOR BIT DATA", "0001", new byte[] { 0x00, 0x01 }, "char for bit data", "X'0001'"),
        new("VARCHAR(10) FOR BIT DATA", "0001", new byte[] { 0x00, 0x01 }, "varchar for bit data", "X'0001'"),
        new("BLOB(1K)", "0001", new byte[] { 0x00, 0x01 }, "blob", "BLOB(X'0001')"),
        new("DATE", "2024-06-01", new DateTime(2024, 6, 1), "date"),
        new("TIME", "12:34:56", new TimeSpan(12, 34, 56), "time"),
        new("TIMESTAMP(12)", "2024-06-01 12:34:56.1234567", new DateTime(2024, 6, 1, 12, 34, 56).AddTicks(1234567), "timestamp(12) at .NET tick precision (the driver refuses to truncate picoseconds)"),
        new("XML", "<a>1</a>", "<a>1</a>", "xml", "XMLPARSE(DOCUMENT '<a>1</a>' PRESERVE WHITESPACE)"),
    };

    private static readonly HashSet<string> CockroachTypes = new()
    {
        "smallint", "integer", "bigint", "numeric", "real", "double precision", "boolean", "char(10)", "varchar(100)",
        "text", "bytea", "bit(3)", "varbit", "date", "time", "timestamp", "timestamptz", "uuid", "json", "jsonb",
        "inet", "tsvector", "tsquery",
    };

    // CockroachDB INTEGER is 64-bit, so an integer read is a long.
    public static IReadOnlyList<NativeCase> CockroachDbCases { get; } =
        PostgreSqlCases.Where(c => CockroachTypes.Contains(c.NativeType))
            .Where(c => c.NativeType != "tstzrange")
            .Select(c => c.NativeType switch
            {
                "integer" => new NativeCase("integer", c.Literal, Convert.ToInt64(c.Expected), c.Label + " (CockroachDB INTEGER is 64-bit)"),
                "json" => new NativeCase("json", "{\"a\":1}", "{\"a\": 1}", "json (CockroachDB adds a space after the colon)"),
                _ => c,
            })
            .ToList();

    private static readonly HashSet<string> SpannerTypes = new()
    {
        "bigint", "numeric", "real", "double precision", "boolean", "varchar(100)", "text", "bytea", "date",
        "timestamptz", "uuid", "jsonb", "interval",
    };

    public static IReadOnlyList<NativeCase> SpannerCases { get; } =
        PostgreSqlCases.Where(c => SpannerTypes.Contains(c.NativeType)).ToList();

    public static IReadOnlyList<NativeCase> DuckDbCases { get; } = new List<NativeCase>
    {
        new("TINYINT", "-128", (sbyte)-128, "tinyint min"),
        new("UTINYINT", "255", (byte)255, "utinyint max"),
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("USMALLINT", "65535", (ushort)65535, "usmallint max"),
        new("INTEGER", "-2147483648", -2147483648, "integer min"),
        new("UINTEGER", "4294967295", 4294967295u, "uinteger max"),
        new("BIGINT", "-9223372036854775808", long.MinValue, "bigint min"),
        new("UBIGINT", "18446744073709551615", 18446744073709551615ul, "ubigint max"),
        new("HUGEINT", "-170141183460469231731687303715884105728", Int128.MinValue, "hugeint min (Int128)"),
        new("UHUGEINT", "340282366920938463463374607431768211455", UInt128.MaxValue, "uhugeint max (UInt128)"),
        new("DECIMAL(38,10)", "1234.0123456789", 1234.0123456789m, "decimal(38,10)"),
        new("FLOAT", "3.4028235e38", float.MaxValue, "float max"),
        new("DOUBLE", "1.7976931348623157e308", double.MaxValue, "double max"),
        new("BOOLEAN", "true", true, "boolean"),
        new("VARCHAR", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("BLOB", "'\\xDE\\xAD'::BLOB", new byte[] { 0xDE, 0xAD }, "blob", "'\\xDE\\xAD'::BLOB"),
        new("DATE", "1970-01-01", DateOnly.FromDateTime(new DateTime(1970, 1, 1)), "date epoch (driver returns DateOnly)"),
        new("TIME", "12:34:56.123456", TimeOnly.FromTimeSpan(new TimeSpan(12, 34, 56) + TimeSpan.FromTicks(1234560)), "time microseconds (driver returns TimeOnly)"),
        new("TIMESTAMP", "2024-06-01 12:34:56.123456", new DateTime(2024, 6, 1, 12, 34, 56) + TimeSpan.FromTicks(1234560), "timestamp microseconds"),
        new("TIMESTAMPTZ", "2024-06-01 12:34:56+00", new DateTime(2024, 6, 1, 12, 34, 56, DateTimeKind.Utc), "timestamptz (UTC)"),
        new("INTERVAL", "1 day 02:03:04", new TimeSpan(1, 2, 3, 4), "interval (day and time)"),
        new("UUID", "ffffffff-ffff-ffff-ffff-ffffffffffff", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), "uuid max"),
        new("JSON", "{\"a\": 1}", "{\"a\": 1}", "json"),
    };

    public static IReadOnlyList<NativeCase> SqliteCases { get; } = new List<NativeCase>
    {
        new("INTEGER", "-9223372036854775808", long.MinValue, "integer min (64-bit storage class)"),
        new("INTEGER", "9223372036854775807", long.MaxValue, "integer max"),
        new("REAL", "1.7976931348623157e308", double.MaxValue, "real max"),
        new("REAL", "4.9406564584124654e-324", double.Epsilon, "real denormal"),
        new("TEXT", "héllo 'q'", "héllo 'q'", "text unicode and quote"),
        new("TEXT", "", "", "text empty string"),
        new("BLOB", "X'00FF'", new byte[] { 0x00, 0xFF }, "blob", "X'00FF'"),
        new("NUMERIC", "1234.5", 1234.5m, "numeric (affinity)"),
        new("BOOLEAN", "1", 1L, "boolean (stored as INTEGER 1)"),
        new("DATE", "2024-06-01", "2024-06-01", "date (stored as TEXT)"),
        new("DATETIME", "2024-06-01 12:34:56", "2024-06-01 12:34:56", "datetime (stored as TEXT)"),
    };

    /// <summary>YugabyteDB's list: the PostgreSQL cases except xml (unsupported by the server) and xid8 (its max reads back as a different value); both are recorded in the datatype doc.</summary>
    public static IReadOnlyList<NativeCase> YugabyteDbCases { get; } =
        PostgreSqlCases.Where(c => c.NativeType is not ("xml" or "xid8")).ToList();

    public static IReadOnlyList<NativeCase> SnowflakeCases { get; } = new List<NativeCase>
    {
        new("NUMBER(38,0)", "-99999999999999999999999999999999999999", System.Numerics.BigInteger.Parse("-99999999999999999999999999999999999999"), "number(38,0) min (the driver returns BigInteger)"),
        new("NUMBER(38,0)", "79228162514264337593543950335", System.Numerics.BigInteger.Parse("79228162514264337593543950335"), "number(38,0) at the CLR decimal maximum (BigInteger)"),
        new("NUMBER(10,2)", "12345678.90", 12345678.90m, "number(10,2)"),
        new("FLOAT", "1.25e300", 1.25e300, "float (double) large value (Snowflake reads doubles to 15 significant digits; Double.MaxValue reads as Infinity)"),
        new("BOOLEAN", "TRUE", true, "boolean"),
        new("VARCHAR(100)", "h\u00e9llo 'q'", "h\u00e9llo 'q'", "varchar unicode and quote"),
        new("CHAR(10)", "abc", "abc", "char(10) (Snowflake does not pad CHAR)"),
        new("BINARY(4)", "X'00FF0102'", new byte[] { 0x00, 0xFF, 0x01, 0x02 }, "binary(4)", "X'00FF0102'"),
        new("VARBINARY(100)", "X'00FF'", new byte[] { 0x00, 0xFF }, "varbinary", "X'00FF'"),
        new("DATE", "0001-01-01", new DateTime(1, 1, 1), "date min"),
        new("DATE", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("TIME", "23:59:59", new DateTime(1970, 1, 1, 23, 59, 59), "time (the driver returns a DateTime on 1970-01-01)"),
        new("TIMESTAMP_NTZ", "2024-06-01 12:34:56.123456789", new DateTime(2024, 6, 1, 12, 34, 56).AddTicks(1234567),
            "timestamp_ntz (nanoseconds truncated to .NET ticks)"),
        new("VARIANT", "{\"a\": 1}", "{\n  \"a\": 1\n}", "variant (Snowflake returns pretty-printed JSON)", "PARSE_JSON('{\"a\": 1}')"),
        new("OBJECT", "{\"a\": 1}", "{\n  \"a\": 1\n}", "object (pretty-printed JSON)", "OBJECT_CONSTRUCT('a', 1)"),
        new("ARRAY", "[1,2,3]", "[\n  1,\n  2,\n  3\n]", "array (pretty-printed JSON)", "ARRAY_CONSTRUCT(1, 2, 3)"),
        new("GEOGRAPHY", "POINT(1 2)", "SRID=4326;POINT(1 2)", "geography (Snowflake returns EWKT)", "TO_GEOGRAPHY('POINT(1 2)')"),
        new("GEOMETRY", "POINT(1 2)", "SRID=0;POINT(1 2)", "geometry (Snowflake returns EWKT)", "TO_GEOMETRY('POINT(1 2)')"),
        new("VECTOR(FLOAT, 3)", "[1,2,3]", new float[] { 1f, 2f, 3f }, "vector(float,3)", "CAST(ARRAY_CONSTRUCT(1.0, 2.0, 3.0) AS VECTOR(FLOAT, 3))"),
    };

    public static IReadOnlyList<NativeCase> InformixCases { get; } = new List<NativeCase>
    {
        new("SMALLINT", "-32767", (short)-32767, "smallint min (the server reserves -32768)"),
        new("INTEGER", "2147483647", 2147483647, "integer max"),
        new("INT8", "9223372036854775807", 9223372036854775807L, "int8 max"),
        new("DECIMAL(31,0)", "79228162514264337593543950335", decimal.MaxValue, "decimal(31,0) at the CLR maximum"),
        new("FLOAT", "1.25e300", 1.25e300, "float (double)"),
        new("SMALLFLOAT", "3.402823e38", 3.402823e38f, "smallfloat (real) largest representable"),
        new("MONEY(16,2)", "12345678901234.56", 12345678901234.56m, "money(16,2)"),
        new("BOOLEAN", "t", true, "boolean"),
        new("CHAR(10)", "abc", "abc       ", "char(10) pads"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("NCHAR(10)", "漢字", "漢字    ", "nchar(10) (padded by bytes, not characters)"),
        new("NVARCHAR(100)", "漢字", "漢字", "nvarchar"),
        new("LVARCHAR(100)", "lvar", "lvar", "lvarchar"),
        new("DATE", "01/01/1900", new DateTime(1900, 1, 1), "date (Informix DATE range is narrower than .NET's; DBDATE is month-day-year)"),
        new("DATE", "12/31/2999", new DateTime(2999, 12, 31), "date upper bound used"),
        new("DATETIME YEAR TO FRACTION(5)", "2024-06-01 12:34:56.12345", new DateTime(2024, 6, 1, 12, 34, 56).AddTicks(1234500),
            "datetime year to fraction(5)"),
        new("DATETIME HOUR TO SECOND", "12:34:56", new TimeSpan(12, 34, 56), "datetime hour to second (the driver's form may vary)"),
        new("INTERVAL DAY TO FRACTION(5)", "1 02:03:04.12345", new TimeSpan(1, 2, 3, 4) + TimeSpan.FromTicks(1234500),
            "interval day to fraction(5)"),
        new("INTERVAL YEAR TO MONTH", "2-3", "2-03", "interval year to month (the driver returns the text form)"),
        new("JSON", "{\"a\": 1}", "{\"a\": 1}", "json"),
    };

    public static IReadOnlyList<NativeCase> SybaseASECases { get; } = new List<NativeCase>
    {
        new("TINYINT", "255", (byte)255, "tinyint max (unsigned)"),
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("UNSIGNED SMALLINT", "65535", (ushort)65535, "unsigned smallint max"),
        new("INT", "-2147483648", -2147483648, "int min"),
        new("UNSIGNED INT", "4294967295", 4294967295u, "unsigned int max"),
        new("BIGINT", "-9223372036854775808", long.MinValue, "bigint min"),
        new("UNSIGNED BIGINT", "18446744073709551615", 18446744073709551615ul, "unsigned bigint max"),
        new("NUMERIC(38,0)", "79228162514264337593543950335", decimal.MaxValue, "numeric(38,0) at the CLR maximum"),
        new("REAL", "3.402823e38", 3.402823e38f, "real largest representable"),
        new("FLOAT", "1.7976931348623157e308", double.MaxValue, "float max"),
        new("MONEY", "922337203685477.5807", 922337203685477.5807m, "money max"),
        new("SMALLMONEY", "214748.3647", 214748.3647m, "smallmoney max"),
        new("BIT", "1", true, "bit (Sybase ASE does not create a nullable BIT)", null, "BIT NOT NULL"),
        new("CHAR(10)", "abc", "abc", "char(10) (Sybase ASE does not pad CHAR)"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("TEXT", "t", "t", "text"),
        new("BINARY(4)", "0x00FF0102", new byte[] { 0x00, 0xFF, 0x01, 0x02 }, "binary(4)", "0x00FF0102"),
        new("VARBINARY(100)", "0x00FF", new byte[] { 0x00, 0xFF }, "varbinary", "0x00FF"),
        new("IMAGE", "0x01", new byte[] { 0x01 }, "image", "0x01"),
        new("DATE", "0001-01-01", new DateTime(1, 1, 1), "date min"),
        new("DATE", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("TIME", "23:59:59.999", new TimeSpan(23, 59, 59) + TimeSpan.FromMilliseconds(996), "time (Sybase ASE stores 1/300 second: 999 ms reads back as 996)"),
        new("DATETIME", "1753-01-01 00:00:00.000", new DateTime(1753, 1, 1), "datetime min"),
        new("SMALLDATETIME", "1900-01-01 00:00:00", new DateTime(1900, 1, 1), "smalldatetime min"),
        new("BIGDATETIME", "0001-01-01 00:00:00.000000", new DateTime(1, 1, 1), "bigdatetime min"),

        // NCHAR, NVARCHAR, UNICHAR and UNIVARCHAR are excluded: the test server uses a single-byte character set, so Unicode text reads back as '?'. See the datatype doc.
    };

    public static IReadOnlyList<NativeCase> SapHanaCases { get; } = new List<NativeCase>
    {
        new("TINYINT", "255", (byte)255, "tinyint max"),
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("INTEGER", "-2147483648", -2147483648, "integer min"),
        new("BIGINT", "9223372036854775807", long.MaxValue, "bigint max"),
        new("DECIMAL(38,10)", "1234.0123456789", 1234.0123456789m, "decimal(38,10)"),
        new("SMALLDECIMAL", "1234.5", 1234.5m, "smalldecimal"),
        new("REAL", "3.402823e38", 3.402823e38f, "real largest representable"),
        new("DOUBLE", "1.7976931348623157e308", double.MaxValue, "double max"),
        new("BOOLEAN", "TRUE", true, "boolean", "TRUE"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("NVARCHAR(100)", "漢字", "漢字", "nvarchar"),
        new("ALPHANUM(10)", "abc123", "abc123", "alphanum"),
        new("SHORTTEXT(100)", "short", "short", "shorttext"),
        new("VARBINARY(100)", "X'00FF'", new byte[] { 0x00, 0xFF }, "varbinary", "X'00FF'"),
        new("BLOB", "X'01'", new byte[] { 0x01 }, "blob", "X'01'"),
        new("CLOB", "clob", "clob", "clob"),
        new("NCLOB", "nclob", "nclob", "nclob"),
        new("DATE", "0001-01-01", new DateTime(1, 1, 1), "date min"),
        new("DATE", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("TIME", "23:59:59", new TimeSpan(23, 59, 59), "time"),
        new("SECONDDATE", "1000-01-01 00:00:00", new DateTime(1000, 1, 1), "seconddate min"),
        new("TIMESTAMP", "2024-06-01 12:34:56", new DateTime(2024, 6, 1, 12, 34, 56), "timestamp (the driver returns no fraction for this value)"),
    };

    public static IReadOnlyList<NativeCase> InterBaseCases { get; } = new List<NativeCase>
    {
        new("SMALLINT", "-32768", (short)-32768, "smallint min"),
        new("INTEGER", "2147483647", 2147483647, "integer max"),
        new("NUMERIC(18,4)", "-99999999999999.9999", -99999999999999.9999m, "numeric(18,4) min"),
        new("DECIMAL(18,4)", "12.3456", 12.3456m, "decimal(18,4)"),
        new("FLOAT", "1e38", 1e38f, "float (InterBase overflows the largest single-precision value)"),
        new("DOUBLE PRECISION", "1e300", 1e300, "double precision (15 significant digits)"),
        new("BOOLEAN", "TRUE", true, "boolean", "TRUE"),
        new("CHAR(10)", "abc", "abc       ", "char(10) pads"),
        new("VARCHAR(100)", "héllo 'q'", "héllo 'q'", "varchar unicode and quote"),
        new("BLOB SUB_TYPE 1", "clob", "clob", "blob sub_type 1 (text)"),
        new("DATE", "0001-01-01", new DateTime(1, 1, 1), "date min"),
        new("DATE", "9999-12-31", new DateTime(9999, 12, 31), "date max"),
        new("TIME", "23:59:59.9999", new TimeSpan(23, 59, 59) + TimeSpan.FromTicks(9999000), "time (0.1 ms)"),
        new("TIMESTAMP", "2024-06-01 12:34:56.1234", new DateTime(2024, 6, 1, 12, 34, 56) + TimeSpan.FromTicks(1234000), "timestamp (0.1 ms)"),
        new("CHAR(4) CHARACTER SET OCTETS", "bound", new byte[] { 0x00, 0xFF, 0x01, 0x02 }, "char octets (bound parameter)", BoundParameter),
        new("BLOB SUB_TYPE 0", "bound", new byte[] { 0x00, 0x01, 0xFF }, "blob sub_type 0 (bound parameter)", BoundParameter),
    };

    public static IReadOnlyDictionary<SupportedDatabase, IReadOnlyList<NativeCase>> CasesByDatabase { get; } =
        new Dictionary<SupportedDatabase, IReadOnlyList<NativeCase>>
        {
            [SupportedDatabase.PostgreSql] = PostgreSqlCases,
            [SupportedDatabase.MySql] = MySqlCases,
            [SupportedDatabase.MariaDb] = MySqlCases,
            [SupportedDatabase.SingleStore] = SingleStoreCases(),
            [SupportedDatabase.SqlServer] = SqlServerCases,
            [SupportedDatabase.Oracle] = OracleCases,
            [SupportedDatabase.Informix] = InformixCases,
            [SupportedDatabase.SybaseASE] = SybaseASECases,
            [SupportedDatabase.SapHana] = SapHanaCases,
            [SupportedDatabase.InterBase] = InterBaseCases,
            [SupportedDatabase.Snowflake] = SnowflakeCases,
            [SupportedDatabase.CockroachDb] = CockroachDbCases,
            [SupportedDatabase.Spanner] = SpannerCases,
            [SupportedDatabase.YugabyteDb] = YugabyteDbCases,
            [SupportedDatabase.DuckDB] = DuckDbCases,
            [SupportedDatabase.Sqlite] = SqliteCases,
            [SupportedDatabase.Firebird] = FirebirdCases,
            [SupportedDatabase.Db2] = Db2Cases,
            [SupportedDatabase.TiDb] = MySqlCases.Where(c => !c.NativeType.StartsWith("point") && !c.NativeType.StartsWith("linestring") && !c.NativeType.StartsWith("polygon") && !c.NativeType.StartsWith("multi") && !c.NativeType.StartsWith("geometrycollection")).ToList(),
        };

    public static IReadOnlyDictionary<SupportedDatabase, IReadOnlyList<KnownLimit>> KnownLimitsByDatabase { get; } =
        new Dictionary<SupportedDatabase, IReadOnlyList<KnownLimit>>
        {
            [SupportedDatabase.SingleStore] = new List<KnownLimit>
            {
                new("float", "3.4028235e38", "v", 3.40282e38f,
                    "SingleStore rounds the FLOAT maximum on write; it reads back as 3.40282E+38 (observed). The value is recorded, not corrected."),
            },
            [SupportedDatabase.PostgreSql] = PostgreSqlKnownLimits,
            [SupportedDatabase.MySql] = MySql8SpatialKnownLimits,
            [SupportedDatabase.MariaDb] = MySqlSpatialKnownLimits,
        };

    [SkippableFact]
    public async Task EveryNativeType_RoundTripsOnEveryDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!CasesByDatabase.TryGetValue(provider, out var cases))
            {
                Assert.Fail($"No native type cases are recorded for {provider}; every supported database needs a list.");
            }

            var failures = new List<string>();
            var index = 0;
            foreach (var c in cases)
            {
                index++;
                var table = IntegrationObjectNameHelper.Table(context, $"native_rt_{index}");
                var error = await RoundTripAsync(context, provider, table, c);
                if (error is not null)
                {
                    failures.Add($"{c.Label} ({c.NativeType}): {error}");
                }
            }

            var limits = new List<string>();
            foreach (var limit in KnownLimitsByDatabase.GetValueOrDefault(provider) ?? new List<KnownLimit>())
            {
                index++;
                var table = IntegrationObjectNameHelper.Table(context, $"native_rt_{index}");
                var error = await KnownLimitAsync(context, provider, table, limit);
                if (error is not null)
                {
                    failures.Add($"{limit.NativeType} (known limit): {error}");
                }
                else
                {
                    limits.Add($"{limit.NativeType} LIMIT: {limit.Reason}");
                }
            }

            Output.WriteLine($"Native round trip on PostgreSql: {cases.Count - failures.Count + limits.Count} of {cases.Count + KnownLimitsByDatabase.GetValueOrDefault(provider)?.Count ?? 0} passed or declared; {limits.Count} declared limit(s)");
            foreach (var limit in limits)
            {
                Output.WriteLine("  " + limit);
            }
            foreach (var failure in failures)
            {
                Output.WriteLine("  FAIL " + failure);
            }

            Assert.True(failures.Count == 0,
                $"{failures.Count} native type case(s) failed:\n  " + string.Join("\n  ", failures));
        });
    }

    private static async Task<string?> KnownLimitAsync(IDatabaseContext context, SupportedDatabase provider, string table, KnownLimit limit)
    {
        try
        {
            await DropIfPresentAsync(context, table);

            await using (var create = context.CreateSqlContainer($"CREATE TABLE {table} (v {limit.NativeType})"))
            {
                await create.ExecuteNonQueryAsync();
            }

            var literal = limit.Literal.Replace("'", "''");
            var value = limit.InsertSql ?? (CastsLiterals(provider)
                ? $"CAST('{literal}' AS {limit.NativeType})"
                : $"'{literal}'");
            await using (var insert = context.CreateSqlContainer($"INSERT INTO {table} (v) VALUES ({value})"))
            {
                await insert.ExecuteNonQueryAsync();
            }

            await using var select = context.CreateSqlContainer($"SELECT {limit.WorkaroundProjection} FROM {table}");
            await using var reader = await select.ExecuteReaderAsync();
            if (!reader.Read())
            {
                return "workaround returned no row";
            }

            var actual = reader.GetValue(0);
            return Same(limit.WorkaroundExpected, actual)
                ? null
                : $"workaround {limit.WorkaroundProjection} expected {Describe(limit.WorkaroundExpected)} but read {Describe(actual)}";
        }
        catch (Exception ex)
        {
            return $"workaround {limit.WorkaroundProjection} failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            await DropIfPresentAsync(context, table);
        }
    }

    /// <summary>The value the library produces from a driver value for a CLR type: the registered coercion when one reads it, else the driver's value.</summary>
    private static object LibraryRead(SupportedDatabase provider, object driverValue, Type target)
    {
        if (driverValue.GetType() == target)
        {
            return driverValue;
        }

        return CoercionRegistry.Shared.TryRead(new DbValue(driverValue), target, out var coerced, provider) && coerced is not null
            ? coerced
            : driverValue;
    }

    /// <summary>Drops the scratch table; a table that does not exist is not an error (Firebird has no DROP ... IF EXISTS).</summary>
    private static async Task DropIfPresentAsync(IDatabaseContext context, string table)
    {
        try
        {
            await using var drop = context.CreateSqlContainer($"DROP TABLE {table}");
            await drop.ExecuteNonQueryAsync();
        }
        catch (DatabaseException)
        {
            // The table was not there.
        }
    }

    /// <summary>The InsertSql marker for a value written as a bound parameter (for types with no literal form).</summary>
    private const string BoundParameter = "{param}";

    /// <summary>Databases whose literals are written as CAST('text' AS type); the others take a quoted literal.</summary>
    private static bool CastsLiterals(SupportedDatabase provider) =>
        provider is SupportedDatabase.PostgreSql or SupportedDatabase.SqlServer or SupportedDatabase.SybaseASE;

    private static async Task<string?> RoundTripAsync(IDatabaseContext context, SupportedDatabase provider, string table, NativeCase c)
    {
        try
        {
            await DropIfPresentAsync(context, table);

            // Oracle keeps LOB segments out of the SYSTEM tablespace, so the table goes to USERS (as the fidelity matrix does).
            var tablespace = provider == SupportedDatabase.Oracle ? " TABLESPACE USERS" : "";
            await using (var create = context.CreateSqlContainer($"CREATE TABLE {table} (v {c.ColumnDefinition ?? c.NativeType}){tablespace}"))
            {
                await create.ExecuteNonQueryAsync();
            }

            var literal = c.Literal.Replace("'", "''");
            if (c.InsertSql == BoundParameter)
            {
                // A type with no literal form is written as a bound parameter.
                await using var bound = context.CreateSqlContainer($"INSERT INTO {table} (v) VALUES (");
                var parameter = bound.AddParameterWithValue("v", DbType.Binary, (byte[])c.Expected);
                bound.Query.Append(bound.MakeParameterName(parameter));
                bound.Query.Append(")");
                await bound.ExecuteNonQueryAsync();
            }
            else
            {
                var value = c.InsertSql ?? (CastsLiterals(provider)
                    ? $"CAST('{literal}' AS {c.NativeType})"
                    : $"'{literal}'");
                // Snowflake's VALUES clause takes no function calls, so its values go in through INSERT ... SELECT.
                var insertSql = provider == SupportedDatabase.Snowflake
                    ? $"INSERT INTO {table} (v) SELECT {value}"
                    : $"INSERT INTO {table} (v) VALUES ({value})";
                await using var insert = context.CreateSqlContainer(insertSql);
                await insert.ExecuteNonQueryAsync();
            }

            await using var select = context.CreateSqlContainer($"SELECT v FROM {table}");
            await using var reader = await select.ExecuteReaderAsync();
            if (!reader.Read())
            {
                return "no row returned";
            }

            if (reader.IsDBNull(0))
            {
                return "returned NULL";
            }

            // The value the library reads: the driver's value passed through the registered coercions for the expected type.
            var actual = LibraryRead(provider, reader.GetValue(0), c.Expected.GetType());
            return Same(c.Expected, actual) ? null : $"expected {Describe(c.Expected)} but read {Describe(actual)}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            await DropIfPresentAsync(context, table);
        }
    }

    private static bool Same(object expected, object actual)
    {
        switch (expected)
        {
            case SpatialValue spatial:
                // Equal when the SRID and the well-known binary match; the provider's own form is not compared.
                return actual is SpatialValue gotSpatial && gotSpatial.Srid == spatial.Srid &&
                       spatial.WellKnownBinary.Span.SequenceEqual(gotSpatial.WellKnownBinary.Span);
            case byte[] bytes:
                return actual is byte[] got && bytes.AsSpan().SequenceEqual(got);
            case Array elements:
                return actual is Array gotArray && elements.Length == gotArray.Length &&
                       Enumerable.Range(0, elements.Length).All(i => Equals(elements.GetValue(i), gotArray.GetValue(i)));
            case BitArray bits:
                return actual is BitArray gotBits && bits.Length == gotBits.Length &&
                       Enumerable.Range(0, bits.Length).All(i => bits[i] == gotBits[i]);
            case string text:
                return string.Equals(text, Convert.ToString(actual, CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
            case float f:
                return actual is IConvertible && Convert.ToSingle(actual, CultureInfo.InvariantCulture) == f;
            case double d:
                return actual is IConvertible && Convert.ToDouble(actual, CultureInfo.InvariantCulture) == d;
            case decimal m:
                return actual is IConvertible && Convert.ToDecimal(actual, CultureInfo.InvariantCulture) == m;
            case DateTime dt:
                return actual is DateTime got2 && got2.Ticks == dt.Ticks;
            case TimeSpan ts:
                return actual is TimeSpan got3 && got3 == ts;
            default:
                return Equals(expected, actual);
        }
    }

    private static string Describe(object value) => value switch
    {
        byte[] bytes => "bytes[" + string.Join(",", bytes) + "]",
        string text => "string[len=" + text.Length + ", " + string.Join(" ", text.Select(ch => ((int)ch).ToString("X4"))) + "]",
        SpatialValue spatial => $"{spatial.GetType().Name}[srid={spatial.Srid}, wkb={Convert.ToHexString(spatial.WellKnownBinary.Span)}]",
        BitArray bits => "bits[" + string.Concat(Enumerable.Range(0, bits.Length).Select(i => bits[i] ? "1" : "0")) + "]",
        _ => value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture)
    };
}
