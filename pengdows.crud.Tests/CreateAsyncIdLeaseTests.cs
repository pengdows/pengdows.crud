using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DEC-014 / PERF-016: when INSERT ... RETURNING yields the id, CreateAsync allocates no more than
/// that statement run alone (no connection lease, no write-tracking array). The INSERT's connection
/// serves the fallback id query only when the id doesn't come back (GeneratedIdConnectionAffinityTests
/// pins that fallback).
/// </summary>
public class CreateAsyncIdLeaseTests
{
    [Table("lease_items")]
    private sealed class Item
    {
        [Id(false)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Fact]
    public async Task CreateAsync_IdReturnedInline_AllocatesNoMoreThanTheReturningStatementAlone()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext("Data Source=lease;EmulatedProduct=PostgreSql", factory);
        // Each operation stages the connection it will get, answering the RETURNING row; both paths
        // pay the same staging cost.
        void Stage() => factory.EnqueueReaderResult(new[] { new System.Collections.Generic.Dictionary<string, object> { ["id"] = 7 } });

        Assert.Equal(GeneratedKeyPlan.Returning, context.Dialect.GetGeneratedKeyPlan());
        var gateway = new TableGateway<Item, int>(context);

        var lastId = 0;
        async Task Create()
        {
            Stage();
            var item = new Item { Name = "n" };
            await gateway.CreateAsync(item);
            lastId = item.Id; // asserted outside the measured loop: an Assert allocates
        }

        async Task Statement()
        {
            Stage();
            var item = new Item { Name = "n" };
            await using var sc = gateway.BuildCreateWithReturning(item, true);
            item.Id = Convert.ToInt32(await sc.ExecuteScalarOrNullAsync<object>(ExecutionType.Write));
        }

        var create = await AllocationMeasurement.LowestAsync(() => AllocationMeasurement.PerRunAsync(Create));
        var statement = await AllocationMeasurement.LowestAsync(() => AllocationMeasurement.PerRunAsync(Statement));

        Assert.Equal(7, lastId);
        Assert.True(statement != long.MaxValue, "every pass resumed on another thread");
        Assert.True(create <= statement, $"CreateAsync {create} B, the statement alone {statement} B");
    }

    private const string ReturningInsert = "INSERT INTO \"lease_items\" (\"name\") VALUES (@i0) RETURNING \"id\"";

    private static (DatabaseContext Context, fakeDbFactory Factory, fakeDbConnection Exec) CreatePostgres()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=lease;EmulatedProduct=PostgreSql", factory);
        // The first write learns the table's declared column types (TYPE-020) on its own connection.
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.PostgreSql };
        exec.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 7 } });
        factory.Connections.Add(exec);
        return (context, factory, exec);
    }

    [Fact]
    public async Task CreateAsync_IdReturnedInline_ReleasesTheConnectionOnce()
    {
        var (context, factory, exec) = CreatePostgres();
        await using var _ = context;
        var item = new Item { Name = "n" };

        Assert.True(await new TableGateway<Item, int>(context).CreateAsync(item));

        Assert.Equal(7, item.Id);
        Assert.Equal(1, exec.DisposeCount);
        Assert.DoesNotContain(factory.CreatedConnections, c => c.ExecutedReaderTexts.Any(t => t.Contains("lastval")));
    }

    [Fact]
    public async Task CreateAsync_ReturningInsertFails_ReleasesTheConnectionOnce()
    {
        var (context, factory, exec) = CreatePostgres();
        await using var _ = context;
        factory.SetCommandFailure(ReturningInsert, new InvalidOperationException("insert failed"));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await new TableGateway<Item, int>(context).CreateAsync(new Item { Name = "n" }));

        Assert.Equal(1, exec.DisposeCount);
    }
}
