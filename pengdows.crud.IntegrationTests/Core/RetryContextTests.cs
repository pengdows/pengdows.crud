using System.Threading;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Ported from testbed/TestProvider.cs's TestRetryContextSequential/TestRetryContextTransactional
/// (part of the testbed-vs-IntegrationTests consolidation — see CLAUDE.md's "Adding a New
/// Database" workflow notes) — RetryContext previously had no coverage at all in this project,
/// only in testbed's own ad-hoc check battery. Verifies RetryContext against a real provider in
/// both of its modes: Sequential (each queued command commits in its own transaction) and
/// Transactional (all queued commands commit together, atomically).
/// </summary>
[Collection("IntegrationTests")]
public class RetryContextTests : DatabaseTestBase
{
    private static long _nextId;

    public RetryContextTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task RetryContext_Sequential_EachCommandCommitsInOwnTransaction()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var before = await CountTestRows(context);

            await using var rc = new RetryContext(context, RetryContextType.Sequential);
            var id1 = Interlocked.Increment(ref _nextId);
            var id2 = Interlocked.Increment(ref _nextId);
            var t1 = new TestTable { Id = id1, Name = NameEnum.Test, Description = "retry-sequential-1" };
            var t2 = new TestTable { Id = id2, Name = NameEnum.Test, Description = "retry-sequential-2" };

            using var c1 = helper.BuildCreate(t1, rc);
            using var c2 = helper.BuildCreate(t2, rc);

            await rc.StartAsync();

            Assert.Equal(0, rc.QueuedCommandCount);

            var after = await CountTestRows(context);
            Assert.Equal(before + 2, after);

            await helper.DeleteAsync(id1, context);
            await helper.DeleteAsync(id2, context);
        });
    }

    [SkippableFact]
    public async Task RetryContext_Transactional_AllCommandsCommitTogether()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var before = await CountTestRows(context);

            await using var rc = new RetryContext(context, RetryContextType.Transactional);
            var id1 = Interlocked.Increment(ref _nextId);
            var id2 = Interlocked.Increment(ref _nextId);
            var t1 = new TestTable { Id = id1, Name = NameEnum.Test, Description = "retry-transactional-1" };
            var t2 = new TestTable { Id = id2, Name = NameEnum.Test, Description = "retry-transactional-2" };

            using var c1 = helper.BuildCreate(t1, rc);
            using var c2 = helper.BuildCreate(t2, rc);

            await rc.StartAsync();

            Assert.True(rc.IsCompleted);

            var after = await CountTestRows(context);
            Assert.Equal(before + 2, after);

            await helper.DeleteAsync(id1, context);
            await helper.DeleteAsync(id2, context);
        });
    }

    private static async Task<int> CountTestRows(IDatabaseContext context)
    {
        await using var sc = context.CreateSqlContainer();
        sc.Query.Append("SELECT COUNT(*) FROM ").Append(context.WrapObjectName("test_table"));
        return await sc.ExecuteScalarOrNullAsync<int>();
    }
}
