using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-029: Oracle TIMESTAMP WITH TIME ZONE and Snowflake TIMESTAMP_TZ keep a DateTimeOffset's offset,
/// but a parameter can't tell which column it is going to: sent with its offset, the value stores its
/// local wall time in a column with no offset (Oracle TIMESTAMP/DATE, Snowflake TIMESTAMP_NTZ), and a
/// Snowflake TIMESTAMP_TZ-typed bind is refused into LTZ and NTZ columns (all found live). So a parameter
/// is sent as its UTC instant, as before, and the gateways send the offset only to a column whose
/// declared type (learned by the TYPE-020 probe) keeps one.
/// </summary>
public sealed class OffsetColumnBindingTests
{
    private static readonly DateTimeOffset Zoned =
        new DateTimeOffset(2026, 10, 1, 13, 45, 30, TimeSpan.FromHours(-5)).AddTicks(1234567);

    [Table("tz_t")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("tz", DbType.DateTimeOffset)] public DateTimeOffset Tz { get; set; }
        [Column("ltz", DbType.DateTimeOffset)] public DateTimeOffset Ltz { get; set; }
        [Column("plain", DbType.DateTimeOffset)] public DateTimeOffset Plain { get; set; }
    }

    private static (DatabaseContext Context, fakeDbFactory Exec) Context(SupportedDatabase product,
        string connectionString, IReadOnlyList<fakeDbColumn> declared)
    {
        var factory = new fakeDbFactory(product);
        var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard
        }, factory);
        var probe = new fakeDbConnection { EmulatedProduct = product };
        probe.EnqueueReaderResult(new fakeDbDataReader(Array.Empty<Dictionary<string, object>>()) { Columns = declared });
        factory.Connections.Add(probe);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        return (context, factory);
    }

    private static object? Bound(fakeDbFactory factory, string statementStart, int ordinal) =>
        factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands)
            .Single(c => c.CommandText.StartsWith(statementStart, StringComparison.Ordinal))
            .Parameters[ordinal].Value;

    private static void AssertUtcInstant(DateTimeOffset expected, object? bound)
    {
        switch (bound)
        {
            case DateTimeOffset dto:
                Assert.Equal(expected, dto);
                Assert.Equal(TimeSpan.Zero, dto.Offset);
                break;
            default:
                Assert.Equal(expected.UtcDateTime, Assert.IsType<DateTime>(bound));
                break;
        }
    }

    private static IReadOnlyList<fakeDbColumn> Declared(string tz, string ltz, string plain) => new[]
    {
        new fakeDbColumn("tz", typeof(DateTimeOffset), tz),
        new fakeDbColumn("ltz", typeof(DateTimeOffset), ltz),
        new fakeDbColumn("plain", typeof(DateTime), plain)
    };

    [Fact]
    public async Task Oracle_Create_SendsTheOffsetOnlyToTimestampWithTimeZone()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle, "Data Source=x;EmulatedProduct=Oracle",
            Declared("TimeStampTZ", "TimeStampLTZ", "TimeStamp"));
        await using var _ = context;

        await new TableGateway<Row, int>(context).CreateAsync(new Row { Id = 1, Tz = Zoned, Ltz = Zoned, Plain = Zoned });

        var tz = Assert.IsType<DateTimeOffset>(Bound(exec, "INSERT", 1));
        Assert.Equal(Zoned, tz);
        Assert.Equal(Zoned.Offset, tz.Offset);
        AssertUtcInstant(Zoned, Bound(exec, "INSERT", 2));
        AssertUtcInstant(Zoned, Bound(exec, "INSERT", 3));
    }

    [Fact]
    public async Task Oracle_Update_SendsTheOffsetToTimestampWithTimeZone()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle, "Data Source=x;EmulatedProduct=Oracle",
            Declared("TimeStampTZ", "TimeStampLTZ", "TimeStamp"));
        await using var _ = context;

        await new TableGateway<Row, int>(context).UpdateAsync(new Row { Id = 1, Tz = Zoned, Ltz = Zoned, Plain = Zoned });

        var bound = factoryValues(exec, "UPDATE");
        Assert.Contains(bound, v => v is DateTimeOffset d && d.Offset == Zoned.Offset);
        Assert.Equal(2, bound.Count(v => v is DateTimeOffset d && d.Offset == TimeSpan.Zero));

        static List<object?> factoryValues(fakeDbFactory f, string start) =>
            f.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands)
                .Single(c => c.CommandText.StartsWith(start, StringComparison.Ordinal))
                .Parameters.Select(p => p.Value).ToList();
    }

    [Fact]
    public async Task Snowflake_Create_SendsTheOffsetOnlyToTimestampTz()
    {
        var (context, exec) = Context(SupportedDatabase.Snowflake, "account=x;user=y;EmulatedProduct=Snowflake",
            Declared("TIMESTAMP_TZ", "TIMESTAMP_LTZ", "TIMESTAMP_NTZ"));
        await using var _ = context;

        await new TableGateway<Row, int>(context).CreateAsync(new Row { Id = 1, Tz = Zoned, Ltz = Zoned, Plain = Zoned });

        // ISO 8601 text with the offset: Snowflake.Data's own DateTimeOffset bind is typed TIMESTAMP_TZ.
        Assert.Equal("2026-10-01T13:45:30.1234567-05:00", Bound(exec, "INSERT", 1));
        AssertUtcInstant(Zoned, Bound(exec, "INSERT", 2));
        AssertUtcInstant(Zoned, Bound(exec, "INSERT", 3));
    }

    // Oracle batches bind one array per column (ODP.NET array binding). Every row is marked for its
    // column, not just the first (the later rows also missed DRY-028's truncation to the column's scale).
    private sealed class ArrayBindingCommand : fakeDbCommand
    {
        public ArrayBindingCommand(System.Data.Common.DbConnection connection) : base(connection)
        {
        }

        public int ArrayBindCount { get; set; } = -1;
    }

    [Fact]
    public async Task Oracle_ArrayBoundBatch_MarksEveryRow()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle, "Data Source=x;EmulatedProduct=Oracle", new[]
        {
            new fakeDbColumn("tz", typeof(DateTimeOffset), "TimeStampTZ"),
            new fakeDbColumn("ltz", typeof(DateTimeOffset), "TimeStampLTZ"),
            new fakeDbColumn("plain", typeof(DateTime), "TimeStamp") { NumericScale = 6 }
        });
        exec.CommandFactory = c => new ArrayBindingCommand(c);
        await using var _ = context;
        var later = Zoned.AddHours(1);

        await new TableGateway<Row, int>(context).BatchCreateAsync(new[]
        {
            new Row { Id = 1, Tz = Zoned, Ltz = Zoned, Plain = Zoned },
            new Row { Id = 2, Tz = later, Ltz = later, Plain = later }
        });

        var tz = Assert.IsType<object[]>(Bound(exec, "INSERT", 1));
        Assert.Equal(later.Offset, Assert.IsType<DateTimeOffset>(tz[1]).Offset);
        var plain = Assert.IsType<object[]>(Bound(exec, "INSERT", 3));
        AssertUtcInstant(Zoned.AddTicks(-7), plain[0]);
        AssertUtcInstant(later.AddTicks(-7), plain[1]);
    }
}
