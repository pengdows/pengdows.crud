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

/// <summary>
/// DRY-028: fractional seconds a column can't hold are truncated, never rounded (docs/utc-and-time.md).
/// Found live with a value one tick before midnight, which these columns stored as the next day: SQL
/// Server DATETIME (1/300 s) and SMALLDATETIME (minutes), Sybase ASE SMALLDATETIME, Oracle TIMESTAMP
/// WITH LOCAL TIME ZONE and INTERVAL DAY TO SECOND(n). The gateways learn each temporal column's
/// declared type and scale with the TYPE-020 probe (SQL Server and Oracle report the scale in their
/// schema table) and floor the value to it before it is bound.
/// </summary>
public sealed class TemporalTruncationTests
{
    private static readonly TimeSpan LastTick = TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);
    private static readonly DateTime Edge = DateTime.SpecifyKind(new DateTime(2026, 12, 31).Add(LastTick), DateTimeKind.Utc);
    private static readonly DateTime Midnight = new(2026, 12, 31);

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

    private static object? Bound(fakeDbFactory factory, int ordinal) =>
        factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryCommands)
            .Single(c => c.CommandText.StartsWith("INSERT", StringComparison.Ordinal))
            .Parameters[ordinal].Value;

    private static long Ticks(object? value) => value switch
    {
        DateTime dt => dt.Ticks,
        DateTimeOffset dto => dto.Ticks,
        TimeSpan ts => ts.Ticks,
        TimeOnly t => t.Ticks,
        _ => throw new InvalidOperationException("not temporal: " + value?.GetType())
    };

    [Table("ss_t")]
    public sealed class SqlServerRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("sdt", DbType.DateTime)] public DateTime Sdt { get; set; }
        [Column("dt", DbType.DateTime)] public DateTime Dt { get; set; }
        [Column("dt3", DbType.DateTime2)] public DateTime Dt3 { get; set; }
        [Column("tm", DbType.Time)] public TimeSpan Tm { get; set; }
        [Column("dto", DbType.DateTimeOffset)] public DateTimeOffset Dto { get; set; }
        [Column("full", DbType.DateTime2)] public DateTime Full { get; set; }
    }

    [Fact]
    public async Task SqlServer_FloorsEachValueToItsColumn()
    {
        var (context, exec) = Context(SupportedDatabase.SqlServer, "Server=x;Database=d;EmulatedProduct=SqlServer", new[]
        {
            new fakeDbColumn("sdt", typeof(DateTime), "smalldatetime") { NumericScale = 0 },
            new fakeDbColumn("dt", typeof(DateTime), "datetime") { NumericScale = 3 },
            new fakeDbColumn("dt3", typeof(DateTime), "datetime2") { NumericScale = 3 },
            new fakeDbColumn("tm", typeof(TimeSpan), "time") { NumericScale = 2 },
            new fakeDbColumn("dto", typeof(DateTimeOffset), "datetimeoffset") { NumericScale = 4 },
            new fakeDbColumn("full", typeof(DateTime), "datetime2") { NumericScale = 7 }
        });
        await using var _ = context;
        var offsetEdge = new DateTimeOffset(new DateTime(2026, 12, 31).Add(LastTick), TimeSpan.FromHours(2));

        await new TableGateway<SqlServerRow, int>(context).CreateAsync(new SqlServerRow
        {
            Id = 1, Sdt = Edge, Dt = Edge, Dt3 = Edge, Tm = LastTick, Dto = offsetEdge, Full = Edge
        });

        Assert.Equal(Midnight.AddMinutes(1439).Ticks, Ticks(Bound(exec, 1)));
        // DATETIME counts 1/300 s: 299/300 of the last second, which SqlClient keeps as is.
        Assert.Equal(Midnight.Add(new TimeSpan(23, 59, 59)).AddTicks(9966666).Ticks, Ticks(Bound(exec, 2)));
        Assert.Equal(Midnight.Add(new TimeSpan(0, 23, 59, 59, 999)).Ticks, Ticks(Bound(exec, 3)));
        Assert.Equal(new TimeSpan(0, 23, 59, 59, 990).Ticks, Ticks(Bound(exec, 4)));
        Assert.Equal(offsetEdge.AddTicks(-999).Ticks, Ticks(Bound(exec, 5)));
        Assert.Equal(TimeSpan.FromHours(2), ((DateTimeOffset)Bound(exec, 5)!).Offset);
        Assert.Equal(Edge.Ticks, Ticks(Bound(exec, 6)));
    }

    [Table("ora_t")]
    public sealed class OracleRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("ltz", DbType.DateTime)] public DateTime Ltz { get; set; }
        [Column("d", DbType.DateTime)] public DateTime D { get; set; }
        [Column("ds", DbType.Time)] public TimeSpan Ds { get; set; }
        [Column("ivo", DbType.Object)] public IntervalDaySecond Ivo { get; set; }
    }

    [Fact]
    public async Task Oracle_FloorsEachValueToItsColumn()
    {
        var (context, exec) = Context(SupportedDatabase.Oracle, "Data Source=x;EmulatedProduct=Oracle", new[]
        {
            new fakeDbColumn("ltz", typeof(DateTime), "TimeStampLTZ") { NumericScale = 6 },
            new fakeDbColumn("d", typeof(DateTime), "Date"),
            new fakeDbColumn("ds", typeof(TimeSpan), "IntervalDS") { NumericPrecision = 2, NumericScale = 2 },
            new fakeDbColumn("ivo", typeof(TimeSpan), "IntervalDS") { NumericPrecision = 9, NumericScale = 2 }
        });
        await using var _ = context;

        await new TableGateway<OracleRow, int>(context).CreateAsync(new OracleRow
        {
            Id = 1, Ltz = Edge, D = Edge, Ds = LastTick, Ivo = new IntervalDaySecond(1, LastTick)
        });

        Assert.Equal(Edge.AddTicks(-9).Ticks, Ticks(Bound(exec, 1)));
        Assert.Equal(Midnight.Add(new TimeSpan(23, 59, 59)).Ticks, Ticks(Bound(exec, 2)));
        Assert.Equal(new TimeSpan(0, 23, 59, 59, 990).Ticks, Ticks(Bound(exec, 3)));
        Assert.Equal("+000000001 23:59:59.99", Bound(exec, 4));
    }

    [Table("ase_t")]
    public sealed class SybaseRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("sdt", DbType.DateTime)] public DateTime Sdt { get; set; }
        [Column("big", DbType.DateTime)] public DateTime Big { get; set; }
    }

    // AseClient reports BIGDATETIME as "datetime" too, so only SMALLDATETIME is recognized.
    [Fact]
    public async Task Sybase_FloorsSmallDateTimeToTheMinute_AndLeavesDateTimeAlone()
    {
        var (context, exec) = Context(SupportedDatabase.SybaseASE, "Server=x;Database=d;EmulatedProduct=SybaseASE", new[]
        {
            new fakeDbColumn("sdt", typeof(DateTime), "smalldatetime"),
            new fakeDbColumn("big", typeof(DateTime), "datetime")
        });
        await using var _ = context;

        await new TableGateway<SybaseRow, int>(context).CreateAsync(new SybaseRow { Id = 1, Sdt = Edge, Big = Edge });

        Assert.Equal(Midnight.AddMinutes(1439).Ticks, Ticks(Bound(exec, 1)));
        Assert.Equal(Edge.Ticks, Ticks(Bound(exec, 2)));
    }
}
