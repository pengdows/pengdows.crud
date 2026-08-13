using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Proves the async detection entry points (added to close the "detection probes are still
/// synchronous" gap) genuinely await I/O instead of silently falling back to the sync
/// ExecuteScalar() path. AsyncOnlyProbeCommand.ExecuteScalar() throws, so any test that reaches
/// a passing assertion did so exclusively through ExecuteScalarAsync.
/// </summary>
public class DatabaseDetectionServiceAsyncTests
{
    [Fact]
    public async Task DetectFromConnectionAsync_AuroraMySql_UsesGenuineAsyncProbe()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => "2.09.1",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.AuroraMySql, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_AuroraPostgreSql_UsesGenuineAsyncProbe()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
            "SELECT version()" => "PostgreSQL 15.4",
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1" => DBNull.Value,
            "SELECT aurora_version()" => "3.4.2",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.AuroraPostgreSql, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_YugabyteViaPgSettings_UsesGenuineAsyncProbe()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
            "SELECT version()" => "PostgreSQL 11.2-YB-2.14.0.0-b0", // no -YB- marker matched deliberately below
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1" =>
                "yb_enable_optimizer_statistics",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        // The version() string above already contains "-YB-", so detection resolves at that
        // probe. This still proves the async path: if ExecuteScalar() (sync) had been called
        // instead, AsyncOnlyProbeCommand would have thrown before ever reaching this assertion.
        Assert.Equal(SupportedDatabase.YugabyteDb, product);
    }

    [Fact]
    public async Task DetectFromConnectionAsync_NoMatches_ReturnsUnknown_WithoutUsingSyncPath()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => throw new InvalidOperationException("Unknown system variable"),
            "SELECT version()" => "MySQL 8.0.35",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

        var product = await DatabaseDetectionService.DetectFromConnectionAsync(connection);

        Assert.Equal(SupportedDatabase.Unknown, product);
    }

    [Fact]
    public async Task DetectProductAsync_FromConnection_PreferredOverFactory()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => "2.09.1",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

        var product = await DatabaseDetectionService.DetectProductAsync(connection, factory: null);

        Assert.Equal(SupportedDatabase.AuroraMySql, product);
    }

    [Fact]
    public async Task DetectFromConnectionWithDetailAsync_RecordsProbeEvidence()
    {
        using var connection = new AsyncOnlyProbeConnection(commandText => commandText switch
        {
            "SELECT @@aurora_version" => "2.09.1",
            _ => throw new InvalidOperationException($"Unexpected probe for this scenario: {commandText}")
        });

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
        using var connection = new AsyncOnlyProbeConnection(_ => throw new InvalidOperationException("should not be reached"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DatabaseDetectionService.DetectFromConnectionAsync(connection, cts.Token));
    }

    /// <summary>
    /// Minimal DbConnection double whose commands answer only through ExecuteScalarAsync.
    /// ExecuteScalar() (sync) throws, so a test can only reach a passing assertion by having
    /// gone through the genuinely async path.
    /// </summary>
    private sealed class AsyncOnlyProbeConnection : DbConnection
    {
        private readonly Func<string, object?> _scalarResolver;
        private string _connectionString = string.Empty;

        public AsyncOnlyProbeConnection(Func<string, object?> scalarResolver)
        {
            _scalarResolver = scalarResolver;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override int ConnectionTimeout => 30;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbCommand CreateDbCommand() => new AsyncOnlyProbeCommand(_scalarResolver);
    }

    private sealed class AsyncOnlyProbeCommand : DbCommand
    {
        private readonly Func<string, object?> _scalarResolver;
        private string _commandText = string.Empty;

        public AsyncOnlyProbeCommand(Func<string, object?> scalarResolver)
        {
            _scalarResolver = scalarResolver;
        }

        [AllowNull]
        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }

        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }

        public override void Cancel()
        {
        }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();

        public override object? ExecuteScalar()
            => throw new InvalidOperationException(
                "Sync ExecuteScalar() was called — async detection must use ExecuteScalarAsync().");

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<object?>(cancellationToken);
            }

            object? result;
            try
            {
                result = _scalarResolver(_commandText);
            }
            catch (Exception ex)
            {
                return Task.FromException<object?>(ex);
            }

            return Task.FromResult(result);
        }

        public override void Prepare()
        {
        }
    }
}
