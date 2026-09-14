using System.Data;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Live integration coverage for DeadlockException — the sibling
/// <see cref="TransientErrorTests"/>'s own file-level doc comment promised ("with
/// CommandTimeoutException/DeadlockException/SerializationConflictException following in the same
/// file") but never actually implemented. SerializationConflictException got its own dedicated
/// file (<see cref="SerializationConflictTests"/>, DuckDB+Firebird); this is the equivalent for
/// DeadlockException, targeting MySQL.
/// </summary>
/// <remarks>
/// MySQL/InnoDB is the right provider for this: <see cref="SerializationConflictTests"/>'s own
/// remarks already establish that MySql/MariaDb/TiDb deliberately have NO distinct
/// serialization-conflict classification, because InnoDB's row-locking implementation of
/// SERIALIZABLE surfaces a genuine conflict as either a lock-wait timeout (error 1205, already
/// CommandTimeoutException) or a real deadlock (error 1213, classified as DeadlockException in
/// <c>MySqlDialect.TryClassifyProviderException</c> and reachable through
/// <c>MySqlExceptionTranslator</c>). This file forces that second path live, through the full
/// pengdows.crud stack (TransactionContext -> SqlContainer -> real MySqlException ->
/// IDbExceptionTranslator), not just a raw driver-exception-to-translator call.
///
/// Classic circular-wait recipe: two transactions each lock one row via UPDATE, in opposite row
/// order, using a pair of <see cref="TaskCompletionSource"/> barriers so both sides are guaranteed
/// to be holding their first row's lock before either attempts the second — otherwise one side
/// could simply finish before the other starts and no deadlock would ever occur. MySQL's own
/// deadlock detector then picks one side as the victim (real timing, not something this test
/// controls), rolling that transaction back with error 1213 while the other proceeds and commits.
/// The whole scenario is retried a bounded number of times: even with barriers, exactly which side
/// InnoDB picks as the victim (and how quickly its detector fires) is a live timing detail, not
/// something deterministic from the client side.
/// </remarks>
[Collection("IntegrationTests")]
public class DeadlockConflictTests : DatabaseTestBase
{
    private const string TableName = "deadlock_test";

    public DeadlockConflictTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(p => p == SupportedDatabase.MySql);
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var idColumn = context.WrapObjectName("id");
        var valColumn = context.WrapObjectName("val");

        await using (var create = context.CreateSqlContainer(
                         $"CREATE TABLE {table} ({idColumn} INT PRIMARY KEY, {valColumn} INT) ENGINE=InnoDB"))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var seed = context.CreateSqlContainer(
            $"INSERT INTO {table} ({idColumn}, {valColumn}) VALUES (1, 100), (2, 200)");
        await seed.ExecuteNonQueryAsync();
    }

    protected override Task CleanupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return DropTableIfExistsAsync(context, TableName);
    }

    [SkippableFact]
    public async Task ConcurrentOppositeOrderUpdates_OneSideThrowsDeadlockException()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.MySql, async context =>
        {
            // MySQL's deadlock detector is a live timing race even with barriers forcing both
            // sides to hold their first lock first — allow a small bounded number of attempts
            // before failing the test outright.
            const int maxAttempts = 5;
            string? lastFailureReason = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var outcome = await TryRunOneDeadlockAttemptAsync(context);
                if (outcome is null)
                {
                    return; // success: exactly one side threw DeadlockException, the other committed
                }

                lastFailureReason = outcome;
                Output.WriteLine($"Deadlock attempt {attempt}/{maxAttempts} did not produce the expected outcome: {outcome}");
            }

            throw new Xunit.Sdk.XunitException(
                $"Could not reliably reproduce a MySQL deadlock after {maxAttempts} attempts. Last reason: {lastFailureReason}");
        });
    }

    /// <summary>
    /// Runs one attempt at the circular-wait scenario. Returns null on the expected outcome
    /// (exactly one side threw <see cref="DeadlockException"/>, the other committed), or a
    /// diagnostic string describing why the attempt didn't produce that outcome.
    /// </summary>
    private async Task<string?> TryRunOneDeadlockAttemptAsync(IDatabaseContext context)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var idColumn = context.WrapObjectName("id");
        var valColumn = context.WrapObjectName("val");

        // Reset both rows to a known state before each attempt (a prior attempt's victim rolled
        // back, but the winner's committed increment persists).
        await using (var reset = context.CreateSqlContainer(
                         $"UPDATE {table} SET {valColumn} = CASE {idColumn} WHEN 1 THEN 100 ELSE 200 END"))
        {
            await reset.ExecuteNonQueryAsync();
        }

        await using var context2 = await CreateAdditionalContextAsync(SupportedDatabase.MySql);

        var side1HoldsFirstLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var side2HoldsFirstLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> RunSideAsync(IDatabaseContext ctx, int firstId, int secondId,
            TaskCompletionSource ownSignal, TaskCompletionSource otherSignal)
        {
            using var tx = ctx.BeginTransaction();
            try
            {
                await LockRowAsync(tx, table, idColumn, valColumn, firstId);

                ownSignal.TrySetResult();
                await otherSignal.Task.WaitAsync(TimeSpan.FromSeconds(15));

                // One of the two concurrent LockRowAsync calls below blocks on the other
                // transaction's held row lock; InnoDB's deadlock detector eventually kills one of
                // them with error 1213 rather than letting both wait forever.
                await LockRowAsync(tx, table, idColumn, valColumn, secondId);

                tx.Commit();
                return null;
            }
            catch (Exception ex)
            {
                try
                {
                    tx.Rollback();
                }
                catch
                {
                    // The deadlock victim's transaction may already be aborted server-side.
                }

                return ex;
            }
        }

        var task1 = RunSideAsync(context, 1, 2, side1HoldsFirstLock, side2HoldsFirstLock);
        var task2 = RunSideAsync(context2, 2, 1, side2HoldsFirstLock, side1HoldsFirstLock);

        Exception?[] results;
        try
        {
            results = await Task.WhenAll(task1, task2);
        }
        catch (Exception ex)
        {
            return $"one side threw before Task.WhenAll could observe both results: {ex}";
        }

        var failures = results.Where(r => r is not null).ToList();
        var successCount = results.Count(r => r is null);

        if (failures.Count != 1 || successCount != 1)
        {
            var summary = string.Join("; ", results.Select((r, i) => $"side{i + 1}={(r is null ? "committed" : r.GetType().Name)}"));
            return $"expected exactly one failure and one success, got [{summary}]";
        }

        if (failures[0] is not DeadlockException deadlock)
        {
            return $"the failing side threw {failures[0]!.GetType().Name} instead of DeadlockException: {failures[0]!.Message}";
        }

        Output.WriteLine($"Confirmed live: MySQL deadlock victim threw {nameof(DeadlockException)} — {deadlock.Message}");
        return null;
    }

    private static async Task LockRowAsync(IDatabaseContext tx, string table, string idColumn, string valColumn, int id)
    {
        await using var container = tx.CreateSqlContainer();
        container.Query.Append("UPDATE ").Append(table)
            .Append(" SET ").Append(valColumn).Append(" = ").Append(valColumn).Append(" + 1")
            .Append(" WHERE ").Append(idColumn).Append(" = ")
            .Append(container.MakeParameterName(container.AddParameterWithValue("id", DbType.Int32, id)));
        await container.ExecuteNonQueryAsync();
    }
}
