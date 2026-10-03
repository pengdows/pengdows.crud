using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The gateway caches the plan of its last recordset shape in a hot slot. The slot held the plan
/// and its shape in two fields written one after the other, so a load racing a load of another
/// shape could pair one shape with the other's plan. Gateways are singletons; with the same column
/// types in a different order the wrong plan swaps values silently.
/// </summary>
public sealed class RecordsetPlanConcurrencyTests
{
    [Table("scores")]
    public sealed class Score
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
        [Column("score", DbType.Int32)] public int Points { get; set; }
    }

    private static readonly Dictionary<string, object> IdFirst =
        new() { ["id"] = 1, ["name"] = "n", ["score"] = 1000 };

    // Same columns, same types by position (int, string, int), different order.
    private static readonly Dictionary<string, object> ScoreFirst =
        new() { ["score"] = 1000, ["name"] = "n", ["id"] = 1 };

    private static Score Map(TableGateway<Score, int> gateway, Dictionary<string, object> row)
    {
        var reader = new TrackedReader(new fakeDbDataReader(new[] { row }), new Mock<ITrackedConnection>().Object,
            Mock.Of<IAsyncDisposable>(), false);
        reader.Read();
        return gateway.MapReaderToObject(reader);
    }

    [Fact]
    public async Task MapReaderToObject_TwoShapesConcurrently_NeverUsesTheOtherShapesPlan()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var gateway = new TableGateway<Score, int>(context);
        var wrong = 0;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var workers = Enumerable.Range(0, Math.Max(4, Environment.ProcessorCount)).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 50_000 && Volatile.Read(ref wrong) == 0 && !stop.IsCancellationRequested; i++)
            {
                var entity = Map(gateway, ((worker + i) % 2) == 0 ? IdFirst : ScoreFirst);
                if (entity.Id != 1 || entity.Points != 1000)
                {
                    Interlocked.Increment(ref wrong);
                }
            }
        })).ToArray();
        await Task.WhenAll(workers);

        Assert.Equal(0, wrong);
    }

    [Fact]
    public void MapReaderToObject_AlternatingShapes_MapsEachByItsOwnColumns()
    {
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));
        var gateway = new TableGateway<Score, int>(context);

        for (var i = 0; i < 4; i++)
        {
            var entity = Map(gateway, (i % 2) == 0 ? IdFirst : ScoreFirst);
            Assert.Equal(1, entity.Id);
            Assert.Equal(1000, entity.Points);
        }
    }
}
