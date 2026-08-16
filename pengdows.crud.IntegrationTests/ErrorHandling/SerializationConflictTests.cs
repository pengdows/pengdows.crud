using System.Data;
using DuckDB.NET.Data;
using FirebirdSql.Data.FirebirdClient;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Live integration coverage for SerializationConflictException on DuckDB and Firebird —
/// deliberately self-contained (own connection/container lifecycle) rather than using the
/// shared IntegrationTestFixture, since these two providers need no Docker orchestration
/// (DuckDB is embedded/file-based) or can be validated against a single standalone container
/// without pulling in the other 10 providers' fixture setup.
/// </summary>
/// <remarks>
/// Provider coverage decisions for SerializationConflictException:
/// <list type="bullet">
/// <item><b>DuckDB</b> (this file): confirmed via a real concurrent-write conflict that
/// DuckDBException reports ErrorType == Transaction (NOT "Serialization" despite that enum
/// member's name) with message "TransactionContext Error: Conflict on update!".</item>
/// <item><b>Firebird</b> (this file): confirmed via both a reversed-lock-order two-connection
/// scenario AND a snapshot-read-then-conflicting-write scenario that Firebird cannot distinguish
/// a true deadlock from a serialization conflict — both produce SQLSTATE 40001 with message
/// "deadlock\nupdate conflicts with concurrent update...". Classified as
/// SerializationConflictException, matching the ambiguous-40001 precedent already used for Db2.</item>
/// <item><b>PostgreSql/CockroachDb/YugabyteDb</b>: SQLSTATE 40001 classification pre-dates this
/// session; not re-verified live here.</item>
/// <item><b>Oracle</b>: ORA-08177 classification pre-dates this session; not re-verified live
/// here.</item>
/// <item><b>Db2</b>: SQLSTATE 40001 classification pre-dates this session (shared with its own
/// deadlock ambiguity); not re-verified live here.</item>
/// <item><b>SqlServer</b>: error 3960 classification added this session (see
/// SqlServerTranslatorTests), unit-tested only — live verification requires
/// ALLOW_SNAPSHOT_ISOLATION to be turned on at the database level (off by default), deferred.</item>
/// <item><b>MySql/MariaDb/TiDb</b>: deliberately NOT given a SerializationConflictException
/// classification. InnoDB implements SERIALIZABLE via locking reads, not optimistic/snapshot
/// conflict detection — a "conflict" manifests as a genuine lock-wait-timeout (error 1205,
/// already CommandTimeoutException) or a real deadlock (error 1213, already DeadlockException).
/// There is no third, distinct SQLSTATE/error code for this engine — this is standard,
/// well-documented InnoDB behavior, not something requiring live re-verification.</item>
/// <item><b>Sqlite</b>: deliberately NOT given a SerializationConflictException classification.
/// SQLITE_BUSY/SQLITE_LOCKED (codes 5/6) are lock-contention/timeout semantics (a writer
/// couldn't acquire the lock within busy_timeout), not an after-the-fact optimistic conflict —
/// conceptually closer to CommandTimeoutException than SerializationConflictException. Also,
/// pengdows.crud's own SingleWriter/PoolGovernor architecture serializes writes at the app level
/// specifically to prevent this from being reachable through the public API in practice.</item>
/// </list>
/// </remarks>
public class SerializationConflictTests
{
    [Fact]
    public async Task DuckDb_ConcurrentConflictingWrite_ClassifiesAsSerializationConflictException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pengdows_serialization_probe_{Guid.NewGuid():N}.db");
        var factory = DuckDBClientFactory.Instance;

        try
        {
            await using (var setup = (System.Data.Common.DbConnection)factory.CreateConnection()!)
            {
                setup.ConnectionString = $"Data Source={path}";
                await setup.OpenAsync();
                await using var create = setup.CreateCommand();
                create.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY, val INTEGER)";
                await create.ExecuteNonQueryAsync();
                await using var insert = setup.CreateCommand();
                insert.CommandText = "INSERT INTO t VALUES (1, 100)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var conn1 = (System.Data.Common.DbConnection)factory.CreateConnection()!;
            conn1.ConnectionString = $"Data Source={path}";
            await conn1.OpenAsync();

            await using var conn2 = (System.Data.Common.DbConnection)factory.CreateConnection()!;
            conn2.ConnectionString = $"Data Source={path}";
            await conn2.OpenAsync();

            await using (var beginTx1 = conn1.CreateCommand())
            {
                beginTx1.CommandText = "BEGIN TRANSACTION";
                await beginTx1.ExecuteNonQueryAsync();
            }

            await using (var beginTx2 = conn2.CreateCommand())
            {
                beginTx2.CommandText = "BEGIN TRANSACTION";
                await beginTx2.ExecuteNonQueryAsync();
            }

            // Tx2 reads first, establishing its snapshot view, before tx1 commits a change.
            await using (var read2 = conn2.CreateCommand())
            {
                read2.CommandText = "SELECT val FROM t WHERE id = 1";
                await read2.ExecuteScalarAsync();
            }

            await using (var write1 = conn1.CreateCommand())
            {
                write1.CommandText = "UPDATE t SET val = 200 WHERE id = 1";
                await write1.ExecuteNonQueryAsync();
            }

            await using (var commit1 = conn1.CreateCommand())
            {
                commit1.CommandText = "COMMIT";
                await commit1.ExecuteNonQueryAsync();
            }

            var thrown = await Record.ExceptionAsync(async () =>
            {
                await using var write2 = conn2.CreateCommand();
                write2.CommandText = "UPDATE t SET val = 300 WHERE id = 1";
                await write2.ExecuteNonQueryAsync();
            });

            Assert.NotNull(thrown);

            var translator = new DuckDbExceptionTranslator();
            var translated = translator.Translate(SupportedDatabase.DuckDB, thrown!, DbOperationKind.Update);

            Assert.IsType<SerializationConflictException>(translated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [SkippableFact]
    public async Task Firebird_ConcurrentConflictingWrite_ClassifiesAsSerializationConflictException()
    {
        var containerName = $"pengdows_fb_serialization_probe_{Guid.NewGuid():N}";
        var hostPort = GetFreeTcpPort();

        var startInfo = new System.Diagnostics.ProcessStartInfo("docker",
            $"run -d --name {containerName} " +
            "-e FIREBIRD_DATABASE=testdb.fdb -e FIREBIRD_USER=SYSDBA " +
            "-e FIREBIRD_PASSWORD=mysecretpassword -e FIREBIRD_ROOT_PASSWORD=mysecretpassword " +
            $"-p {hostPort}:3050 firebirdsql/firebird:latest")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using (var startProcess = System.Diagnostics.Process.Start(startInfo)!)
        {
            await startProcess.WaitForExitAsync();
            if (startProcess.ExitCode != 0)
            {
                throw new Xunit.SkipException(
                    $"Could not start standalone Firebird container: {await startProcess.StandardError.ReadToEndAsync()}");
            }
        }

        try
        {
            var connStr = $"data source=localhost;port number={hostPort};" +
                           "initial catalog=/var/lib/firebird/data/testdb.fdb;" +
                           "user id=SYSDBA;password=mysecretpassword;character set=UTF8;pooling=false";
            var factory = FirebirdClientFactory.Instance;

            // Wait for the server to accept connections (own bounded retry, no shared fixture).
            var deadline = DateTime.UtcNow.AddSeconds(30);
            Exception? lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await using var probe = (FbConnection)factory.CreateConnection()!;
                    probe.ConnectionString = connStr;
                    await probe.OpenAsync();
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(1000);
                }
            }

            if (lastError != null)
            {
                throw new Xunit.SkipException($"Firebird did not become ready in time: {lastError.Message}");
            }

            await using (var setup = (FbConnection)factory.CreateConnection()!)
            {
                setup.ConnectionString = connStr;
                await setup.OpenAsync();
                await using var create = setup.CreateCommand();
                create.CommandText = "CREATE TABLE t (id INTEGER NOT NULL PRIMARY KEY, val INTEGER)";
                await create.ExecuteNonQueryAsync();
                await using var insert = setup.CreateCommand();
                insert.CommandText = "INSERT INTO t VALUES (1, 100)";
                await insert.ExecuteNonQueryAsync();
            }

            await using var conn1 = (FbConnection)factory.CreateConnection()!;
            conn1.ConnectionString = connStr;
            await conn1.OpenAsync();

            await using var conn2 = (FbConnection)factory.CreateConnection()!;
            conn2.ConnectionString = connStr;
            await conn2.OpenAsync();

            var tx1 = await conn1.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var tx2 = await conn2.BeginTransactionAsync(IsolationLevel.RepeatableRead);

            await using (var read2 = conn2.CreateCommand())
            {
                read2.Transaction = tx2;
                read2.CommandText = "SELECT val FROM t WHERE id = 1";
                await read2.ExecuteScalarAsync();
            }

            await using (var write1 = conn1.CreateCommand())
            {
                write1.Transaction = tx1;
                write1.CommandText = "UPDATE t SET val = 200 WHERE id = 1";
                await write1.ExecuteNonQueryAsync();
            }

            await tx1.CommitAsync();

            var thrown = await Record.ExceptionAsync(async () =>
            {
                await using var write2 = conn2.CreateCommand();
                write2.Transaction = tx2;
                write2.CommandText = "UPDATE t SET val = 300 WHERE id = 1";
                await write2.ExecuteNonQueryAsync();
            });

            Assert.NotNull(thrown);

            try { await tx2.RollbackAsync(); } catch { /* best-effort cleanup */ }

            var translator = new FirebirdExceptionTranslator();
            var translated = translator.Translate(SupportedDatabase.Firebird, thrown!, DbOperationKind.Update);

            Assert.IsType<SerializationConflictException>(translated);
        }
        finally
        {
            System.Diagnostics.Process.Start("docker", $"stop {containerName}")?.WaitForExit(15000);
            System.Diagnostics.Process.Start("docker", $"rm {containerName}")?.WaitForExit(15000);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
