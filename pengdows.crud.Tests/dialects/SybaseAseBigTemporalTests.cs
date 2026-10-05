using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-022 (Sybase ASE part), confirmed live (ASE 16.0, AdoNetCore.AseClient 0.19.2): the driver
/// truncates a DateTime to milliseconds on write (BIGDATETIME holds microseconds), decodes BIGDATETIME
/// a few microseconds off on read (.123456 as .1229952) and can't read BIGTIME at all ("Unsupported
/// data type 188"). Text avoids all three: microsecond text converts implicitly into BIGDATETIME and
/// DATETIME (writes and WHERE alike, the same as a typed value for DATETIME); BIGTIME takes it only
/// through CONVERT(BIGTIME, ...), which a TIME column accepts too; and CONVERT(VARCHAR, col, 140)
/// (BIGDATETIME) / 137 (BIGTIME) render them exactly for gateway reads.
/// </summary>
public sealed class SybaseAseBigTemporalTests
{
    [Table("ase_rows")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("d", DbType.DateTime2)] public DateTime D { get; set; }
        [Column("t", DbType.Time)] public TimeSpan T { get; set; }
        [Column("o", DbType.Time)] public TimeOnly O { get; set; }
        [Column("plain", DbType.DateTime)] public DateTime Plain { get; set; }
    }

    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.SybaseASE,
            new fakeDbFactory(SupportedDatabase.SybaseASE), NullLogger.Instance);

    private static readonly DateTime Sample = new DateTime(2026, 10, 1, 13, 45, 30).AddTicks(1234567);

    [Fact]
    public void DateTime2Parameter_IsMicrosecondText_Truncated()
    {
        var p = Dialect().CreateDbParameter("p", DbType.DateTime2, Sample);

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("2026-10-01 13:45:30.123456", p.Value);
    }

    [Fact]
    public void DateTimeOffsetParameter_IsTheUtcInstantAsMicrosecondText()
    {
        var p = Dialect().CreateDbParameter("p", DbType.DateTimeOffset, new DateTimeOffset(Sample, TimeSpan.FromHours(-5)));

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("2026-10-01 18:45:30.123456", p.Value);
    }

    [Fact]
    public void NullDateTime2Parameter_IsATextNull()
    {
        var p = Dialect().CreateDbParameter<DateTime?>("p", DbType.DateTime2, null);

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(DBNull.Value, p.Value);
    }

    [Fact]
    public void DateOnlyParameter_IsTextLikeTheEquivalentDateTime()
    {
        var p = Dialect().CreateDbParameter("p", DbType.DateTime2, new DateOnly(2026, 10, 1));

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("2026-10-01 00:00:00.000000", p.Value);
    }

    [Fact]
    public void PlainDateTimeParameter_IsUnchanged()
    {
        var p = Dialect().CreateDbParameter("p", DbType.DateTime, Sample);

        Assert.Equal(DbType.DateTime, p.DbType);
    }

    private static async Task<(DatabaseContext Context, fakeDbFactory Factory)> ContextAsync()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SybaseASE);
        var context = new DatabaseContext("Data Source=ase;EmulatedProduct=SybaseASE", factory);
        await Task.CompletedTask;
        return (context, factory);
    }

    [Fact]
    public async Task GatewayWrite_TimeColumns_GoThroughConvertBigTime_AsText()
    {
        var (context, factory) = await ContextAsync();
        await using var _ = context;
        var gateway = new TableGateway<Row, int>(context);

        await gateway.CreateAsync(new Row { Id = 1, D = Sample, T = new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234567)), O = new TimeOnly(1, 2, 3).Add(TimeSpan.FromTicks(4567890)), Plain = Sample });

        var command = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands).Single(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Contains("CONVERT(BIGTIME, @i2)", command.CommandText);
        Assert.Contains("CONVERT(BIGTIME, @i3)", command.CommandText);
        Assert.DoesNotContain("CONVERT(BIGTIME, @i1)", command.CommandText);
        var values = command.Parameters.ToDictionary(p => p.Name.TrimStart('@'), p => p.Value);
        Assert.Equal("2026-10-01 13:45:30.123456", values["i1"]);
        Assert.Equal("13:45:30.123456", values["i2"]);
        Assert.Equal("01:02:03.456789", values["i3"]);
    }

    [Fact]
    public async Task GatewayRead_SelectsBigTemporalColumnsAsExactText()
    {
        var (context, _) = await ContextAsync();
        await using var _c = context;
        var gateway = new TableGateway<Row, int>(context);

        var sql = gateway.BuildBaseRetrieve("a").Query.ToString();

        Assert.Contains("CONVERT(VARCHAR(26), \"a\".\"d\", 140) AS \"d\"", sql);
        Assert.Contains("CONVERT(VARCHAR(15), \"a\".\"t\", 137) AS \"t\"", sql);
        Assert.Contains("CONVERT(VARCHAR(15), \"a\".\"o\", 137) AS \"o\"", sql);
        Assert.DoesNotContain("CONVERT(VARCHAR(26), \"a\".\"plain\"", sql);
    }

    [Fact]
    public async Task GatewayRead_ParsesTheTextExactly()
    {
        var (context, factory) = await ContextAsync();
        await using var _ = context;
        factory.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["d"] = "2026-10-01 13:45:30.123456", ["t"] = "13:45:30.123456", ["o"] = "01:02:03.456789", ["plain"] = new DateTime(2026, 10, 1) }
        });
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(new DateTime(2026, 10, 1, 13, 45, 30).AddTicks(1234560), row!.D);
        Assert.Equal(new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234560)), row.T);
        Assert.Equal(new TimeOnly(1, 2, 3).Add(TimeSpan.FromTicks(4567890)), row.O);
    }
}
