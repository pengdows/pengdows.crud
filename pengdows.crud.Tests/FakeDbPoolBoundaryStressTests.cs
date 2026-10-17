using System;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// F4: the 200-worker, pool-of-40 boundary scenario, run with fakeDb execute gates instead of a real database. Forty commands
/// are held in flight; every other caller must be refused with <see cref="PoolSaturatedException"/>; the pool must never
/// run more than forty at once; and once the gates open, the pool must serve again with nothing leaked.
/// What it cannot show (a real server's own connection limit, process kill, network loss) stays on the integration suite.
/// </summary>
public sealed class FakeDbPoolBoundaryStressTests
{
    private const int Workers = 200;
    private const int Slots = 40;

    [Fact]
    public async Task TwoHundredWorkersOverAPoolOfForty_HoldForty_RefuseTheRest_AndRecover()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        // The first connection a context opens runs its start-up probes, so it is not gated.
        factory.Connections.Add(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            ConnectionString = "Host=x;Database=y;EmulatedProduct=PostgreSql"
        });
        var gates = Enumerable.Range(0, Slots).Select(_ =>
        {
            var connection = new fakeDbConnection
            {
                EmulatedProduct = SupportedDatabase.PostgreSql,
                ConnectionString = "Host=x;Database=y;EmulatedProduct=PostgreSql"
            };
            var gate = connection.SetExecuteGate();
            factory.Connections.Add(connection);
            return (Connection: connection, Gate: gate);
        }).ToList();

        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Host=x;Database=y;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            MaxConcurrentWrites = Slots,
            MaxQueuedWrites = 8,
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(300)
        }, factory);

        var work = Enumerable.Range(0, Workers).Select(i => Task.Run(async () =>
        {
            await using var sc = context.CreateSqlContainer("UPDATE t SET a = 1");
            return await sc.ExecuteNonQueryAsync();
        })).ToList();

        using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await Task.WhenAll(gates.Select(g => g.Connection.ExecuteGateEntered)).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"entered={gates.Count(g => g.Connection.ExecuteGateEntered.IsCompleted)} faulted={work.Count(t => t.IsFaulted)} done={work.Count(t => t.IsCompletedSuccessfully)} created={factory.CreatedConnections.Count} open={context.NumberOfOpenConnections} first={work.FirstOrDefault(t => t.IsFaulted)?.Exception?.InnerException}");
        }

        while (work.Count(t => t.IsFaulted) < Workers - Slots)
        {
            await Task.Delay(20, deadline.Token);
        }

        Assert.All(work.Where(t => t.IsFaulted), t => Assert.IsType<PoolSaturatedException>(t.Exception!.InnerException));
        Assert.Equal(Slots, work.Count(t => !t.IsCompleted));
        Assert.True(context.PeakOpenConnections <= Slots, $"peak open connections {context.PeakOpenConnections} exceeded {Slots}");

        foreach (var (_, gate) in gates)
        {
            gate.SetResult(true);
        }

        await Task.WhenAll(work.Where(t => !t.IsFaulted));
        Assert.Equal(Workers - Slots, work.Count(t => t.IsFaulted));
        Assert.Equal(0, context.NumberOfOpenConnections);

        await using var again = context.CreateSqlContainer("UPDATE t SET a = 2");
        Assert.Equal(1, await again.ExecuteNonQueryAsync());
    }
}
