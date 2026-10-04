using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// TYPE-003: byte, sbyte, ushort, uint, ulong, char and an in-day TimeSpan round-trip through
/// TableGateway on every database at their minimum, maximum and a middle value — each column is
/// the narrowest standard type that holds the full .NET range. The database layer owns the
/// conversion; the application writes plain properties.
/// </summary>
[Collection("IntegrationTests")]
public class SmallAndUnsignedTypeRoundTripTests : DatabaseTestBase
{
    private const string TableName = "small_unsigned_types";

    public SmallAndUnsignedTypeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    private static string Small(SupportedDatabase p) => p == SupportedDatabase.Oracle ? "NUMBER(5)" : p == SupportedDatabase.Spanner ? "BIGINT" : "SMALLINT";   // byte, sbyte
    private static string Int(SupportedDatabase p) => p == SupportedDatabase.Oracle ? "NUMBER(10)" : p == SupportedDatabase.Spanner ? "BIGINT" : "INTEGER";    // ushort
    private static string Big(SupportedDatabase p) => IntegrationObjectNameHelper.BigIntType(p);     // uint
    private static string Unsigned64(SupportedDatabase p) => p switch              // ulong
    {
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.Oracle => "NUMBER(20)",
        SupportedDatabase.Spanner => "NUMERIC",
        // InterBase's exact numerics stop at 18 digits; ulong.MaxValue has 20 (HARN-011).
        SupportedDatabase.InterBase => "VARCHAR(20)",
        _ => "DECIMAL(20,0)"
    };
    private static string Char1(SupportedDatabase p) => p == SupportedDatabase.Spanner ? "VARCHAR(1)" : "CHAR(1)";                // char
    private static string Time(SupportedDatabase p) => p switch                    // TimeSpan in a day
    {
        SupportedDatabase.Sqlite => "TEXT",
        SupportedDatabase.Oracle => "INTERVAL DAY(0) TO SECOND(0)",
        SupportedDatabase.Informix => "DATETIME HOUR TO SECOND",
        SupportedDatabase.Spanner => "VARCHAR(16)",
        _ => "TIME"
    };

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var w = (string name) => context.WrapObjectName(name);
        var idType = provider == SupportedDatabase.Spanner ? "BIGINT" : "INTEGER";
        // A database kept between runs (InterBase's externally managed container) still has the
        // table from the last run (HARN-011).
        await DropTableIfExistsAsync(context, TableName);
        var suffix = provider == SupportedDatabase.FlatFile ? " WITH (NULLTOKEN = '<<NULL>>')" : string.Empty;
        await using var sc = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TableName)} (" +
            $"{w("id")} {idType} NOT NULL PRIMARY KEY, " +
            $"{w("byte_value")} {Small(provider)} NOT NULL, " +
            $"{w("sbyte_value")} {Small(provider)} NOT NULL, " +
            $"{w("ushort_value")} {Int(provider)} NOT NULL, " +
            $"{w("uint_value")} {Big(provider)} NOT NULL, " +
            $"{w("ulong_value")} {Unsigned64(provider)} NOT NULL, " +
            $"{w("char_value")} {Char1(provider)} NOT NULL, " +
            $"{w("span_value")} {Time(provider)} NOT NULL)" + suffix);
        await sc.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task SmallUnsignedCharAndTimeSpan_RoundTripAtTheirLimits()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<Row, int>(context);
            var rows = new[]
            {
                new Row { Id = 1, Byte = byte.MinValue, SByte = sbyte.MinValue, UShort = ushort.MinValue,
                    UInt = uint.MinValue, ULong = ulong.MinValue, Char = 'A', Span = TimeSpan.Zero },
                new Row { Id = 2, Byte = byte.MaxValue, SByte = sbyte.MaxValue, UShort = ushort.MaxValue,
                    UInt = uint.MaxValue, ULong = ulong.MaxValue, Char = '~', Span = new TimeSpan(23, 59, 59) },
                new Row { Id = 3, Byte = 128, SByte = -1, UShort = 40000, UInt = 3_000_000_000u,
                    ULong = 10_000_000_000_000_000_000ul, Char = 'z', Span = new TimeSpan(13, 45, 30) }
            };

            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context), $"[{provider}] insert {row.Id}");
            }

            foreach (var expected in rows)
            {
                var actual = await gateway.RetrieveOneAsync(expected.Id, context);
                Assert.NotNull(actual);
                Assert.Equal(expected.Byte, actual!.Byte);
                Assert.Equal(expected.SByte, actual.SByte);
                Assert.Equal(expected.UShort, actual.UShort);
                Assert.Equal(expected.UInt, actual.UInt);
                Assert.Equal(expected.ULong, actual.ULong);
                Assert.Equal(expected.Char, actual.Char);
                Assert.Equal(expected.Span, actual.Span);
            }
        });
    }

    [Table(TableName)]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("byte_value", DbType.Byte)] public byte Byte { get; set; }
        [Column("sbyte_value", DbType.SByte)] public sbyte SByte { get; set; }
        [Column("ushort_value", DbType.UInt16)] public ushort UShort { get; set; }
        [Column("uint_value", DbType.UInt32)] public uint UInt { get; set; }
        [Column("ulong_value", DbType.UInt64)] public ulong ULong { get; set; }
        [Column("char_value", DbType.StringFixedLength)] public char Char { get; set; }
        [Column("span_value", DbType.Time)] public TimeSpan Span { get; set; }
    }
}
