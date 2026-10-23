using System.Data;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// A read-intent transaction is read-only at the database on Oracle. Before 23ai the entry
/// statement was sent as "SET TRANSACTION READ ONLY;", which those versions reject; the failure
/// was only logged and the transaction stayed writable.
/// </summary>
[Collection("IntegrationTests")]
public class OracleReadOnlyTransactionTests : DatabaseTestBase
{
    private const string TableName = "ro_tx_oracle";

    public OracleReadOnlyTransactionTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        IntegrationTestConfiguration.EnabledProviders.Where(p => p == SupportedDatabase.Oracle);

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, TableName);
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, TableName)} " +
            $"({context.WrapObjectName("id")} NUMBER(10) NOT NULL PRIMARY KEY)");
        await create.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task WriteInsideAReadIntentTransaction_IsRefusedByTheDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (_, context) =>
        {
            var table = IntegrationObjectNameHelper.Table(context, TableName);
            await using (var tx = await context.BeginTransactionAsync(executionType: ExecutionType.Read))
            {
                await using var insert = tx.CreateSqlContainer(
                    $"INSERT INTO {table} ({context.WrapObjectName("id")}) VALUES (1)");
                // Sent with read intent so pengdows.crud's own guard stays out of the way: what is
                // tested is that the database itself refuses (ORA-01456).
                await Assert.ThrowsAnyAsync<DatabaseOperationException>(async () =>
                    await insert.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text, CancellationToken.None));
            }

            await using var count = context.CreateSqlContainer($"SELECT COUNT(*) FROM {table}");
            Assert.Equal(0L, await count.ExecuteScalarRequiredAsync<long>());
        });
    }
}
