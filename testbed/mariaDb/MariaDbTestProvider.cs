using System.Data;
using MySqlConnector;
using pengdows.crud;
using pengdows.crud.attributes;

namespace testbed.mariaDb;

public sealed class MariaDbTestProvider : TestProvider
{
    public MariaDbTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    /// <summary>
    /// Raw-ADO.NET probe (bypasses pengdows.crud entirely): opens a transaction, inserts a row,
    /// then KILLs the owning connection from a second connection without ever calling Commit() or
    /// Rollback() on the first. Verifies via a third connection that the insert did not survive —
    /// proof that MariaDB itself rolls back an in-flight transaction when its connection dies,
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
                         "CREATE TABLE kill_probe (id INT PRIMARY KEY, val VARCHAR(50))"))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            MySqlConnection? connA = null;
            try
            {
                connA = new MySqlConnection(rawCs);
                await connA.OpenAsync();

                long connectionId;
                await using (var idCmd = connA.CreateCommand())
                {
                    idCmd.CommandText = "SELECT CONNECTION_ID()";
                    connectionId = Convert.ToInt64(await idCmd.ExecuteScalarAsync());
                }

                var txnA = await connA.BeginTransactionAsync();
                await using (var insertCmd = connA.CreateCommand())
                {
                    insertCmd.Transaction = txnA;
                    insertCmd.CommandText = "INSERT INTO kill_probe (id, val) VALUES (1, 'should-vanish')";
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // Never Commit()/Rollback() txnA — the server-side KILL below must be what undoes
                // this insert, not our own client-side cleanup.
                await using (var connB = new MySqlConnection(rawCs))
                {
                    await connB.OpenAsync();
                    await using var killCmd = connB.CreateCommand();
                    killCmd.CommandText = $"KILL CONNECTION {connectionId}";
                    await killCmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (connA != null)
                {
                    try { connA.Close(); } catch { /* connection already killed server-side */ }
                    try { await connA.DisposeAsync(); } catch { /* MySqlConnector can throw tearing down an already-killed connection's internal state */ }
                }
            }

            long count = -1;
            for (var i = 0; i < 20; i++)
            {
                await using var connC = new MySqlConnection(rawCs);
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
                CheckOk("MariaDb.TransactionRollbackOnKilledConnection",
                    "  [TransactionRollbackOnKilledConnection] KILLing the connection mid-transaction rolled back the uncommitted insert server-side: OK");
            }
            else
            {
                CheckFail("MariaDb.TransactionRollbackOnKilledConnection",
                    $"row survived a killed connection's uncommitted transaction (count={count}) — server did not roll back on connection death");
            }
        }
        finally
        {
            await using var cleanup = _context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        await TestUnsignedIdentityAsync<UIntIdentityRow, uint>(
            "mariadb_uint_identity_test",
            "INT UNSIGNED",
            2_147_483_648UL,
            () => new UIntIdentityRow { Value = "unsigned identity" },
            row => row.Id);
        await TestUnsignedIdentityAsync<ULongIdentityRow, ulong>(
            "mariadb_ulong_identity_test",
            "BIGINT UNSIGNED",
            9_223_372_036_854_775_808UL,
            () => new ULongIdentityRow { Value = "unsigned identity" },
            row => row.Id);
    }

    private async Task TestUnsignedIdentityAsync<TEntity, TId>(
        string table,
        string idType,
        ulong firstIdentity,
        Func<TEntity> createRow,
        Func<TEntity, TId> getId)
        where TEntity : class, new()
    {
        var tableName = _context.WrapObjectName(table);
        await using var container = _context.CreateSqlContainer();
        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{_context.WrapObjectName("id")} {idType} AUTO_INCREMENT PRIMARY KEY, ");
            container.Query.Append($"{_context.WrapObjectName("value")} VARCHAR(100) NOT NULL) ");
            container.Query.Append($"AUTO_INCREMENT={firstIdentity}");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<TEntity, TId>(_context);
            var row = createRow();
            var created = await gateway.CreateAsync(row);
            var id = getId(row);
            if (!created || Convert.ToUInt64(id) < firstIdentity)
            {
                throw new Exception($"MariaDB {idType} identity was not returned as {typeof(TId).Name}");
            }

            CheckOk($"MariaDb.UnsignedIdentity{typeof(TId).Name}", $"MariaDB {idType} identity returned as {typeof(TId).Name}: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("mariadb_uint_identity_test")]
    private class UIntIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.UInt32)]
        public uint Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }

    [Table("mariadb_ulong_identity_test")]
    private class ULongIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.UInt64)]
        public ulong Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
