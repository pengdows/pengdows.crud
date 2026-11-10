using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

// FlatFile reports an INTERVAL column as TimeSpan but returns its ISO 8601 text (confirmed live by the fidelity matrix,
// 2026-11-07): "P1DT2H3M4S" is not a TimeSpan, so the read must take the text the way the interval value object does.
public sealed class FlatFileIntervalReadTests
{
    [Table("ff_interval")]
    private sealed class DayTimeRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("d", DbType.Object)]
        public IntervalDaySecond Duration { get; set; }
    }

    [Fact]
    public async Task DayTimeIntervalText_ReadsWhenTheProviderReportsTimeSpan()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile };
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["d"] = "P1DT2H3M4S" }
        })
        {
            ReportedFieldTypes = new Dictionary<string, Type> { ["d"] = typeof(TimeSpan) }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=FlatFile",
            DbMode = DbMode.Standard
        }, factory);

        var rows = await new TableGateway<DayTimeRow, int>(context).RetrieveAsync(new[] { 1 });

        Assert.Equal(IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4)), rows[0].Duration);
    }

    [Table("ff_year_month")]
    private sealed class YearMonthRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("ym", DbType.Object)]
        public IntervalYearMonth Span { get; set; }
    }

    [Fact]
    public async Task YearMonthIntervalText_ReadsWhenTheProviderReportsTimeSpan()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile };
        exec.EnqueueReaderResult(new fakeDbDataReader(new[]
        {
            new Dictionary<string, object> { ["id"] = 1, ["ym"] = "2 years 3 months" }
        })
        {
            ReportedFieldTypes = new Dictionary<string, Type> { ["ym"] = typeof(TimeSpan) }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=FlatFile",
            DbMode = DbMode.Standard
        }, factory);

        var rows = await new TableGateway<YearMonthRow, int>(context).RetrieveAsync(new[] { 1 });

        Assert.Equal(new IntervalYearMonth(2, 3), rows[0].Span);
    }

    // pengdows.flatfile parses an INTERVAL value as a TimeSpan (the fidelity matrix, 2026-11-07: "P1DT2H3M4S" is refused on
    // insert). A day-time interval is therefore bound as its TimeSpan, which the read takes back as a TimeSpan.
    [Fact]
    public async Task DayTimeIntervalWrite_BindsAsTimeSpan()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.FlatFile };
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;EmulatedProduct=FlatFile",
            DbMode = DbMode.Standard
        }, factory);

        await using var insertSql = new TableGateway<DayTimeRow, int>(context).BuildCreate(new DayTimeRow
        {
            Id = 1,
            Duration = IntervalDaySecond.FromTimeSpan(new TimeSpan(1, 2, 3, 4))
        });
        await insertSql.ExecuteNonQueryAsync();

        var insert = exec.ExecutedNonQueryCommands.First(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Contains(insert.Parameters, p => Equals(p.Value, new TimeSpan(1, 2, 3, 4)));
    }
}
