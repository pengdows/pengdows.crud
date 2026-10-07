using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using FirebirdSql.Data.Types;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, all found live on Oracle 23ai (ODP.NET 23.8) and Firebird 5 (FirebirdClient 10.3.3):
/// - ODP.NET rejects DbType.DateTime2 and DbType.Xml ("Value does not fall within the expected range").
/// - ODP.NET binds DbType.Double/Single on NUMBER, so a double beyond 1e126 overflowed; System.Double
///   is IEEE, so it binds as BINARY_DOUBLE/BINARY_FLOAT (which still stores into NUMBER/FLOAT columns).
/// - ODP.NET reads LONG/LONG RAW as empty unless InitialLONGFetchSize is -1 (or the select has the key).
/// - ODP.NET can't read a NUMBER beyond decimal as decimal; a double property reads it with GetDouble.
/// - FirebirdClient reports BINARY/VARBINARY as string while returning byte[].
/// - Firebird TIME WITH TIME ZONE takes an FbZonedTime; a DateTimeOffset declared DbType.Time is one.
/// </summary>
public sealed class OracleFirebirdTypeFixesTests
{
    public enum OracleDbTypeStub { Int16 = 111, Double = 108, BinaryDouble = 132, BinaryFloat = 133, Decimal = 107 }

    private sealed class OracleLikeParameter : fakeDbParameter
    {
        public OracleDbTypeStub OracleDbType { get; set; }
    }

    private sealed class OracleLikeFactory : DbProviderFactory
    {
        public override DbParameter CreateParameter() => new OracleLikeParameter();
        public override DbConnection CreateConnection() => new fakeDbConnection();
        public override DbCommand CreateCommand() => new fakeDbCommand();
    }

    private static OracleDialect Oracle() => new(new OracleLikeFactory(), NullLogger<OracleDialect>.Instance);

    [Theory]
    [InlineData(DbType.DateTime2, DbType.DateTime)]
    [InlineData(DbType.Xml, DbType.String)]
    public void Oracle_UnsupportedDbTypes_AreRemapped(DbType declared, DbType bound)
    {
        object value = declared == DbType.Xml ? "<a/>" : new DateTime(2026, 10, 1, 13, 45, 30).AddTicks(1234567);

        var parameter = Oracle().CreateDbParameter("p", declared, value);

        Assert.Equal(bound, parameter.DbType);
        Assert.Equal(value, parameter.Value);
    }

    [Theory]
    [InlineData(DbType.Double, OracleDbTypeStub.BinaryDouble)]
    [InlineData(DbType.Single, OracleDbTypeStub.BinaryFloat)]
    public void Oracle_DoubleAndSingle_BindAsBinaryFloatingPoint(DbType declared, OracleDbTypeStub expected)
    {
        object value = declared == DbType.Double ? -1.25e300 : 1.5f;

        var parameter = Assert.IsType<OracleLikeParameter>(Oracle().CreateDbParameter("p", declared, value));

        Assert.Equal(expected, parameter.OracleDbType);
        Assert.Equal(value, parameter.Value);
    }

    [Theory]
    [InlineData(SupportedDatabase.Oracle, -1)]
    [InlineData(SupportedDatabase.PostgreSql, 0)]
    public async Task Commands_OracleFetchesLongDataUpFront(SupportedDatabase product, int expected)
    {
        var (context, exec) = Context(product);
        await using var _ = context;

        await using (var sc = context.CreateSqlContainer("SELECT 1 FROM t"))
        {
            await sc.ExecuteNonQueryAsync();
        }

        Assert.Equal(expected, Assert.Single(exec.CreatedCommands, c => c.CommandText == "SELECT 1 FROM t").InitialLONGFetchSize);
    }

    [Table("num_rows")]
    public sealed class NumberRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("f", DbType.Double)] public double F { get; set; }
        [Column("g", DbType.Single)] public float? G { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_NumberBeyondDecimal_ReadsIntoDouble()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle);
        await using var _ = context;
        exec.EnqueueReaderResult(NumberReader());

        var row = await new TableGateway<NumberRow, int>(context).RetrieveOneAsync(1);

        Assert.Equal(1.25e100, row!.F);
        Assert.Equal(3.0e38f, row.G);
    }

    [Fact]
    public async Task DataReaderMapper_NumberBeyondDecimal_ReadsIntoDouble()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle);
        await using var _ = context;
        exec.EnqueueReaderResult(NumberReader());
        await using var sc = context.CreateSqlContainer("SELECT id, f, g FROM num_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<NumberRow>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(1.25e100, row.F);
        Assert.Equal(3.0e38f, row.G);
    }

    private static fakeDbDataReader NumberReader() =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["f"] = 1.25e100, ["g"] = 3.0e38 } })
        {
            DoubleBeyondDecimalColumns = new HashSet<string> { "f", "g" }
        };

    [Table("bin_rows")]
    public sealed class BinaryRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("b", DbType.Binary)] public byte[]? B { get; set; }
    }

    [Fact]
    public async Task RetrieveOneAsync_BinaryReportedAsString_ReadsTheBytes()
    {
        var (context, exec) = Context(SupportedDatabase.Firebird);
        await using var _ = context;
        exec.EnqueueReaderResult(BinaryReader());

        var row = await new TableGateway<BinaryRow, int>(context).RetrieveOneAsync(1);

        Assert.Equal(new byte[] { 0, 1, 2, 0xFE, 0xFF }, row!.B);
    }

    [Fact]
    public async Task DataReaderMapper_BinaryReportedAsString_ReadsTheBytes()
    {
        var (context, exec) = Context(SupportedDatabase.Firebird);
        await using var _ = context;
        exec.EnqueueReaderResult(BinaryReader());
        await using var sc = context.CreateSqlContainer("SELECT id, b FROM bin_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<BinaryRow>(reader, new MapperOptions(Strict: true, ColumnsOnly: true)));

        Assert.Equal(new byte[] { 0, 1, 2, 0xFE, 0xFF }, row.B);
    }

    private static fakeDbDataReader BinaryReader() =>
        new(new[] { new Dictionary<string, object> { ["id"] = 1, ["b"] = new byte[] { 0, 1, 2, 0xFE, 0xFF } } })
        {
            BinaryReportedAsStringColumns = new HashSet<string> { "b" }
        };

    [Fact]
    public async Task Firebird_DateTimeOffsetDeclaredTime_IsAZonedUtcTime()
    {
        var (context, _) = Context(SupportedDatabase.Firebird, "5.0.2");
        await using var __ = context;

        var parameter = context.CreateDbParameter("p", DbType.Time,
            new DateTimeOffset(1, 1, 1, 13, 45, 30, TimeSpan.FromHours(-5)));

        var zoned = Assert.IsType<FbZonedTime>(parameter.Value);
        Assert.Equal(new TimeSpan(18, 45, 30), zoned.Time);
        Assert.Equal("UTC", zoned.TimeZone);
    }

    [Fact]
    public void Coerce_FbZonedTime_ReadsAsDateTimeOffset()
    {
        var read = TypeCoercionHelper.Coerce(new FbZonedTime(new TimeSpan(18, 45, 30), "UTC"), typeof(FbZonedTime), typeof(DateTimeOffset));

        var value = Assert.IsType<DateTimeOffset>(read);
        Assert.Equal(new DateTimeOffset(1, 1, 1, 18, 45, 30, TimeSpan.Zero), value);
    }

    // A parameter with no column is sent as its UTC instant, which every column type stores correctly;
    // the gateways send the offset to a TIMESTAMP WITH TIME ZONE column (OffsetlessColumnBindingTests).
    private static readonly DateTimeOffset Zoned = new DateTimeOffset(2026, 10, 1, 13, 45, 30, TimeSpan.FromHours(-5)).AddTicks(1234567);

    [Fact]
    public void Oracle_DateTimeOffset_BareParameterIsItsUtcInstant()
    {
        var parameter = Oracle().CreateDbParameter("p", DbType.DateTimeOffset, Zoned);

        var bound = Assert.IsType<DateTimeOffset>(parameter.Value);
        Assert.Equal(Zoned, bound);
        Assert.Equal(TimeSpan.Zero, bound.Offset);
    }

    [Table("tz_rows")]
    public sealed class ZonedRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("tz", DbType.DateTimeOffset)] public DateTimeOffset Tz { get; set; }
        [Column("ts", DbType.DateTimeOffset)] public DateTimeOffset? Ts { get; set; }
    }

    // ODP.NET 3.21 returns a TIMESTAMP WITH TIME ZONE from GetValue as its wall time with no offset
    // (read as UTC, it was 5 hours off for -05:00); its GetDateTimeOffset returns the value. A plain
    // TIMESTAMP ("ts") still reads as UTC.
    [Fact]
    public async Task RetrieveOneAsync_OffsetDroppedByGetValue_KeepsTheOffset()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle);
        await using var _ = context;
        exec.EnqueueReaderResult(ZonedReader());

        var row = await new TableGateway<ZonedRow, int>(context).RetrieveOneAsync(1);

        AssertZoned(row!);
    }

    [Fact]
    public async Task DataReaderMapper_OffsetDroppedByGetValue_KeepsTheOffset()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle);
        await using var _ = context;
        exec.EnqueueReaderResult(ZonedReader());
        await using var sc = context.CreateSqlContainer("SELECT id, tz, ts FROM tz_rows");
        await using var reader = await sc.ExecuteReaderAsync();

        AssertZoned(Assert.Single(await DataReaderMapper.LoadAsync<ZonedRow>(reader, new MapperOptions(Strict: true, ColumnsOnly: true))));
    }

    [Fact]
    public async Task ExecuteScalar_OffsetDroppedByGetValue_KeepsTheOffset()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle);
        await using var _ = context;
        exec.EnqueueReaderResult(new fakeDbDataReader(new[] { new Dictionary<string, object> { ["tz"] = Zoned } })
        {
            OffsetDroppedByGetValueColumns = new HashSet<string> { "tz" }
        });
        await using var sc = context.CreateSqlContainer("SELECT tz FROM tz_rows");

        var read = await sc.ExecuteScalarRequiredAsync<DateTimeOffset>();

        Assert.Equal(Zoned, read);
        Assert.Equal(Zoned.Offset, read.Offset);
    }

    private static void AssertZoned(ZonedRow row)
    {
        Assert.Equal(Zoned, row.Tz);
        Assert.Equal(Zoned.Offset, row.Tz.Offset);
        Assert.Equal(new DateTimeOffset(Zoned.DateTime, TimeSpan.Zero), row.Ts);
    }

    private static fakeDbDataReader ZonedReader() =>
        new(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["tz"] = Zoned, ["ts"] = DateTime.SpecifyKind(Zoned.DateTime, DateTimeKind.Unspecified) }
        })
        {
            OffsetDroppedByGetValueColumns = new HashSet<string> { "tz" }
        };

    private static (DatabaseContext Context, fakeDbConnection Exec) Context(SupportedDatabase product, string? version = null)
    {
        var factory = new fakeDbFactory(product);
        var init = new fakeDbConnection { EmulatedProduct = product };
        if (version != null)
        {
            init.SetServerVersion(version);
        }

        factory.Connections.Add(init);
        var exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        return (new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory), exec);
    }
}
