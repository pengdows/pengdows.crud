using System.Data;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;

namespace testbed.SqlServer;

public sealed class SqlServerTestProvider : TestProvider
{
    public SqlServerTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        await TestIdentityReturningWithTriggerAsync();
        await TestPagingRequiresOrderByAsync();
    }

    /// <summary>
    /// Raw-ADO.NET probe (bypasses pengdows.crud entirely): opens a transaction, inserts a row,
    /// then KILLs the owning session from a second connection without ever calling Commit() or
    /// Rollback() on the first. Verifies via a third connection that the insert did not survive —
    /// proof that SQL Server itself rolls back an in-flight transaction when its session dies,
    /// independent of anything the client does.
    /// </summary>
    protected override async Task TestTransactionRollbackOnKilledConnection()
    {
        var rawCs = (_context as DatabaseContext)?.RawConnectionString ?? _context.ConnectionString;

        await using (var drop = _context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe"))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await using (var create = _context.CreateSqlContainer(
                         "CREATE TABLE kill_probe (id INT PRIMARY KEY, val NVARCHAR(50))"))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            SqlConnection? connA = null;
            try
            {
                connA = new SqlConnection(rawCs);
                await connA.OpenAsync();

                int spid;
                await using (var spidCmd = connA.CreateCommand())
                {
                    spidCmd.CommandText = "SELECT @@SPID";
                    spid = Convert.ToInt32(await spidCmd.ExecuteScalarAsync());
                }

                var txnA = connA.BeginTransaction();
                await using (var insertCmd = connA.CreateCommand())
                {
                    insertCmd.Transaction = txnA;
                    insertCmd.CommandText = "INSERT INTO kill_probe (id, val) VALUES (1, 'should-vanish')";
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // Never Commit()/Rollback() txnA — the server-side KILL below must be what undoes
                // this insert, not our own client-side cleanup.
                await using (var connB = new SqlConnection(rawCs))
                {
                    await connB.OpenAsync();
                    await using var killCmd = connB.CreateCommand();
                    killCmd.CommandText = $"KILL {spid}";
                    await killCmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (connA != null)
                {
                    try { connA.Close(); } catch { /* session already killed server-side */ }
                    try { connA.Dispose(); } catch { /* tearing down an already-killed connection's internal state */ }
                }
            }

            // SQL Server's KILL triggers rollback asynchronously; poll briefly rather than
            // assuming it's instantaneous.
            long count = -1;
            for (var i = 0; i < 20; i++)
            {
                await using var connC = new SqlConnection(rawCs);
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
                CheckOk("SqlServer.TransactionRollbackOnKilledConnection",
                    "  [TransactionRollbackOnKilledConnection] KILLing the session mid-transaction rolled back the uncommitted insert server-side: OK");
            }
            else
            {
                CheckFail("SqlServer.TransactionRollbackOnKilledConnection",
                    $"row survived a killed session's uncommitted transaction (count={count}) — server did not roll back on connection death");
            }
        }
        finally
        {
            await using var cleanup = _context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// AUTO_CLOSE is OFF by default for ordinary SQL Server databases (unlike Firebird's
    /// RDB$LINGER=0 or Db2's implicit-activation default), so DbMode.Best correctly stays
    /// Standard without this. This override exists purely to empirically check: IF an
    /// application/DBA turns AUTO_CLOSE on for a specific database, does the same idle-unload
    /// probe methodology detect a real cost the way it does for Firebird? (It must not change
    /// SQL Server's own DbMode.Best resolution — that stays Standard; AUTO_CLOSE is opt-in.)
    /// </summary>
    protected override async Task<bool> TryEnableFastIdleUnloadAsync()
    {
        await using var sc = _context.CreateSqlContainer("ALTER DATABASE CURRENT SET AUTO_CLOSE ON");
        await sc.ExecuteNonQueryAsync();
        return true;
    }

    protected override void ClearProviderPoolForIdleUnloadProbe()
    {
        SqlConnection.ClearAllPools();
    }

    /// <summary>
    /// Restores AUTO_CLOSE to OFF — the default for every non-Express SQL Server edition,
    /// including the full server image the testbed container runs — so the probe doesn't leave
    /// every later test in this testbed run against an AUTO_CLOSE-enabled database.
    /// </summary>
    protected override async Task RestoreIdleUnloadKnobAsync()
    {
        await using var sc = _context.CreateSqlContainer("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF");
        await sc.ExecuteNonQueryAsync();
    }

    private Task TestPagingRequiresOrderByAsync()
    {
        using var container = _context.CreateSqlContainer();
        container.Query.Append("SELECT 1");

        try
        {
            _context.GetDialect().AppendPaging(container.Query, offset: 0, limit: 1);
            throw new Exception("SQL Server paging without ORDER BY was not rejected");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("ORDER BY", StringComparison.Ordinal))
        {
            CheckOk("SqlServer.PagingWithoutOrderBy", "SQL Server paging without ORDER BY: rejected before execution");
        }

        return Task.CompletedTask;
    }

    private async Task TestIdentityReturningWithTriggerAsync()
    {
        var tableName = _context.WrapObjectName("sqlserver_trigger_identity_test");
        var auditTableName = _context.WrapObjectName("sqlserver_trigger_identity_audit");
        await using var container = _context.CreateSqlContainer();

        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();
        container.Clear();
        container.Query.Append($"DROP TABLE IF EXISTS {auditTableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{_context.WrapObjectName("id")} INT IDENTITY(1,1) PRIMARY KEY, ");
            container.Query.Append($"{_context.WrapObjectName("value")} NVARCHAR(100) NOT NULL)");
            await container.ExecuteNonQueryAsync();

            container.Clear();
            container.Query.Append($"CREATE TABLE {auditTableName} ({_context.WrapObjectName("id")} INT IDENTITY(1,1) PRIMARY KEY)");
            await container.ExecuteNonQueryAsync();

            container.Clear();
            container.Query.Append($"CREATE TRIGGER {_context.WrapObjectName("sqlserver_trigger_identity_test_insert")} ON {tableName} AFTER INSERT AS INSERT INTO {auditTableName} DEFAULT VALUES");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<TriggerIdentityRow, int>(_context);
            var row = new TriggerIdentityRow { Value = "trigger-safe" };
            var created = await gateway.CreateAsync(row);
            if (!created || row.Id <= 0)
            {
                throw new Exception("SQL Server trigger-safe identity insert did not return an identity value");
            }

            CheckOk("SqlServer.TriggerIdentityReturn", "SQL Server identity return with enabled trigger: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {auditTableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("sqlserver_trigger_identity_test")]
    private class TriggerIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
