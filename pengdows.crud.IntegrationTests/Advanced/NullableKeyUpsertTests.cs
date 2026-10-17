using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Advanced;

/// <summary>
/// Audit G-06, proven live before the fix: a NULL in an upsert's conflict key never matches (MERGE's <c>ON t.k = s.k</c>,
/// ON CONFLICT and ON DUPLICATE KEY alike), so upserting the same key (7, NULL) twice left 2 rows on SQLite, PostgreSQL
/// and MySQL (silent duplicates) and raised a unique violation on SQL Server. The upsert is now refused up front and
/// writes nothing; a key with every component set still upserts normally.
/// </summary>
[Collection("IntegrationTests")]
public class NullableKeyUpsertTests : DatabaseTestBase
{
    private const string TableName = "nullable_key_rows";

    public NullableKeyUpsertTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<NullableKeyRow>();
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task UpsertAsync_ANullKeyComponent_IsRefusedAndWritesNothing()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsUpsert(context))
            {
                Output.WriteLine($"{provider}: capability - upsert not supported");
                return;
            }

            await RecreateTableAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<NullableKeyRow>(context);

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await gateway.UpsertAsync(new NullableKeyRow { A = 7, B = null, Value = 1 }, context));

            Assert.Equal(0, await CountAsync(context, 7));
        });
    }

    [SkippableFact]
    public Task UpsertAsync_AKeyWithEveryComponentSet_UpdatesInPlace()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (!SupportsUpsert(context))
            {
                Output.WriteLine($"{provider}: capability - upsert not supported");
                return;
            }

            await RecreateTableAsync(provider, context);
            var gateway = new PrimaryKeyTableGateway<NullableKeyRow>(context);

            await gateway.UpsertAsync(new NullableKeyRow { A = 7, B = 1, Value = 1 }, context);
            await gateway.UpsertAsync(new NullableKeyRow { A = 7, B = 1, Value = 2 }, context);

            Assert.Equal(1, await CountAsync(context, 7));
        });
    }

    private static async Task<int> CountAsync(IDatabaseContext context, long a)
    {
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        await using var sc = context.CreateSqlContainer(
            $"SELECT COUNT(*) FROM {table} WHERE {context.WrapObjectName("a")} = ");
        sc.Query.Append(sc.MakeParameterName(sc.AddParameterWithValue("a", DbType.Int64, a)));
        return Convert.ToInt32(await sc.ExecuteScalarRequiredAsync<long>());
    }

    private static async Task RecreateTableAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, TableName);
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        var a = context.WrapObjectName("a");
        var b = context.WrapObjectName("b");
        var value = context.WrapObjectName("value");
        var bigInt = IntegrationObjectNameHelper.BigIntType(provider);
        var intType = IntegrationObjectNameHelper.IntType(provider);

        await using var sc = context.CreateSqlContainer(
            $"CREATE TABLE {table} ({a} {bigInt} NOT NULL, {b} {bigInt} NULL, {value} {intType} NOT NULL, UNIQUE ({a}, {b}))");
        await sc.ExecuteNonQueryAsync();
    }
}

[Table(NullableKeyUpsertTestsTable.Name)]
public class NullableKeyRow
{
    [PrimaryKey(1)][Column("a", DbType.Int64)] public long A { get; set; }

    [PrimaryKey(2)][Column("b", DbType.Int64)] public long? B { get; set; }

    [Column("value", DbType.Int32)] public int Value { get; set; }
}

internal static class NullableKeyUpsertTestsTable
{
    public const string Name = "nullable_key_rows";
}
