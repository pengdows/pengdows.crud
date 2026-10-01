#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DatabaseContextIsolationTests
{
    [Theory]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Snapshot)]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.SqlServer, IsolationProfile.FastWithRisks, IsolationLevel.ReadUncommitted)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationProfile.SafeNonBlockingReads, IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationProfile.FastWithRisks, IsolationLevel.ReadCommitted)]
    [InlineData(SupportedDatabase.YugabyteDb, IsolationProfile.SafeNonBlockingReads, IsolationLevel.RepeatableRead)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.CockroachDb, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationProfile.SafeNonBlockingReads, IsolationLevel.Serializable)]
    [InlineData(SupportedDatabase.DuckDB, IsolationProfile.StrictConsistency, IsolationLevel.Serializable)]
    public void BeginTransaction_ResolvesIsolationLevel(SupportedDatabase product, IsolationProfile profile,
        IsolationLevel expected)
    {
        var factory = new fakeDbFactory(product.ToString());
        if (product == SupportedDatabase.SqlServer)
        {
            var connection = new fakeDbConnection();
            connection.SetScalarResultForCommand(
                "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()",
                1);
            connection.SetScalarResultForCommand(
                "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()",
                1);
            factory.Connections.Add(connection);
        }

        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);
        using var tx = context.BeginTransaction(profile);

        Assert.Equal(expected, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void BeginTransaction_SafeNonBlockingReads_UsesRepeatableReadForPostgresCompatibleDatabases(
        SupportedDatabase product)
    {
        // BP-209: PostgreSQL/YugabyteDB RepeatableRead is an MVCC snapshot, so the profile's
        // non-blocking, consistent-read guarantee is met - it used to throw.
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));
        using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public async Task BeginTransactionAsync_SafeNonBlockingReads_UsesRepeatableReadForPostgresCompatibleDatabases(
        SupportedDatabase product)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));
        await using var tx = await context.BeginTransactionAsync(IsolationProfile.SafeNonBlockingReads);
        Assert.Equal(IsolationLevel.RepeatableRead, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.Oracle)]
    public void BeginTransaction_ProfileRequiresRcsi_DoesNotThrowForOtherProviders(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product.ToString());
        if (product == SupportedDatabase.SqlServer)
        {
            var connection = new fakeDbConnection();
            connection.SetScalarResultForCommand(
                "SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()",
                1);
            connection.SetScalarResultForCommand(
                "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()",
                1);
            factory.Connections.Add(connection);
        }

        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}", factory);

        using var tx = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        Assert.NotNull(tx);
    }

    // CockroachDbDialect.GetSupportedIsolationLevels() returns only {Serializable}. This used to
    // be masked for the native IsolationLevel overload by a hardcoded silent upgrade in
    // TransactionContext's constructor (`if (context.Product == SupportedDatabase.CockroachDb)
    // isolationLevel = IsolationLevel.Serializable`) — removed when IsolationResolver's
    // per-database switches were replaced with dialect-owned data, so an unsupported native
    // level is now rejected like any other database's unsupported level instead of silently
    // substituted. Locks down that this is the actual, current, intentional behavior.
    [Fact]
    public void BeginTransaction_NativeIsolationLevel_CockroachDb_UnsupportedLevel_FailsUpToSerializable()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.CockroachDb}",
            new fakeDbFactory(SupportedDatabase.CockroachDb.ToString()));

        using var tx = context.BeginTransaction(IsolationLevel.ReadCommitted);
        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
    }

    [Fact]
    public void BeginTransaction_NativeIsolationLevel_CockroachDb_Serializable_Succeeds()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.CockroachDb}",
            new fakeDbFactory(SupportedDatabase.CockroachDb.ToString()));

        using var tx = context.BeginTransaction(IsolationLevel.Serializable);
        Assert.NotNull(tx);
    }

    [Fact]
    public void BeginTransaction_ProfileSupported_CockroachDb_And_DuckDB()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.CockroachDb}",
            new fakeDbFactory(SupportedDatabase.CockroachDb.ToString()));
        using var tx1 = context.BeginTransaction(IsolationProfile.FastWithRisks);
        Assert.NotNull(tx1);

        context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.DuckDB}",
            new fakeDbFactory(SupportedDatabase.DuckDB.ToString()));
        using var tx2 = context.BeginTransaction(IsolationProfile.FastWithRisks);
        Assert.NotNull(tx2);
    }

    [Fact]
    public void BeginTransaction_UnknownProduct_UsesSerializable()
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Unknown}",
            new fakeDbFactory(SupportedDatabase.Unknown.ToString()));

        using var tx = context.BeginTransaction(IsolationProfile.StrictConsistency);
        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.PostgreSql, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.MySql, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.Sqlite, IsolationLevel.Serializable, true)]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.Serializable, false)]
    [InlineData(SupportedDatabase.TiDb, IsolationLevel.ReadCommitted, true)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.Serializable, false)]
    [InlineData(SupportedDatabase.Snowflake, IsolationLevel.ReadCommitted, true)]
    public void GetSupportedIsolationLevels_ReflectsProviderCapabilities(
        SupportedDatabase product, IsolationLevel level, bool expectedSupported)
    {
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={product}",
            new fakeDbFactory(product.ToString()));

        var supported = context.GetSupportedIsolationLevels();

        Assert.Equal(expectedSupported, supported.Contains(level));
    }

    [Fact]
    public void BeginTransaction_StrictConsistency_NotDegradedOnSqlServer_DoesNotLogWarning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer.ToString());
        var loggerFactory = new RecordingLoggerFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory, loggerFactory);

        using var tx = context.BeginTransaction(IsolationProfile.StrictConsistency);

        Assert.Equal(IsolationLevel.Serializable, tx.IsolationLevel);
        Assert.DoesNotContain(loggerFactory.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("degraded", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(Entries);
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly List<(LogLevel Level, string Message)> _entries;

            public RecordingLogger(List<(LogLevel Level, string Message)> entries)
            {
                _entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}