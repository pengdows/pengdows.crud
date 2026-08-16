using System.Data.Common;
using System.Text.RegularExpressions;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

/// <summary>
/// Live integration coverage for the transient/connection exception categories that
/// ConstraintViolationTests does not exercise: connection failures today, with
/// CommandTimeoutException/DeadlockException/SerializationConflictException following in the
/// same file. Each test builds a SEPARATE, deliberately-broken IDatabaseContext (never touching
/// the shared per-provider context) so a real ADO.NET provider produces a real, unmocked
/// exception that must survive pengdows.crud's translation layer.
/// </summary>
/// <remarks>
/// IMPORTANT — two distinct "can't connect" exception types exist in pengdows.crud, and this
/// test exercises the FIRST one:
/// <list type="number">
/// <item><see cref="pengdows.crud.exceptions.ConnectionFailedException"/> — NOT a
/// <see cref="DatabaseException"/> subtype. <c>DatabaseContext</c>'s constructor always eagerly
/// opens a probe connection for product/capability detection (see
/// <c>DatabaseContext.InitializeInternals</c>); if that fails, it throws this directly,
/// bypassing <c>IDbExceptionTranslator</c> entirely. This is the exception a real caller gets
/// for the overwhelmingly common case: a wrong host/port/credential in a connection string a DI
/// container is building a context from. Confirmed live (this test) for all 12 providers.</item>
/// <item><see cref="ConnectionException"/> (a real <see cref="DatabaseException"/> subtype,
/// produced via <c>IDbExceptionTranslator</c>) — only reachable when a connection that WAS
/// already established later fails during a normal query/command execution (network drop,
/// server restart, connection killed mid-session), not during initial construction. Forcing
/// this live deterministically would require sabotaging a previously-healthy shared container
/// mid-test (stopping/pausing it), which risks breaking other tests sharing the same fixture —
/// so this category is validated at the unit level only (see e.g. OracleTranslatorTests,
/// FirebirdTranslatorTests, DuckDbTranslatorTests, Db2TranslatorTests — each has a dedicated
/// ConnectionException test using a constructed fake exception with the real driver's confirmed
/// error shape).</item>
/// </list>
/// </remarks>
[Collection("IntegrationTests")]
public class TransientErrorTests : DatabaseTestBase
{
    public TransientErrorTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    [SkippableFact]
    public async Task ConnectionFailure_UnreachableEndpoint_ThrowsConnectionFailedException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var ex = Assert.Throws<ConnectionFailedException>(() =>
            {
                using var badContext = BuildUnreachableContext(provider, context);
            });

            Assert.Equal("InitConnect", ex.Phase);
            Assert.NotNull(ex.InnerException);
        });
    }

    // =========================================================================
    // Per-provider "point at something that can't answer" context builders
    // =========================================================================

    private static IDatabaseContext BuildUnreachableContext(SupportedDatabase provider, IDatabaseContext context)
    {
        // Sqlite/DuckDB are embedded, file-based engines with no TCP concept — their closest
        // analog to a connection failure is a file-open failure against an unwritable path.
        if (provider == SupportedDatabase.Sqlite)
        {
            return new DatabaseContext("Data Source=/nonexistent_dir_pengdows_probe/db.sqlite",
                Microsoft.Data.Sqlite.SqliteFactory.Instance);
        }

        if (provider == SupportedDatabase.DuckDB)
        {
            return new DatabaseContext("Data Source=/nonexistent_dir_pengdows_probe/db.duckdb",
                DuckDB.NET.Data.DuckDBClientFactory.Instance);
        }

        // Every other provider is TCP-based: rewrite the real connection string's port to a
        // closed local port (nothing listening) — an immediate, deterministic ECONNREFUSED,
        // as opposed to a blackholed host which could instead manifest as a slow-connect
        // timeout and collide with CommandTimeoutException classification.
        var rawCs = (context as DatabaseContext)?.RawConnectionString ?? context.ConnectionString;

        return provider switch
        {
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb =>
                new DatabaseContext(WithBuilderKey(rawCs, "Port", "1"), Npgsql.NpgsqlFactory.Instance),

            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb =>
                new DatabaseContext(WithBuilderKey(rawCs, "Port", "1"),
                    MySql.Data.MySqlClient.MySqlClientFactory.Instance),

            SupportedDatabase.SqlServer =>
                new DatabaseContext(ReplacePortInDataSource(rawCs, "Data Source"),
                    Microsoft.Data.SqlClient.SqlClientFactory.Instance),

            SupportedDatabase.Oracle =>
                new DatabaseContext(ReplaceOracleTnsPort(rawCs),
                    Oracle.ManagedDataAccess.Client.OracleClientFactory.Instance),

            SupportedDatabase.Firebird =>
                new DatabaseContext(WithBuilderKey(rawCs, "Port Number", "1"),
                    FirebirdSql.Data.FirebirdClient.FirebirdClientFactory.Instance),

            SupportedDatabase.Db2 => BuildUnreachableDb2Context(rawCs),

            _ => throw new NotSupportedException(
                $"No unreachable-endpoint builder defined for {provider}.")
        };
    }

    private static IDatabaseContext BuildUnreachableDb2Context(string rawCs)
    {
        // No native-library bootstrap needed here: this test only ever runs after the fixture
        // has already constructed a real Db2 context for this test class's provider list (Db2
        // is always in GetSupportedProviders() by default), which registers
        // NativeLibrary.SetDllImportResolver process-wide as a side effect. Registering again
        // here would throw InvalidOperationException ("a resolver is already set").
        var rewritten = Regex.Replace(rawCs, @"(?<=[Ss]erver=[^;]*:)\d+", "1");
        return new DatabaseContext(rewritten, IBM.Data.Db2.DB2Factory.Instance);
    }

    private static string WithBuilderKey(string connectionString, string key, string value)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        builder[key] = value;
        return builder.ConnectionString;
    }

    private static string ReplacePortInDataSource(string connectionString, string dataSourceKey)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (builder.TryGetValue(dataSourceKey, out var value) && value is string dataSource)
        {
            builder[dataSourceKey] = Regex.Replace(dataSource, @",\d+", ",1");
        }

        return builder.ConnectionString;
    }

    private static string ReplaceOracleTnsPort(string connectionString)
    {
        // Oracle's TNS descriptor is embedded as a single opaque "Data Source" value —
        // DbConnectionStringBuilder can't parse inside it, so rewrite the PORT=nnnn token
        // directly via regex.
        return Regex.Replace(connectionString, @"(?i)PORT=\d+", "PORT=1");
    }
}
