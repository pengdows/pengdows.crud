using System.Data;
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

    /// <summary>
    /// <c>ISqlDialect.SupportsWindowFunctions</c> was previously asserted only as a boolean on the
    /// dialect object — never exercised as a real query against a live server. Proves
    /// <c>ROW_NUMBER() OVER (ORDER BY ...)</c> actually executes and produces correctly-ordered
    /// ranks for every provider that claims support.
    /// </summary>
    [SkippableFact]
    public async Task Capability_WindowFunctions_RowNumberOrdersCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.SupportsWindowFunctions)
            {
                Output.WriteLine($"Skipping window function capability probe for {provider} (not supported)");
                return;
            }

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var ids = new List<long>();
            var values = new[] { 30, 10, 20 };
            foreach (var v in values)
            {
                var id = Interlocked.Increment(ref _nextId);
                ids.Add(id);
                await helper.CreateAsync(
                    new TestTable { Id = id, Name = NameEnum.Test, Description = "window-fn", Value = v }, context);
            }

            try
            {
                var idCol = context.WrapObjectName("id");
                var valueCol = context.WrapObjectName("value");
                var idList = string.Join(",", ids);

                await using var sc = context.CreateSqlContainer(
                    $"SELECT {idCol}, ROW_NUMBER() OVER (ORDER BY {valueCol}) AS rn " +
                    $"FROM {IntegrationObjectNameHelper.Table(context, "test_table")} " +
                    $"WHERE {idCol} IN ({idList}) ORDER BY rn");

                await using var reader = await sc.ExecuteReaderAsync();
                var rankedIds = new List<long>();
                while (await reader.ReadAsync())
                {
                    rankedIds.Add(Convert.ToInt64(reader.GetValue(0)));
                }

                // values = [30, 10, 20] inserted for ids[0..2] — sorted by value the order must be
                // ids[1] (10), ids[2] (20), ids[0] (30).
                Assert.Equal(new[] { ids[1], ids[2], ids[0] }, rankedIds);
            }
            finally
            {
                await helper.DeleteAsync(ids, context);
            }
        });
    }

    /// <summary>
    /// <c>ISqlDialect.SupportsCommonTableExpressions</c> was previously asserted only as a boolean
    /// — never exercised as a real query. Proves a <c>WITH cte AS (...)</c> block round-trips real
    /// data for every provider that claims support.
    /// </summary>
    [SkippableFact]
    public async Task Capability_CommonTableExpression_ReturnsRealData()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!context.Dialect.SupportsCommonTableExpressions)
            {
                Output.WriteLine($"Skipping CTE capability probe for {provider} (not supported)");
                return;
            }

            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var id = Interlocked.Increment(ref _nextId);
            await helper.CreateAsync(
                new TestTable { Id = id, Name = NameEnum.Test, Description = "cte-row", Value = 777 }, context);

            try
            {
                var idCol = context.WrapObjectName("id");
                var valueCol = context.WrapObjectName("value");
                var table = IntegrationObjectNameHelper.Table(context, "test_table");

                await using var sc = context.CreateSqlContainer(
                    $"WITH cte_probe AS (SELECT {idCol} AS cid, {valueCol} AS cval FROM {table} WHERE {idCol} = {id}) " +
                    "SELECT cval FROM cte_probe");

                var result = await sc.ExecuteScalarOrNullAsync<int>();
                Assert.Equal(777, result);
            }
            finally
            {
                await helper.DeleteAsync(id, context);
            }
        });
    }

    /// <summary>
    /// <c>ISqlDialect.SupportsArrayTypes</c> — Oracle's own array-binding round trip
    /// (<c>OracleArrayBindingRoundTripTests.cs</c>) already covers Oracle specifically; this
    /// covers PostgreSQL's native <c>int[]</c> array column, the other provider that claims this
    /// capability and is broadly available without extra opt-in flags (DuckDB/Db2/Firebird/
    /// Snowflake also claim it but are either covered elsewhere or opt-in-gated).
    /// </summary>
    [SkippableFact]
    public async Task Capability_ArrayTypes_PostgresIntArrayRoundTrips()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            Skip.IfNot(context.Dialect.SupportsArrayTypes, "PostgreSQL dialect does not claim SupportsArrayTypes");

            await DropTableIfExistsAsync(context, "array_capability_probe");
            var table = IntegrationObjectNameHelper.Table(context, "array_capability_probe");
            await using (var create = context.CreateSqlContainer(
                             $"CREATE TABLE {table} (id INTEGER PRIMARY KEY, values_col INTEGER[] NOT NULL)"))
            {
                await create.ExecuteNonQueryAsync();
            }

            try
            {
                await using (var insert = context.CreateSqlContainer(
                                 $"INSERT INTO {table} (id, values_col) VALUES (1, @vals)"))
                {
                    insert.AddParameterWithValue("vals", DbType.Object, new[] { 2, 4, 8 });
                    await insert.ExecuteNonQueryAsync();
                }

                await using var select = context.CreateSqlContainer($"SELECT values_col FROM {table} WHERE id = 1");
                await using var reader = await select.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                var actual = (int[])reader.GetValue(0);
                Assert.Equal(new[] { 2, 4, 8 }, actual);
            }
            finally
            {
                await DropTableIfExistsAsync(context, "array_capability_probe");
            }
        });
    }
}
