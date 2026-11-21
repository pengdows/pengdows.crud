using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// InterBase, like Firebird, rejects DDL on an object another attachment still has in use ("object INDEX ... is in
/// use"). InterBaseDialect clears the ADO.NET pools before DDL and runs the DDL in a WAIT transaction. Reads leave
/// statement handles on a pooled attachment, which is what holds the object; a following CREATE INDEX or DROP TABLE on
/// the writer must still succeed.
/// </summary>
[Collection("IntegrationTests")]
public sealed class InterBaseDdlPoolResetTests : DatabaseTestBase
{
    public InterBaseDdlPoolResetTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.InterBase).ToArray();

    private static async Task ReadOnceAsync(IDatabaseContext context, string table, string id)
    {
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
    }

    [SkippableFact]
    public async Task Ddl_AfterReadsLeaveIdlePooledAttachments_Succeeds()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.InterBase, async context =>
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

            // Concurrent parameterized reads: each caller holds its own pooled attachment with statement
            // handles on the table, the state that made the failing concurrent tests hit "object ... in use".
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReadOnceAsync(context, table, id)));

            await using (var index = context.CreateSqlContainer(
                             $"CREATE INDEX {context.WrapObjectName("ix_ddl_pool_probe_id")} ON {table} ({id})"))
            {
                await index.ExecuteNonQueryAsync();
            }

            await using (var drop = context.CreateSqlContainer($"DROP TABLE {table}"))
            {
                await drop.ExecuteNonQueryAsync();
            }
        });
    }
}
