using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review findings on the session-settings work: a failed probe is logged, the shipped
/// GetConnectionSessionSettings keeps returning the full enforcement SQL, and paranoid mode is covered on SQL Server
/// and TiDB as well as SQLite, MariaDB and PostgreSQL.
/// </summary>
public class SessionSettingsReviewFixesTests
{
    private const string SqlServerConnectionString = "Server=localhost;Database=test;EmulatedProduct=SqlServer";
    private const string TiDbConnectionString = "Server=localhost;Database=test;EmulatedProduct=TiDb";
    private const string MySqlConnectionString = "Server=localhost;Database=test;EmulatedProduct=MySql";
    private const string PostgresConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql";
    private const string SqliteConnectionString = "Data Source=test;EmulatedProduct=Sqlite";

    // @@OPTIONS bits for the defaults a SQL Server connection reports: QUOTED_IDENTIFIER, ANSI_NULLS, ANSI_PADDING,
    // ANSI_WARNINGS and CONCAT_NULL_YIELDS_NULL on, the rest off.
    private const int DefaultOptionBits = 256 + 32 + 16 + 8 + 4096;

    [Fact]
    public void PostgreSql_ProbeThatThrows_IsLoggedAsAWarning()
    {
        var logs = new RecordingLoggerFactory();
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                {
                    if (commandText.Contains("standard_conforming_strings", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("probe refused by the server");
                    }

                    return commandText.Contains("client_min_messages", StringComparison.OrdinalIgnoreCase) ? "warning"
                        : commandText.Contains("default_transaction_read_only", StringComparison.OrdinalIgnoreCase) ? "off"
                        : null;
                },
            });
        }

        using (var context = new DatabaseContext(new DatabaseContextConfiguration { ConnectionString = PostgresConnectionString, DbMode = DbMode.Standard }, factory, logs))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning
                                               && entry.Message.Contains("standard_conforming_strings", StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServer_GetConnectionSessionSettings_ModernCompliantServer_StaysEmptyAsBefore2_0_6()
    {
        var factory = SqlServerFactory(optionBits: DefaultOptionBits, isolationLevel: 2);

        using var context = new DatabaseContext(SqlServerConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.True(string.IsNullOrWhiteSpace(context.Dialect.GetConnectionSessionSettings(context, false)));
    }

    [Fact]
    public void PostgreSql_GetConnectionSessionSettings_CompliantServer_ReturnsEverySetting()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("standard_conforming_strings", StringComparison.OrdinalIgnoreCase) ? "on"
                    : commandText.Contains("client_min_messages", StringComparison.OrdinalIgnoreCase) ? "warning"
                    : commandText.Contains("default_transaction_read_only", StringComparison.OrdinalIgnoreCase) ? "off"
                    : null,
            });
        }

        using var context = new DatabaseContext(PostgresConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        var settings = context.Dialect.GetConnectionSessionSettings(context, false);

        Assert.Contains("SET standard_conforming_strings = on, client_min_messages = warning;", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlite_GetConnectionSessionSettings_CompliantServer_ReturnsEveryPragma()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("foreign_keys", StringComparison.OrdinalIgnoreCase) ? "1"
                    : commandText.Contains("query_only", StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("journal_mode", StringComparison.OrdinalIgnoreCase) ? "delete"
                    : commandText.Contains("synchronous", StringComparison.OrdinalIgnoreCase) ? "2"
                    : null,
            });
        }

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        var settings = context.Dialect.GetConnectionSessionSettings(context, false);

        Assert.Contains("PRAGMA foreign_keys = ON;", settings, StringComparison.Ordinal);
        Assert.Contains("PRAGMA query_only = OFF;", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_Paranoid_CompliantServer_SendsEveryOptionAndIsolationOnEveryCheckout()
    {
        var factory = SqlServerFactory(optionBits: DefaultOptionBits, isolationLevel: 2);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = SqlServerConnectionString,
            SessionSettingsMode = SessionSettingsMode.Paranoid,
        };

        using (var context = new DatabaseContext(config, factory))
        {
            for (var checkout = 0; checkout < 2; checkout++)
            {
                using var connection = context.GetConnection(ExecutionType.Write);
                connection.Open();
            }
        }

        var batches = SessionBatches(factory, "SET QUOTED_IDENTIFIER");
        Assert.Equal(2, batches.Count);
        foreach (var batch in batches)
        {
            Assert.Contains("SET QUOTED_IDENTIFIER ON;", batch, StringComparison.Ordinal);
            Assert.Contains("SET ANSI_NULLS ON;", batch, StringComparison.Ordinal);
            Assert.Contains("SET NUMERIC_ROUNDABORT OFF;", batch, StringComparison.Ordinal);
            Assert.Contains("SET NOCOUNT OFF;", batch, StringComparison.Ordinal);
            Assert.Contains("SET XACT_ABORT OFF;", batch, StringComparison.Ordinal);
            Assert.Contains("SET IMPLICIT_TRANSACTIONS OFF;", batch, StringComparison.Ordinal);
            Assert.Contains("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;", batch, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TiDb_Normal_CompliantServer_SendsNoPessimisticSetting()
    {
        var factory = TiDbFactory(pessimistic: "1", requiredSqlMode: RequiredTiDbSqlMode());

        using (var context = new DatabaseContext(TiDbConnectionString, factory))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Empty(SessionBatches(factory, "tidb_pessimistic_txn_default"));
    }

    [Fact]
    public void TiDb_Paranoid_CompliantServer_SendsThePessimisticDefaultOnEveryCheckout()
    {
        var factory = TiDbFactory(pessimistic: "1", requiredSqlMode: RequiredTiDbSqlMode());
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = TiDbConnectionString,
            SessionSettingsMode = SessionSettingsMode.Paranoid,
        };

        using (var context = new DatabaseContext(config, factory))
        {
            for (var checkout = 0; checkout < 2; checkout++)
            {
                using var connection = context.GetConnection(ExecutionType.Write);
                connection.Open();
            }
        }

        var batches = SessionBatches(factory, "tidb_pessimistic_txn_default");
        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch =>
            Assert.Contains("SET tidb_pessimistic_txn_default = ON;", batch, StringComparison.Ordinal));
    }

    [Fact]
    public void MySql_ServerNotCompliant_LogsTheSessionSettingsItSends()
    {
        var logs = new RecordingLoggerFactory();
        var factory = new fakeDbFactory(SupportedDatabase.MySql) { ServerVersion = "8.4.0" };
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("sql_mode", StringComparison.OrdinalIgnoreCase) ? string.Empty
                    : commandText.Contains("transaction_read_only", StringComparison.OrdinalIgnoreCase) ? "0"
                    : null,
            });
        }

        using (var context = new DatabaseContext(new DatabaseContextConfiguration { ConnectionString = MySqlConnectionString, DbMode = DbMode.Standard }, factory, logs))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Information
                                               && entry.Message.Contains("sql_mode", StringComparison.Ordinal));
    }

    // Every connection the factory creates answers the probes: the compatibility level (modern), @@OPTIONS, and the
    // session's isolation level.
    private static fakeDbFactory SqlServerFactory(int optionBits, int isolationLevel)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("compatibility_level", StringComparison.OrdinalIgnoreCase) ? 160
                    : commandText.Contains("@@OPTIONS", StringComparison.OrdinalIgnoreCase) ? optionBits
                    : commandText.Contains("transaction_isolation_level", StringComparison.OrdinalIgnoreCase)
                        ? (short)isolationLevel
                    : null,
            });
        }

        return factory;
    }

    private static fakeDbFactory TiDbFactory(string pessimistic, string requiredSqlMode)
    {
        var factory = new fakeDbFactory(SupportedDatabase.TiDb) { ServerVersion = "8.0.11-TiDB-v7.5.0" };
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("tidb_pessimistic_txn_default", StringComparison.OrdinalIgnoreCase) ? pessimistic
                    : commandText.Contains("sql_mode", StringComparison.OrdinalIgnoreCase) ? requiredSqlMode
                    : commandText.Contains("tx_read_only", StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("transaction_read_only", StringComparison.OrdinalIgnoreCase) ? "0"
                    : null,
            });
        }

        return factory;
    }

    // The requirement is whatever the context sends when the server disagrees with it; the test reads it from there.
    private static string RequiredTiDbSqlMode()
    {
        var probe = new fakeDbFactory(SupportedDatabase.TiDb) { ServerVersion = "8.0.11-TiDB-v7.5.0" };
        for (var i = 0; i < 8; i++)
        {
            probe.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("sql_mode", StringComparison.OrdinalIgnoreCase) ? "NOT_THE_REQUIREMENT"
                    : commandText.Contains("transaction_read_only", StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("tidb_pessimistic_txn_default", StringComparison.OrdinalIgnoreCase) ? "1"
                    : null,
            });
        }

        using (var context = new DatabaseContext(TiDbConnectionString, probe))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        var statement = SessionBatches(probe, "sql_mode").Single();
        var start = statement.IndexOf("sql_mode = '", StringComparison.Ordinal) + "sql_mode = '".Length;
        return statement.Substring(start, statement.IndexOf('\'', start) - start);
    }

    private static List<string> SessionBatches(fakeDbFactory factory, string marker) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.Contains(marker, StringComparison.Ordinal))
            .ToList();

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly object _lock = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private void Add(LogLevel level, string message)
        {
            lock (_lock)
            {
                _entries.Add((level, message));
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerFactory _owner;

            public RecordingLogger(RecordingLoggerFactory owner)
            {
                _owner = owner;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _owner.Add(logLevel, formatter(state, exception));
            }
        }
    }
}
