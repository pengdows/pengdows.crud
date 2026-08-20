using Microsoft.Data.Sqlite;

namespace pengdows.stormgate.Tests;

/// <summary>
/// Proves (or disproves) that <c>CommandBehavior.CloseConnection</c> — a standard, first-class
/// ADO.NET flag — correctly releases the StormGate permit when a reader created with it is
/// disposed. <c>PermitCommand.ExecuteDbDataReader</c>/<c>ExecuteDbDataReaderAsync</c> forward the
/// behavior straight to the real inner provider command, whose <c>Connection</c> is the real
/// inner <c>DbConnection</c> — not the <c>PermitConnection</c> wrapper. If closing that real
/// connection this way doesn't route back through <c>PermitConnection</c>'s own release path,
/// the StormGate permit leaks forever for any caller relying on this flag instead of an explicit
/// <c>using</c> on the gated connection (a legitimate, common ADO.NET/Dapper idiom).
/// <para>
/// Uses real <c>Microsoft.Data.Sqlite</c> (matching <c>DapperIntegrationTests.cs</c>) rather than a
/// mock, since the behavior under test is the real provider's own <c>CommandBehavior.CloseConnection</c>
/// semantics, not anything pengdows.stormgate implements itself.
/// </para>
/// </summary>
public sealed class CommandBehaviorCloseConnectionTests
{
    [Fact]
    public async Task ExecuteReader_WithCloseConnectionBehavior_ReleasesStormGatePermitWhenReaderDisposed()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"stormgate-closeconn-{Guid.NewGuid():N}.db");

        try
        {
            await using var gate = StormGate.Create(
                SqliteFactory.Instance,
                $"Data Source={databasePath}",
                maxConcurrentOpens: 1,
                acquireTimeout: TimeSpan.FromMilliseconds(200));

            var connection = await gate.OpenAsync();

            await using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY)";
                await createCmd.ExecuteNonQueryAsync();
            }

            // Deliberately never dispose `connection` via an explicit using/await-using block —
            // this proves whether CommandBehavior.CloseConnection ALONE is enough to release the
            // permit, without relying on the outer connection's own Dispose ever running.
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM t";
                using var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
            }

            // Real ADO.NET CommandBehavior.CloseConnection semantics: the reader's disposal closes
            // the command's connection. Confirms the real inner connection is genuinely closed,
            // not just that our own wrapper thinks it should be.
            Assert.Equal(ConnectionState.Closed, connection.State);

            // With maxConcurrentOpens: 1, this second gated open can only succeed if the permit
            // held by `connection` was actually released above.
            await using var nextConnection = await gate.OpenAsync();
            Assert.Equal(ConnectionState.Open, nextConnection.State);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
