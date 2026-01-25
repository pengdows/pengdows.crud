#region

using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.fakeDb;
using pengdows.crud.Tests.Logging;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

public class DialectLoggingTests
{
    [Fact]
    public void MariaDb_TryEnterReadOnly_Failure_IsLoggedDebug()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var dialect = new MariaDbDialect(new fakeDbFactory(SupportedDatabase.MariaDb),
            lf.CreateLogger<MariaDbDialect>());

        using var failing = ConnectionFailureHelper.CreateFailOnCommandContext(SupportedDatabase.MariaDb);
        using var tx = failing.BeginTransaction(IsolationLevel.ReadCommitted);

        // Act: force failure inside TryEnterReadOnlyTransaction
        dialect.TryEnterReadOnlyTransaction(tx);

        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Debug && e.Message.Contains("Failed to apply MariaDB read-only session settings"));
    }

    private sealed class FakeOracleConnection : IDbConnection
    {
        public int StatementCacheSize { get; set; } = 0;
        private string _connectionString = string.Empty;

        [AllowNull]
        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public int ConnectionTimeout => 0;
        public string Database => string.Empty;
        public ConnectionState State => ConnectionState.Closed;
        public string DataSource => string.Empty;

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
        }

        public IDbTransaction BeginTransaction()
        {
            return null!;
        }

        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            return null!;
        }

        public void Open()
        {
        }

        public void Dispose()
        {
        }

        public IDbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }
    }

    [Fact]
    public void Oracle_ApplyConnectionSettings_LogsApplied()
    {
        var provider = new ListLoggerProvider();
        using var lf = new LoggerFactory(new[] { provider });
        var dialect = new OracleDialect(new fakeDbFactory(SupportedDatabase.Oracle), lf.CreateLogger<OracleDialect>());
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Oracle}",
            ProviderName = SupportedDatabase.Oracle.ToString()
        };
        var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Oracle), lf);

        var fake = new FakeOracleConnection();
        dialect.ApplyConnectionSettings(fake, ctx, false);

        Assert.Contains(provider.Entries,
            e => e.Level == LogLevel.Debug &&
                 e.Message.Contains("Applied Oracle connection settings: StatementCacheSize configured"));
    }
}