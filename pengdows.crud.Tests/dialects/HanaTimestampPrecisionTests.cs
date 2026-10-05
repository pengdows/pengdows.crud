using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-022 (SAP HANA part), confirmed live (HANA Express, Sap.Data.Hana.Net 2.29): HANA's TIMESTAMP
/// holds 7 fractional digits (100 ns, a .NET tick) but the driver truncates a DateTime to microseconds
/// on write and on read. Seven-digit text converts into TIMESTAMP exactly, and into SECONDDATE and DATE
/// as the typed value did, in writes and WHERE alike; TO_VARCHAR(col, '... FF7') reads it exactly.
/// </summary>
public sealed class HanaTimestampPrecisionTests
{
    [Table("HANA_ROWS")]
    public sealed class Row
    {
        [Id] [Column("ID", DbType.Int32)] public int Id { get; set; }
        [Column("T", DbType.DateTime)] public DateTime T { get; set; }
        [Column("T2", DbType.DateTime2)] public DateTime T2 { get; set; }
        [Column("D", DbType.Date)] public DateTime D { get; set; }
    }

    private static SqlDialect Dialect() =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.SapHana,
            new fakeDbFactory(SupportedDatabase.SapHana), NullLogger.Instance);

    private static readonly DateTime Sample = new DateTime(2026, 10, 1, 13, 45, 30).AddTicks(1234567);

    [Theory]
    [InlineData(DbType.DateTime)]
    [InlineData(DbType.DateTime2)]
    public void DateTimeParameter_IsSevenDigitText(DbType type)
    {
        var p = Dialect().CreateDbParameter("p", type, Sample);

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal("2026-10-01 13:45:30.1234567", p.Value);
    }

    [Fact]
    public void NullDateTimeParameter_IsATextNull()
    {
        var p = Dialect().CreateDbParameter<DateTime?>("p", DbType.DateTime, null);

        Assert.Equal(DbType.String, p.DbType);
        Assert.Equal(DBNull.Value, p.Value);
    }

    [Fact]
    public void DateParameter_IsUnchanged()
    {
        var p = Dialect().CreateDbParameter("p", DbType.Date, Sample.Date);

        Assert.Equal(DbType.Date, p.DbType);
    }

    [Fact]
    public async Task GatewayRead_SelectsTimestampsAsSevenDigitText()
    {
        await using var context = new DatabaseContext("Data Source=hana;EmulatedProduct=SapHana", new fakeDbFactory(SupportedDatabase.SapHana));
        var gateway = new TableGateway<Row, int>(context);

        var sql = gateway.BuildBaseRetrieve("a").Query.ToString();

        Assert.Contains("TO_VARCHAR(\"a\".\"T\", 'YYYY-MM-DD HH24:MI:SS.FF7') AS \"T\"", sql);
        Assert.Contains("TO_VARCHAR(\"a\".\"T2\", 'YYYY-MM-DD HH24:MI:SS.FF7') AS \"T2\"", sql);
        Assert.DoesNotContain("TO_VARCHAR(\"a\".\"D\"", sql);
    }

    [Fact]
    public async Task GatewayRead_ParsesTheTextExactly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SapHana);
        await using var context = new DatabaseContext("Data Source=hana;EmulatedProduct=SapHana", factory);
        factory.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object> { ["ID"] = 1, ["T"] = "2026-10-01 13:45:30.1234567", ["T2"] = "2026-10-01 13:45:30.1234567", ["D"] = new DateTime(2026, 10, 1) }
        });
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");

        var row = await gateway.LoadSingleAsync(sc);

        Assert.Equal(Sample, row!.T);
        Assert.Equal(Sample, row.T2);
    }
}
