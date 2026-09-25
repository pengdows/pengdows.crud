using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Firebird rejects DDL on a table another attachment still has in use ("object TABLE ... is in
/// use"). FirebirdDialect clears the ADO.NET pool before DDL; reader and writer use distinct
/// connection strings (Application Name) and therefore distinct pools, so a read that leaves an
/// idle connection in the READER pool must not block a following DROP/ALTER on the writer.
/// </summary>
[Collection("IntegrationTests")]
public sealed class FirebirdDdlPoolResetTests : DatabaseTestBase
{
    public FirebirdDdlPoolResetTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Firebird).ToArray();

    [SkippableFact]
    public async Task Ddl_AfterReadLeavesIdleReaderConnection_Succeeds()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Firebird, async context =>
        {
            await DropTableIfExistsAsync(context, "ddl_pool_probe");
            var table = IntegrationObjectNameHelper.Table(context, "ddl_pool_probe");
            var id = context.WrapObjectName("id");

            await using (var create = context.CreateSqlContainer($"CREATE TABLE {table} ({id} INTEGER NOT NULL PRIMARY KEY)"))
            {
                await create.ExecuteNonQueryAsync();
            }

            await using (var insert = context.CreateSqlContainer($"INSERT INTO {table} ({id}) VALUES (1)"))
            {
                await insert.ExecuteNonQueryAsync();
            }

            // Parameterized reads (prepared where the dialect prepares) leave statement handles
            // on the pooled reader attachment, which is what holds the table "in use".
            for (var i = 0; i < 3; i++)
            {
                await using var read = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table} WHERE {id} = ");
                var p = read.AddParameterWithValue("id", System.Data.DbType.Int32, 1);
                read.Query.Append(read.MakeParameterName(p));
                Assert.Equal(1, await read.ExecuteScalarRequiredAsync<int>(ExecutionType.Read));
                await using var reader = await read.ExecuteReaderAsync(ExecutionType.Read);
                while (await reader.ReadAsync())
                {
                }
            }

            await using (var alter = context.CreateSqlContainer($"ALTER TABLE {table} ADD {context.WrapObjectName("extra")} INTEGER"))
            {
                await alter.ExecuteNonQueryAsync();
            }

            await using (var read = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table}"))
            {
                Assert.Equal(1, await read.ExecuteScalarRequiredAsync<int>(ExecutionType.Read));
            }

            await using (var drop = context.CreateSqlContainer($"DROP TABLE {table}"))
            {
                await drop.ExecuteNonQueryAsync();
            }
        });
    }
}
