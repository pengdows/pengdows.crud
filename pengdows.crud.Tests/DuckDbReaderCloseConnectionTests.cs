using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DuckDB.NET's DuckDBDataReader.Close (through 1.5.5) closes the connection before releasing the
/// reader's native objects when the reader was opened with CommandBehavior.CloseConnection. When that
/// connection is the file's last one, the database is closed while those objects are still alive.
/// CONFIRMED live (DuckDB.NET 1.3.2 and 1.5.5, native 1.5.5): concurrent reads and writes on one file
/// then failed intermittently with WAL-replay failures, checksum corruption, FATAL "database has been
/// invalidated" and native crashes; raw DuckDB.NET with CloseConnection failed 29/30 runs, with
/// CommandBehavior.Default 0/30. Fixed upstream in DuckDB.NET develop (f94d52b), unreleased. pengdows
/// therefore never asks DuckDB for CloseConnection; TrackedReader closes the connection itself after
/// disposing the reader and command.
/// </summary>
public sealed class DuckDbReaderCloseConnectionTests
{
    private static async Task<fakeDbConnection> ReadOnce(SupportedDatabase product, DbMode mode)
    {
        var factory = new fakeDbFactory(product);
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=file.db;EmulatedProduct={product}",
            DbMode = mode
        }, factory);

        await using (var sc = context.CreateSqlContainer("SELECT 1 AS probe_column"))
        await using (var reader = await sc.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
            }
        }

        return factory.CreatedConnections.Single(c => c.ExecutedReaderTexts.Contains("SELECT 1 AS probe_column"));
    }

    [Theory]
    [InlineData(DbMode.Best)]
    [InlineData(DbMode.Standard)]
    public async Task DuckDb_Read_DoesNotUseCloseConnection_ButStillClosesTheConnection(DbMode mode)
    {
        var connection = await ReadOnce(SupportedDatabase.DuckDB, mode);

        var behavior = connection.ExecutedReaderBehaviors.Single();
        Assert.Equal(CommandBehavior.Default, behavior & CommandBehavior.CloseConnection);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Sqlite_Read_StillUsesCloseConnection()
    {
        var connection = await ReadOnce(SupportedDatabase.Sqlite, DbMode.Best);

        Assert.Equal(CommandBehavior.CloseConnection, connection.ExecutedReaderBehaviors.Single() & CommandBehavior.CloseConnection);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    // The DuckDB.NET fix (f94d52b) is on develop and ships in the first release after 1.5.5, so the
    // workaround applies to DuckDB.NET 1.5.5 and older. DuckDB.NET stamps its package version as the
    // assembly version (1.5.5 is 1.5.5.0). A provider that isn't DuckDB.NET's own assembly (a test
    // double or a wrapper) is assumed affected: skipping CloseConnection costs nothing.
    [Theory]
    [InlineData("DuckDB.NET.Data", "1.3.2.0", true)]
    [InlineData("DuckDB.NET.Data", "1.5.5.0", true)]
    [InlineData("DuckDB.NET.Data", "1.5.5", true)]
    [InlineData("DuckDB.NET.Data", "1.5.6.0", false)]
    [InlineData("DuckDB.NET.Data", "1.6.0.0", false)]
    [InlineData("DuckDB.NET.Data", "2.0.0.0", false)]
    [InlineData("duckdb.net.data", "1.5.5.0", true)]
    [InlineData("pengdows.crud.fakeDb", "2.0.6.0", true)]
    [InlineData("DuckDB.NET.Data", null, true)]
    public void ProviderHasCloseConnectionBug_UpToDuckDbNet155(string assemblyName, string? version, bool expected)
    {
        var name = new System.Reflection.AssemblyName(assemblyName)
        {
            Version = version == null ? null : System.Version.Parse(version)
        };

        Assert.Equal(expected, pengdows.crud.dialects.DuckDbDialect.ProviderHasCloseConnectionBug(name));
    }
}
