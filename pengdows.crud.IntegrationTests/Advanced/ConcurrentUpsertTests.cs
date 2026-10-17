using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// UpsertAsync / BatchUpsertAsync must be atomic when callers race on the same key: every caller
/// succeeds, exactly one row results, and no unique-constraint violation escapes.
///
/// Why this exists: single-row upsert emits plain MERGE on SQL Server, Oracle, Db2, Snowflake and (15+)
/// PostgreSQL, with no HOLDLOCK/serialization hint. MERGE is not an atomic upsert on those engines;
/// two sessions can both take WHEN NOT MATCHED and one then fails with a unique violation. Batch upsert
/// on PostgreSQL uses ON CONFLICT, so the two paths can behave differently under the same race.
/// No other test exercises two callers on one key (MergeConflictTests.* are sequential on one context).
///
/// A transient failure (DatabaseException.IsTransient: deadlock, serialization retry on
/// CockroachDB/YugabyteDB) is retried, as the library's own contract allows. Anything else, in
/// particular UniqueConstraintViolationException, fails the test and names the provider.
/// </summary>
[Collection("IntegrationTests")]
public class ConcurrentUpsertTests : DatabaseTestBase
{
    private const string TableName = "concurrent_upsert_rows";
    private const int Workers = 16;
    private const int Rounds = 5;
    private const int MaxTransientAttempts = 5;

    private static long _nextKey = 9_000_000;

    public ConcurrentUpsertTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<ConcurrentUpsertRow>();
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task UpsertAsync_ManyCallersSameNewKey_OneRowNoUniqueViolation()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsUpsert(context))
            {
                Output.WriteLine($"{provider}: capability - upsert not supported");
                return;
            }

            await RecreateTableAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<ConcurrentUpsertRow>(context);

            for (var round = 0; round < Rounds; round++)
            {
                var key = Interlocked.Increment(ref _nextKey);
                var failures = await RunAllAsync(Workers, worker =>
                    RetryTransientAsync(() => gateway.UpsertAsync(
                        new ConcurrentUpsertRow { Id = key, Value = worker }, context)));

                AssertNoFailures(provider, "UpsertAsync same new key", round, failures);

                Assert.Equal(1, await CountRowsAsync(context, key));
                var row = await gateway.RetrieveOneAsync(new ConcurrentUpsertRow { Id = key }, context);
                Assert.NotNull(row);
                Assert.InRange(row!.Value, 0, Workers - 1);
            }
        });
    }

    [SkippableFact]
    public Task UpsertAsync_ManyCallersSameExistingKey_OneRowLastWriterWins()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsUpsert(context))
            {
                Output.WriteLine($"{provider}: capability - upsert not supported");
                return;
            }

            await RecreateTableAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<ConcurrentUpsertRow>(context);
            var key = Interlocked.Increment(ref _nextKey);
            await gateway.CreateAsync(new ConcurrentUpsertRow { Id = key, Value = -1 }, context);

            var failures = await RunAllAsync(Workers, worker =>
                RetryTransientAsync(() => gateway.UpsertAsync(
                    new ConcurrentUpsertRow { Id = key, Value = worker }, context)));

            AssertNoFailures(provider, "UpsertAsync same existing key", 0, failures);

            Assert.Equal(1, await CountRowsAsync(context, key));
            var row = await gateway.RetrieveOneAsync(new ConcurrentUpsertRow { Id = key }, context);
            Assert.InRange(row!.Value, 0, Workers - 1); // some writer won; the seed value (-1) did not survive
        });
    }

    [SkippableFact]
    public Task BatchUpsertAsync_OverlappingKeysAcrossCallers_NoDuplicatesNoUniqueViolation()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsUpsert(context))
            {
                Output.WriteLine($"{provider}: capability - upsert not supported");
                return;
            }

            await RecreateTableAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<ConcurrentUpsertRow>(context);
            var baseKey = Interlocked.Add(ref _nextKey, 100);
            const int keysPerBatch = 10;

            // Every worker upserts the same ten keys, so each batch collides with every other.
            var failures = await RunAllAsync(Workers, worker =>
                RetryTransientAsync(async () =>
                {
                    var batch = Enumerable.Range(0, keysPerBatch)
                        .Select(i => new ConcurrentUpsertRow { Id = baseKey + i, Value = worker })
                        .ToList();
                    return await gateway.BatchUpsertAsync(batch, context);
                }));

            AssertNoFailures(provider, "BatchUpsertAsync overlapping keys", 0, failures);

            for (var i = 0; i < keysPerBatch; i++)
            {
                Assert.Equal(1, await CountRowsAsync(context, baseKey + i));
            }
        });
    }

    private static async Task<int> RetryTransientAsync(Func<ValueTask<int>> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DatabaseException ex) when (ex.IsTransient == true && attempt < MaxTransientAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }
    }

    private static async Task<List<Exception>> RunAllAsync(int workers, Func<int, Task<int>> work)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, workers).Select(async worker =>
        {
            await start.Task; // release every worker together to maximise the race
            try
            {
                await work(worker);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }).ToList();

        start.SetResult();
        var results = await Task.WhenAll(tasks);
        return results.Where(e => e != null).Select(e => e!).ToList();
    }

    private void AssertNoFailures(SupportedDatabase provider, string what, int round, List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var unique = failures.OfType<UniqueConstraintViolationException>().Count();
        var summary = string.Join("; ", failures.GroupBy(f => f.GetType().Name)
            .Select(g => $"{g.Count()} x {g.Key}: {g.First().Message}"));
        Output.WriteLine($"{provider}: {what} round {round}: {failures.Count}/{Workers} failed ({unique} unique-violation). {summary}");
        Assert.Fail(
            $"{provider}: {what} is not atomic under concurrency: {failures.Count}/{Workers} callers failed " +
            $"({unique} with UniqueConstraintViolationException). {summary}");
    }

    private static async Task<int> CountRowsAsync(IDatabaseContext context, long key)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        await using var sc = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table} WHERE {context.WrapObjectName("id")} = ");
        sc.Query.Append(sc.MakeParameterName(sc.AddParameterWithValue("k", DbType.Int64, key)));
        return Convert.ToInt32(await sc.ExecuteScalarRequiredAsync<long>());
    }

    private static async Task RecreateTableAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, TableName);
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var id = context.WrapObjectName("id");
        var value = context.WrapObjectName("value");
        var bigInt = IntegrationObjectNameHelper.BigIntType(provider);
        var intType = IntegrationObjectNameHelper.IntType(provider);

        await using var sc = context.CreateSqlContainer(
            $"CREATE TABLE {table} ({id} {bigInt} NOT NULL PRIMARY KEY, {value} {intType} NOT NULL)");
        await sc.ExecuteNonQueryAsync();
    }
}

[Table(TableNameConst)]
public class ConcurrentUpsertRow
{
    public const string TableNameConst = "concurrent_upsert_rows";

    [PrimaryKey(1)][Column("id", DbType.Int64)] public long Id { get; set; }

    [Column("value", DbType.Int32)] public int Value { get; set; }
}
