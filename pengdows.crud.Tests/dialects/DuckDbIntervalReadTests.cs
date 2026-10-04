using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// TYPE-022 (DuckDB part), confirmed live with DuckDB.NET 1.5.6: an INTERVAL reports TimeSpan, but
/// GetValue/GetFieldValue&lt;TimeSpan&gt; throw ArgumentOutOfRangeException for any negative value
/// (its microseconds are a signed value the driver exposes as ulong) and for months &gt;= 1, and silently
/// drop negative months. GetProviderSpecificValue returns the stored months/days/micros, which the
/// DuckDB dialect converts exactly; a month has no fixed length, so months fail the read.
/// </summary>
public sealed class DuckDbIntervalReadTests
{
    private const long MicrosPerDay = 86_400_000_000L;

    [Table("spans")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("v", DbType.Object)] public TimeSpan? V { get; set; }
    }

    public sealed class Mapped
    {
        public TimeSpan? V { get; set; }
    }

    private static fakeDbInterval Interval(int months, int days, long micros) =>
        new(months, days, unchecked((ulong)micros));

    [Fact]
    public void FakeDb_EmulatesDuckDbNetOnIntervals()
    {
        var negative = Interval(0, -1, -7_384_567_891);
        var reader = new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["neg"] = negative, ["pos"] = Interval(0, 1, -3_600_000_000), ["m"] = Interval(-1, 2, 0), ["pm"] = Interval(1, 0, 0) }
        });
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(0)); // the driver's null check doesn't convert
        Assert.Equal(typeof(TimeSpan), reader.GetFieldType(0));
        Assert.Equal("Interval", reader.GetDataTypeName(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetValue(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetFieldValue<TimeSpan>(0));
        Assert.Equal(negative, Assert.IsType<fakeDbInterval>(reader.GetProviderSpecificValue(0)));
        Assert.Equal(TimeSpan.FromHours(23), reader.GetValue(1)); // unsigned wraparound still lands in range
        Assert.Equal(TimeSpan.FromDays(2), reader.GetValue(2)); // negative months silently dropped
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetValue(3));
    }

    [Theory]
    [InlineData(0, -1, -7_384_567_891L)] // -1 day -02:03:04.567891
    [InlineData(0, 0, -5_400_000_000L)] // -90 minutes
    [InlineData(0, -1, 3_600_000_000L)] // -1 day +01:00:00
    [InlineData(0, 0, -3L)] // -3 microseconds
    [InlineData(0, 1, 7_384_567_891L)] // positive, as the driver reads it too
    public async Task Gateway_ReadsEveryIntervalExactly(int months, int days, long micros)
    {
        var row = await LoadAsync(Interval(months, days, micros));

        Assert.Equal(TimeSpan.FromTicks((days * MicrosPerDay + micros) * 10), row!.V);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Gateway_IntervalWithMonths_FailsInsteadOfDroppingThem(int months)
    {
        var ex = await Assert.ThrowsAsync<DataMappingException>(() => LoadAsync(Interval(months, 2, 0)));

        Assert.Contains("month", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gateway_NullInterval_ReadsNull()
    {
        var row = await LoadAsync(DBNull.Value);

        Assert.Null(row!.V);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DataReaderMapper_ReadsNegativeIntervals(bool strict)
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["V"] = Interval(0, 0, -5_400_000_000) } });
        await using var sc = context.CreateSqlContainer("SELECT v AS V FROM spans");
        await using var reader = await sc.ExecuteReaderAsync();

        var rows = await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: strict));

        Assert.Equal(TimeSpan.FromMinutes(-90), Assert.Single(rows).V);
    }

    private static async Task<Row?> LoadAsync(object value)
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        factory.EnqueueReaderResult(new[] { new Dictionary<string, object> { ["id"] = 1, ["v"] = value } });
        var gateway = new TableGateway<Row, int>(context);
        await using var sc = gateway.BuildBaseRetrieve("a");
        return await gateway.LoadSingleAsync(sc);
    }
}
