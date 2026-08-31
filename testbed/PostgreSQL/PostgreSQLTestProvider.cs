#region

using System.Data;
using Npgsql;
using pengdows.crud;
using pengdows.crud.attributes;

#endregion

namespace testbed.PostgreSQL;

public class PostgreSQLTestProvider
    : TestProvider
{
    private readonly IDatabaseContext context;

    public PostgreSQLTestProvider(IDatabaseContext context, IServiceProvider serviceProvider) : base(context,
        serviceProvider)
    {
        this.context = context;
    }

    /// <summary>
    /// Raw-ADO.NET probe (bypasses pengdows.crud entirely): opens a transaction, inserts a row,
    /// then terminates the owning backend from a second connection without ever calling Commit()
    /// or Rollback() on the first. Verifies via a third connection that the insert did not
    /// survive — proof that PostgreSQL itself rolls back an in-flight transaction when its backend
    /// dies, independent of anything the client does.
    /// </summary>
    protected override async Task TestTransactionRollbackOnKilledConnection()
    {
        var rawCs = (context as DatabaseContext)?.RawConnectionString ?? context.ConnectionString;

        await using (var drop = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe"))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = context.CreateSqlContainer(
                         "CREATE TABLE kill_probe (id INT PRIMARY KEY, val TEXT)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnection? connA = null;
            try
            {
                connA = new NpgsqlConnection(rawCs);
                await connA.OpenAsync();

                int pid;
                await using (var pidCmd = connA.CreateCommand())
                {
                    pidCmd.CommandText = "SELECT pg_backend_pid()";
                    pid = Convert.ToInt32(await pidCmd.ExecuteScalarAsync());
                }

                var txnA = await connA.BeginTransactionAsync();
                await using (var insertCmd = connA.CreateCommand())
                {
                    insertCmd.Transaction = txnA;
                    insertCmd.CommandText = "INSERT INTO kill_probe (id, val) VALUES (1, 'should-vanish')";
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // Never Commit()/Rollback() txnA — the server-side terminate below must be what
                // undoes this insert, not our own client-side cleanup.
                await using (var connB = new NpgsqlConnection(rawCs))
                {
                    await connB.OpenAsync();
                    await using var killCmd = connB.CreateCommand();
                    killCmd.CommandText = "SELECT pg_terminate_backend(@pid)";
                    killCmd.Parameters.AddWithValue("pid", pid);
                    await killCmd.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (connA != null)
                {
                    try { connA.Close(); } catch { /* backend already terminated server-side */ }
                    try { await connA.DisposeAsync(); } catch { /* tearing down an already-terminated backend's internal state */ }
                }
            }

            long count = -1;
            for (var i = 0; i < 20; i++)
            {
                await using var connC = new NpgsqlConnection(rawCs);
                await connC.OpenAsync();
                await using var checkCmd = connC.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM kill_probe WHERE id = 1";
                count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    break;
                }

                await Task.Delay(250);
            }

            if (count == 0)
            {
                CheckOk("PostgreSQL.TransactionRollbackOnKilledConnection",
                    "  [TransactionRollbackOnKilledConnection] Terminating the backend mid-transaction rolled back the uncommitted insert server-side: OK");
            }
            else
            {
                CheckFail("PostgreSQL.TransactionRollbackOnKilledConnection",
                    $"row survived a terminated backend's uncommitted transaction (count={count}) — server did not roll back on connection death");
            }
        }
        finally
        {
            await using var cleanup = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    public override async Task CreateTable()
    {
        var databaseContext = context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var tableName = databaseContext.WrapObjectName("test_table");
        var idColumn = databaseContext.WrapObjectName("id");
        var nameColumn = databaseContext.WrapObjectName("name");
        var descriptionColumn = databaseContext.WrapObjectName("description");
        var valueColumn = databaseContext.WrapObjectName("value");
        var isActiveColumn = databaseContext.WrapObjectName("is_active");
        var createdAtColumn = databaseContext.WrapObjectName("created_at");
        var createdByColumn = databaseContext.WrapObjectName("created_by");
        var updatedAtColumn = databaseContext.WrapObjectName("updated_at");
        var updatedByColumn = databaseContext.WrapObjectName("updated_by");
        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}", tableName);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist, ignore
        }

        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
-- Create table
CREATE TABLE {0} (
    {1} SERIAL PRIMARY KEY,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INT NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} TIMESTAMP NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} TIMESTAMP NOT NULL,
    {9} VARCHAR(100) NOT NULL
);
", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n --- Continuing anyways");
        }
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        // GENERATED ALWAYS AS IDENTITY was introduced in PostgreSQL 10
        if (context.DataSourceInfo.ParsedVersion == null || context.DataSourceInfo.ParsedVersion.Major >= 10)
        {
            await TestExplicitIdentityUpsertAsync();
        }
        else
        {
            CheckSkip("PostgreSql.GeneratedAlwaysIdentity", $"Skipped GENERATED ALWAYS AS IDENTITY test on PostgreSQL {context.DataSourceInfo.DatabaseProductVersion} (requires PostgreSQL 10+)");
        }
    }

    private async Task TestExplicitIdentityUpsertAsync()
    {
        var tableName = context.WrapObjectName("postgres_explicit_identity_upsert");
        await using var container = context.CreateSqlContainer();

        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{context.WrapObjectName("id")} INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, ");
            container.Query.Append($"{context.WrapObjectName("value")} VARCHAR(100) NOT NULL)");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<ExplicitIdentityUpsertRow, int>(context);
            var row = new ExplicitIdentityUpsertRow { Id = 42, Value = "before" };
            await gateway.UpsertAsync(row);
            row.Value = "after";
            await gateway.UpsertAsync(row);

            var loaded = await gateway.RetrieveOneAsync(42);
            if (loaded?.Value != "after")
            {
                throw new Exception("PostgreSQL explicit identity upsert did not persist the updated value");
            }

            CheckOk("PostgreSql.GeneratedAlwaysIdentity", "PostgreSQL explicit GENERATED ALWAYS identity upsert: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("postgres_explicit_identity_upsert")]
    private class ExplicitIdentityUpsertRow
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
