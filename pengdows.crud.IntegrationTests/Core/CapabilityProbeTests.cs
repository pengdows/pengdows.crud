using System.Linq;
using System.Threading;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Ported from testbed/TestProvider.cs's TestUpsertCapability/TestPagingCapability/
/// TestParenthesizedJoinCapability (part of the testbed-vs-IntegrationTests consolidation).
/// Each probe is gated on the dialect's own capability flag: a provider that doesn't claim the
/// capability skips (via <see cref="Xunit.Skip"/>), a provider that DOES claim it must pass, not
/// silently skip — matching the enforcement testbed's RunCapabilityTest used to provide.
/// </summary>
[Collection("IntegrationTests")]
public class CapabilityProbeTests : DatabaseTestBase
{
    private static long _nextId;

    public CapabilityProbeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task Capability_Upsert_InsertThenUpdate()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var supports = context.DataSourceInfo.SupportsMerge
                           || context.DataSourceInfo.SupportsInsertOnConflict
                           || context.DataSourceInfo.SupportsOnDuplicateKey;

            if (!supports)
            {
                Output.WriteLine($"Skipping upsert capability probe for {provider} (not supported)");
                return;
            }

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var id = Interlocked.Increment(ref _nextId);
            var t = new TestTable { Id = id, Name = NameEnum.Test, Description = "upsert-original", Value = 1 };

            await helper.UpsertAsync(t, context);
            t.Description = "upsert-updated";
            await helper.UpsertAsync(t, context);

            try
            {
                var retrieved = await helper.RetrieveOneAsync(id, context);
                Assert.NotNull(retrieved);
                Assert.Equal("upsert-updated", retrieved!.Description);
            }
            finally
            {
                await helper.DeleteAsync(id, context);
            }
        });
    }

    [SkippableFact]
    public async Task Capability_Paging_TwoPagesDoNotOverlap()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.SupportsOffsetFetch && !context.Dialect.SupportsLimitOffset)
            {
                Output.WriteLine($"Skipping paging capability probe for {provider} (not supported)");
                return;
            }

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var ids = new List<long>();
            for (var i = 0; i < 10; i++)
            {
                var id = Interlocked.Increment(ref _nextId);
                ids.Add(id);
                await helper.CreateAsync(
                    new TestTable { Id = id, Name = NameEnum.Test, Description = $"page-row-{i:D2}", Value = i },
                    context);
            }

            var idCol = context.WrapObjectName("id");
            try
            {
                var sc1 = helper.BuildRetrieve(ids, context);
                sc1.Query.Append(" ORDER BY ").Append(idCol);
                context.Dialect.AppendPaging(sc1.Query, 0, 5);
                var page1 = await helper.LoadListAsync(sc1);

                var sc2 = helper.BuildRetrieve(ids, context);
                sc2.Query.Append(" ORDER BY ").Append(idCol);
                context.Dialect.AppendPaging(sc2.Query, 5, 5);
                var page2 = await helper.LoadListAsync(sc2);

                Assert.Equal(5, page1.Count);
                Assert.Equal(5, page2.Count);

                var p1Ids = page1.Select(r => r.Id).ToHashSet();
                var overlap = page2.Select(r => r.Id).Where(x => p1Ids.Contains(x)).ToList();
                Assert.Empty(overlap);

                var allPaged = p1Ids.Concat(page2.Select(r => r.Id)).ToHashSet();
                Assert.True(allPaged.IsSubsetOf(ids));
            }
            finally
            {
                await helper.DeleteAsync(ids, context);
            }
        });
    }

    /// <summary>
    /// Proves a fully parenthesized multi-table join — standard SQL-92 grammar, and the syntax
    /// Microsoft Access requires for any join beyond two tables — parses and executes identically
    /// across every supported database. See testbed/TestProvider.cs's TestParenthesizedJoinCapability
    /// for the live-verified history behind <see cref="GetDummyFromClause"/> (Oracle/Db2/Firebird
    /// reject a table-less SELECT, which looked like a join-parenthesization rejection but wasn't).
    /// </summary>
    [SkippableFact]
    public async Task Capability_ParenthesizedJoin_ThreeTableJoinParses()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var subquery = $"(SELECT 1 AS v{GetDummyFromClause(provider)})";
            await using var sc = context.CreateSqlContainer();
            sc.Query.Append(
                $"SELECT a.v FROM ({subquery} a INNER JOIN {subquery} b ON a.v = b.v) " +
                $"INNER JOIN {subquery} c ON a.v = c.v");

            var result = await sc.ExecuteScalarOrNullAsync<int>();
            Assert.Equal(1, result);
        });
    }

    private static string GetDummyFromClause(SupportedDatabase product) => product switch
    {
        SupportedDatabase.Oracle => " FROM DUAL",
        SupportedDatabase.Db2 => " FROM SYSIBM.SYSDUMMY1",
        SupportedDatabase.Firebird => " FROM RDB$DATABASE",
        SupportedDatabase.InterBase => " FROM RDB$DATABASE",
        _ => string.Empty
    };
}
