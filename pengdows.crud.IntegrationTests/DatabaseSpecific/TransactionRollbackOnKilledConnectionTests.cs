using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Ported from testbed's PostgreSQLTestProvider/MariaDbTestProvider/SqlServerTestProvider
/// TestTransactionRollbackOnKilledConnection overrides (part of the testbed-vs-IntegrationTests
/// consolidation — see CLAUDE.md's "Adding a New Database" workflow notes). Each is a raw-ADO.NET
/// probe that deliberately bypasses pengdows.crud entirely: opens a transaction, inserts a row,
/// then kills/terminates the owning connection/session from a second connection without ever
/// calling Commit()/Rollback() on the first. Verifies via a third connection that the insert did
/// NOT survive — proof that the engine itself rolls back an in-flight transaction when its
/// connection dies, independent of anything the client does. Spanner is deliberately not
/// included here: its PostgreSQL interface (PGAdapter) doesn't implement
/// pg_backend_pid()/pg_terminate_backend() at all (verified live) — a genuine platform
/// limitation, not something to work around.
/// </summary>
[Collection("IntegrationTests")]
public class TransactionRollbackOnKilledConnectionTests : DatabaseTestBase
{
    public TransactionRollbackOnKilledConnectionTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        new[] { SupportedDatabase.PostgreSql, SupportedDatabase.MariaDb, SupportedDatabase.SqlServer }
            .Where(p => base.GetSupportedProviders().Contains(p));

    [SkippableFact]
    public async Task PostgreSql_TerminatingBackendMidTransaction_RollsBackUncommittedInsert()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            var rawCs = ((DatabaseContext)context).RawConnectionString;

            await using (var drop = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            await using (var create = context.CreateSqlContainer("CREATE TABLE kill_probe (id INT PRIMARY KEY, val TEXT)"))
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
                        try { connA.Close(); } catch { }
                        try { await connA.DisposeAsync(); } catch { }
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

                Assert.Equal(0, count);
            }
            finally
            {
                await using var cleanup = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
                await cleanup.ExecuteNonQueryAsync();
            }
        });
    }

    [SkippableFact]
    public async Task MariaDb_KillingConnectionMidTransaction_RollsBackUncommittedInsert()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.MariaDb, async context =>
        {
            var rawCs = ((DatabaseContext)context).RawConnectionString;

            await using (var drop = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            await using (var create = context.CreateSqlContainer("CREATE TABLE kill_probe (id INT PRIMARY KEY, val VARCHAR(50))"))
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
                        try { connA.Close(); } catch { }
                        try { await connA.DisposeAsync(); } catch { }
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

                Assert.Equal(0, count);
            }
            finally
            {
                await using var cleanup = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
                await cleanup.ExecuteNonQueryAsync();
            }
        });
    }

    [SkippableFact]
    public async Task SqlServer_KillingSessionMidTransaction_RollsBackUncommittedInsert()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async context =>
        {
            var rawCs = ((DatabaseContext)context).RawConnectionString;

            await using (var drop = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe"))
            {
                await drop.ExecuteNonQueryAsync();
            }

            await using (var create = context.CreateSqlContainer("CREATE TABLE kill_probe (id INT PRIMARY KEY, val NVARCHAR(50))"))
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
                        try { connA.Close(); } catch { }
                        try { connA.Dispose(); } catch { }
                    }
                }

                // SQL Server's KILL triggers rollback asynchronously; poll briefly.
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

                Assert.Equal(0, count);
            }
            finally
            {
                await using var cleanup = context.CreateSqlContainer("DROP TABLE IF EXISTS kill_probe");
                await cleanup.ExecuteNonQueryAsync();
            }
        });
    }
}
