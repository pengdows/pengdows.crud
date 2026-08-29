using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using Xunit;

namespace pengdows.crud.Tests;

// TEST-012: reader ownership under every exit. TrackedReaderTests (wrappers/TrackedReaderTest.cs)
// already thoroughly proves individual-layer resilience with mocks — reader/command/connection/
// locker dispose failures each still release the OTHER layers, EOF auto-disposes, Close is
// idempotent with Dispose, etc. What that suite cannot prove (it doesn't use a real
// DatabaseContext/PoolGovernor) is that a REAL reader-permit governor actually returns to
// baseline after each of these exit shapes, not just that the mocked sub-objects' Dispose() was
// called. This fills that integration-level gap for the two most common exit shapes: draining to
// EOF, and closing explicitly before EOF.
public class ReaderPermitBaselineIntegrationTests
{
    private static DatabaseContext CreateContext(int maxConcurrentReads = 1)
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=reader-baseline;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            MaxConcurrentReads = maxConcurrentReads
        };
        return new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    private static void AssertReaderBaseline(DatabaseContext context)
    {
        var snapshot = context.GetPoolStatisticsSnapshot(PoolLabel.Reader);
        Assert.Equal(0, snapshot.InUse);
    }

    [Fact]
    public async Task ReaderPermit_ReturnsToBaseline_AfterDrainingToEof()
    {
        await using var context = CreateContext();
        AssertReaderBaseline(context);

        await using (var container = context.CreateSqlContainer("SELECT 1"))
        await using (var reader = await container.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
            }
        }

        AssertReaderBaseline(context);
    }

    [Fact]
    public async Task ReaderPermit_ReturnsToBaseline_AfterExplicitCloseBeforeEof()
    {
        await using var context = CreateContext();
        AssertReaderBaseline(context);

        await using (var container = context.CreateSqlContainer("SELECT 1"))
        await using (var reader = await container.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            // Explicit close BEFORE any subsequent ReadAsync() would have naturally returned
            // false — proves the manual-close path releases the permit just as reliably as
            // natural EOF exhaustion does.
            reader.Close();
        }

        AssertReaderBaseline(context);
    }

    [Fact]
    public async Task ReaderPermit_ReturnsToBaseline_AfterCancellationDuringRead()
    {
        await using var context = CreateContext();
        AssertReaderBaseline(context);

        await using (var container = context.CreateSqlContainer("SELECT 1"))
        {
            using var cts = new CancellationTokenSource();
            await using var reader = await container.ExecuteReaderAsync(ExecutionType.Read,
                System.Data.CommandType.Text, cts.Token);
            cts.Cancel();

            // A cancellation observed mid-enumeration must not leak the reader's permit even
            // though the caller never reaches a clean EOF or explicit Close() call — the
            // `await using` here disposes it via the exception path.
            await Assert.ThrowsAnyAsync<System.OperationCanceledException>(
                () => reader.ReadAsync(cts.Token).AsTask());
        }

        AssertReaderBaseline(context);
    }
}
