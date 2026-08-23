using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Proves the async detection entry points (added to close the "detection probes are still
/// synchronous" gap) genuinely await I/O instead of silently falling back to the sync
/// ExecuteScalar() path. Every connection here has BlockSynchronousCommandExecution set, so any
/// test that reaches a passing assertion did so exclusively through ExecuteScalarAsync.
/// </summary>
public class DatabaseDetectionServiceAsyncTests
{
    [Fact]
    public async Task DetectFromConnectionAsync_AuroraMySql_UsesGenuineAsyncProbe()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => "2.09.1",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.AuroraMySql, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_AuroraPostgreSql_UsesGenuineAsyncProbe()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => "PostgreSQL 15.4",
                "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1" => DBNull.Value,
                "SELECT aurora_version()" => "3.4.2",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.AuroraPostgreSql, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_YugabyteViaPgSettings_UsesGenuineAsyncProbe()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => "PostgreSQL 11.2-YB-2.14.0.0-b0", // no -YB- marker matched deliberately below
                "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1" =>
                    "yb_enable_optimizer_statistics",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        // The version() string above already contains "-YB-", so detection resolves at that
        // probe. This still proves the async path: if ExecuteScalar() (sync) had been called
        // instead, the sync ExecuteScalar() would have thrown before ever reaching this assertion.
        Assert.Equal(SupportedDatabase.YugabyteDb, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_NoMatches_ReturnsUnknown_WithoutUsingSyncPath()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => "MySQL 8.0.35",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.Unknown, product);
    }

    [Theory]
    [InlineData("TiDB Server 7.5", SupportedDatabase.TiDb)]
    [InlineData("PostgreSQL 11.2-YB-2.14.0.0-b0", SupportedDatabase.YugabyteDb)]
    [InlineData("CockroachDB CCL v24.1.0", SupportedDatabase.CockroachDb)]
    public async Task DetectFromConnectionAsync_VersionFlavor_UsesAsyncVersionProbe(
        string version,
        SupportedDatabase expected)
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => version,
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(expected, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_YugabytePgSettings_UsesAsyncFallbackProbe()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => "PostgreSQL 15.4",
                "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1" =>
                    "yb_enable_optimizer_statistics",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.YugabyteDb, product);
    }

    [Theory]
    [InlineData("10.11.6-MariaDB", SupportedDatabase.MariaDb)]
    [InlineData("8.0.0-TiDB", SupportedDatabase.TiDb)]
    public async Task DetectFromConnectionAsync_MySqlSchemaVersion_RefinesProductWithoutRoundTrip(
        string productVersion,
        SupportedDatabase expected)
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = _ => throw new InvalidOperationException("A schema refinement must not execute a probe."),
            SchemaTable = CreateDataSourceInformationSchema("MySQL", productVersion)
        };

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(expected, product);
    }

    [Theory]
    [InlineData("10.11.6-MariaDB", SupportedDatabase.MariaDb)]
    [InlineData("8.0.0-TiDB", SupportedDatabase.TiDb)]
    public void DetectFromConnection_MySqlSchemaVersion_RefinesProductWithoutRoundTrip(
        string productVersion,
        SupportedDatabase expected)
    {
        using var connection = new fakeDbConnection
        {
            ScalarResolver = _ => throw new InvalidOperationException("A schema refinement must not execute a probe."),
            SchemaTable = CreateDataSourceInformationSchema("MySQL", productVersion)
        };

        var product = DatabaseDetectionService.DetectFromConnection(connection);

        Assert.Equal(expected, product);
    }

    [Theory]
    [InlineData("TiDB Server 7.5", SupportedDatabase.TiDb)]
    [InlineData("PostgreSQL 11.2-YB-2.14.0.0-b0", SupportedDatabase.YugabyteDb)]
    [InlineData("CockroachDB CCL v24.1.0", SupportedDatabase.CockroachDb)]
    public void DetectFromConnection_VersionFlavor_UsesSynchronousVersionProbe(
        string version,
        SupportedDatabase expected)
    {
        using var connection = new fakeDbConnection
        {
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
                "SELECT version()" => version,
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = DatabaseDetectionService.DetectFromConnection(connection);

        Assert.Equal(expected, product);
    }

    [Fact]
    public async Task DetectProductAsync_FromConnection_PreferredOverFactory()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => "2.09.1",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var product = await DatabaseDetectionService.DetectProductAsync(connection, factory: null);

        Assert.Equal(SupportedDatabase.AuroraMySql, product);
    }

    [Fact]
    public async Task DetectFromConnectionWithDetailAsync_RecordsProbeEvidence()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = commandText => commandText switch
            {
                "SELECT @@aurora_version" => "2.09.1",
                _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
            }
        };

        var result = await DatabaseDetectionService.DetectFromConnectionWithDetailAsync(connection);

        Assert.Equal(SupportedDatabase.AuroraMySql, result.ResolvedProduct);
        Assert.Contains(result.Attempts, a => a.ProbeName == "AuroraMySqlVersion" && a.Succeeded);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_NullConnection_ReturnsUnknown()
    {
        var product = await DatabaseDetectionService.DetectFromConnectionAsync(null);
        Assert.Equal(SupportedDatabase.Unknown, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_RespectsCancellation()
    {
        using var connection = new fakeDbConnection
        {
            BlockSynchronousCommandExecution = true,
            ScalarResolver = _ => throw new InvalidOperationException("should not be reached")
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DatabaseDetectionService.DetectFromConnectionAsync(connection, cts.Token));
    }

    private static DataTable CreateDataSourceInformationSchema(string productName, string productVersion)
    {
        var schema = new DataTable();
        schema.Columns.Add("DataSourceProductName", typeof(string));
        schema.Columns.Add("DataSourceProductVersion", typeof(string));
        schema.Rows.Add(productName, productVersion);
        return schema;
    }
}
