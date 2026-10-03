using System.Data;
using System.Reflection;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// REV-050/051, live: a write the database refuses because the transaction or the database is
/// read-only surfaces as ReadOnlyViolationException, and a read-intent transaction is read-only at
/// the database exactly where the dialect says so (EnforcesReadOnlyTransactions).
/// </summary>
[Collection("IntegrationTests")]
public class ReadOnlyRefusalTranslationTests : DatabaseTestBase
{
    private const string TableName = "ro_refusal";

    public ReadOnlyRefusalTranslationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    private static async Task<string> CreateTableAsync(IDatabaseContext context, SupportedDatabase provider)
    {
        await DropTableIfExistsAsync(context, TableName);
        var table = IntegrationObjectNameHelper.Table(context, TableName);
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {table} ({context.WrapObjectName("id")} {IntegrationObjectNameHelper.IntType(provider)} NOT NULL PRIMARY KEY)");
        await create.ExecuteNonQueryAsync();
        return table;
    }

    private static string Insert(IDatabaseContext context, string table, int id) =>
        $"INSERT INTO {table} ({context.WrapObjectName("id")}) VALUES ({id})";

    [SkippableFact]
    public async Task WriteThroughAReadIntentTransaction_IsRefusedExactlyWhereTheDialectEnforcesIt()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var table = await CreateTableAsync(context, provider);
            var enforced = ((SqlDialect)context.Dialect).EnforcesReadOnlyTransactions;

            await using var tx = await context.BeginTransactionAsync(executionType: ExecutionType.Read);
            await using var insert = tx.CreateSqlContainer(Insert(context, table, 1));
            // Sent with read intent so pengdows.crud's own guard stays out of the way: what is
            // tested is the database's refusal and its translation.
            var write = async () => await insert.ExecuteNonQueryAsync(ExecutionType.Read, CommandType.Text,
                CancellationToken.None);

            if (enforced)
            {
                await Assert.ThrowsAsync<ReadOnlyViolationException>(write);
                return;
            }

            Assert.Equal(1, await write());
        });
    }

    // A read-only database (not only a read-only transaction): SQL Server 3906.
    [SkippableFact]
    public async Task SqlServer_WriteToAReadOnlyDatabase_IsReadOnlyViolation()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async context =>
        {
            var table = await CreateTableAsync(context, SupportedDatabase.SqlServer);
            await ExecuteAsync(context, "ALTER DATABASE CURRENT SET READ_ONLY WITH ROLLBACK IMMEDIATE");
            try
            {
                await using var writer = await CreateAdditionalContextAsync(SupportedDatabase.SqlServer);
                await using var insert = writer.CreateSqlContainer(Insert(writer, table, 2));
                await Assert.ThrowsAsync<ReadOnlyViolationException>(async () => await insert.ExecuteNonQueryAsync());
            }
            finally
            {
                await using var restorer = await CreateAdditionalContextAsync(SupportedDatabase.SqlServer);
                await ExecuteAsync(restorer, "ALTER DATABASE CURRENT SET READ_WRITE WITH ROLLBACK IMMEDIATE");
            }
        });
    }

    // MySQL super_read_only (1290) and TiDB tidb_super_read_only (1836).
    [SkippableTheory]
    [InlineData(SupportedDatabase.MySql, "SET GLOBAL super_read_only = ON", "SET GLOBAL super_read_only = OFF")]
    [InlineData(SupportedDatabase.TiDb, "SET GLOBAL tidb_super_read_only = ON", "SET GLOBAL tidb_super_read_only = OFF")]
    public async Task ServerInReadOnlyMode_WriteIsReadOnlyViolation(SupportedDatabase provider, string on, string off)
    {
        await RunTestAgainstProviderAsync(provider, async context =>
        {
            var table = await CreateTableAsync(context, provider);
            await ExecuteAsync(context, on);
            try
            {
                await using var writer = await CreateAdditionalContextAsync(provider);
                await using var insert = writer.CreateSqlContainer(Insert(writer, table, 3));
                await Assert.ThrowsAsync<ReadOnlyViolationException>(async () => await insert.ExecuteNonQueryAsync());
            }
            finally
            {
                await ExecuteAsync(context, off);
            }
        });
    }

    // Firebird's read-only transaction is a provider option (no SQL statement): the refusal
    // (isc_read_only_trans) must classify as ReadOnlyViolation.
    [SkippableFact]
    public async Task Firebird_WriteInAReadOnlyProviderTransaction_ClassifiesAsReadOnlyViolation()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Firebird, async context =>
        {
            var table = await CreateTableAsync(context, SupportedDatabase.Firebird);
            var containers = (System.Collections.IDictionary)typeof(IntegrationTestFixture)
                .GetField("_containers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Fixture)!;
            var container = containers[SupportedDatabase.Firebird]!;
            var connectionString = (string)container.GetType()
                .GetField("_connectionString", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(container)!;

            await using var connection = new FirebirdSql.Data.FirebirdClient.FbConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(
                new FirebirdSql.Data.FirebirdClient.FbTransactionOptions
                {
                    TransactionBehavior = FirebirdSql.Data.FirebirdClient.FbTransactionBehavior.Read |
                                          FirebirdSql.Data.FirebirdClient.FbTransactionBehavior.Concurrency
                });
            await using var command = new FirebirdSql.Data.FirebirdClient.FbCommand(Insert(context, table, 4), connection,
                transaction);

            var refusal = await Assert.ThrowsAnyAsync<Exception>(async () => await command.ExecuteNonQueryAsync());

            Assert.Equal(DbErrorCategory.ReadOnlyViolation, context.Dialect.AnalyzeException(refusal).Category);
        });
    }

    private static async Task ExecuteAsync(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }
}
