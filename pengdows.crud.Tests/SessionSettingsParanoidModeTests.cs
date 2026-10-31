using System.Linq;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Paranoid session-settings mode (SESSION-002). Normal mode sends a managed setting only when the pool's captured
/// server value differs from the requirement; paranoid mode sends every managed setting that applies, on every
/// checkout, whatever the server reported, so a connection a caller poisoned is put back on its next checkout.
/// </summary>
public class SessionSettingsParanoidModeTests
{
    private const string SqliteConnectionString = "Data Source=test;EmulatedProduct=Sqlite";
    private const string MariaDbConnectionString = "Server=localhost;Database=test;EmulatedProduct=MariaDb";
    private const string PostgresConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql";

    [Fact]
    public void SessionSettingsMode_DefaultsToNull_MeaningNormal()
    {
        Assert.Null(new DatabaseContextConfiguration().SessionSettingsMode);
    }

    [Fact]
    public void Sqlite_Normal_CompliantServer_SendsOnlyQueryOnly()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "1");

        using (var context = new DatabaseContext(SqliteConnectionString, factory))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Equal(new[] { "PRAGMA query_only = OFF;" }, SessionStatements(factory));
    }

    [Fact]
    public void Sqlite_Paranoid_CompliantServer_SendsEveryPragmaOnEveryCheckout()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "1");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = SqliteConnectionString,
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

        var expected = "PRAGMA foreign_keys = ON;\nPRAGMA query_only = OFF;\nPRAGMA synchronous = NORMAL;";
        Assert.Equal(new[] { expected, expected }, SessionStatements(factory));
    }

    [Fact]
    public void Sqlite_Paranoid_RollbackJournal_DoesNotSendSynchronous()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "delete", synchronous: "2");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = SqliteConnectionString,
            SessionSettingsMode = SessionSettingsMode.Paranoid,
        };

        using (var context = new DatabaseContext(config, factory))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Equal(new[] { "PRAGMA foreign_keys = ON;\nPRAGMA query_only = OFF;" }, SessionStatements(factory));
    }

    [Fact]
    public void MariaDb_Normal_CompliantServer_SendsNothing()
    {
        var required = RequiredSqlMode();
        var factory = FactoryReportingMariaDbDefaults(sqlMode: required, txReadOnly: "0");

        using (var context = new DatabaseContext(MariaDbConnectionString, factory))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void MariaDb_Paranoid_CompliantServer_SendsSqlModeAndReadOnlyOnEveryCheckout()
    {
        var required = RequiredSqlMode();
        var factory = FactoryReportingMariaDbDefaults(sqlMode: required, txReadOnly: "0");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = MariaDbConnectionString,
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

        // One batch per checkout, each carrying both managed settings.
        var batches = SessionStatements(factory);
        Assert.Equal(2, batches.Count);
        foreach (var batch in batches)
        {
            Assert.Contains("sql_mode", batch, System.StringComparison.Ordinal);
            Assert.Contains("SET SESSION tx_read_only = 0;", batch, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PostgreSql_Normal_CompliantServer_SendsNothing()
    {
        var factory = FactoryReportingPostgresDefaults();

        using (var context = new DatabaseContext(PostgresConnectionString, factory))
        using (var connection = context.GetConnection(ExecutionType.Write))
        {
            connection.Open();
        }

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void PostgreSql_Paranoid_CompliantServer_SendsEverySettingOnEveryCheckout()
    {
        var factory = FactoryReportingPostgresDefaults();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = PostgresConnectionString,
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

        // One batch per checkout, each carrying all three managed settings.
        var batches = SessionStatements(factory);
        Assert.Equal(2, batches.Count);
        foreach (var batch in batches)
        {
            Assert.Contains("SET standard_conforming_strings = on;", batch, System.StringComparison.Ordinal);
            Assert.Contains("SET client_min_messages = warning;", batch, System.StringComparison.Ordinal);
            Assert.Contains("SET default_transaction_read_only = off;", batch, System.StringComparison.Ordinal);
        }
    }

    // The requirement is whatever the context sends when the server disagrees with it; the test reads it from there.
    private static string RequiredSqlMode()
    {
        var probe = FactoryReportingMariaDbDefaults(sqlMode: "NOT_THE_REQUIREMENT", txReadOnly: "0");
        using (var context = new DatabaseContext(MariaDbConnectionString, probe))
        {
            using var connection = context.GetConnection(ExecutionType.Write);
            connection.Open();
        }

        var statement = SessionStatements(probe).Single(text => text.Contains("sql_mode", System.StringComparison.Ordinal));
        return Regex.Match(statement, "sql_mode = '([^']*)'").Groups[1].Value;
    }

    private static fakeDbFactory FactoryReportingPragmas(string foreignKeys, string queryOnly, string journalMode,
        string synchronous)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("foreign_keys", System.StringComparison.OrdinalIgnoreCase) ? foreignKeys
                    : commandText.Contains("query_only", System.StringComparison.OrdinalIgnoreCase) ? queryOnly
                    : commandText.Contains("journal_mode", System.StringComparison.OrdinalIgnoreCase) ? journalMode
                    : commandText.Contains("synchronous", System.StringComparison.OrdinalIgnoreCase) ? synchronous
                    : null,
            });
        }

        return factory;
    }

    private static fakeDbFactory FactoryReportingMariaDbDefaults(string sqlMode, string txReadOnly)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb) { ServerVersion = "11.4.0" };
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("sql_mode", System.StringComparison.OrdinalIgnoreCase) ? sqlMode
                    : commandText.Contains("tx_read_only", System.StringComparison.OrdinalIgnoreCase) ? txReadOnly
                    : null,
            });
        }

        return factory;
    }

    // Every server default already matches what pengdows requires, so normal mode has nothing to send.
    private static fakeDbFactory FactoryReportingPostgresDefaults()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("standard_conforming_strings", System.StringComparison.OrdinalIgnoreCase) ? "on"
                    : commandText.Contains("client_min_messages", System.StringComparison.OrdinalIgnoreCase) ? "warning"
                    : commandText.Contains("default_transaction_read_only", System.StringComparison.OrdinalIgnoreCase) ? "off"
                    : null,
            });
        }

        return factory;
    }

    private static System.Collections.Generic.List<string> SessionStatements(fakeDbFactory factory) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.Contains("foreign_keys", System.StringComparison.Ordinal)
                           || text.Contains("query_only", System.StringComparison.Ordinal)
                           || text.Contains("synchronous", System.StringComparison.Ordinal)
                           || text.Contains("sql_mode", System.StringComparison.Ordinal)
                           || text.Contains("tx_read_only", System.StringComparison.Ordinal)
                           || text.Contains("standard_conforming_strings", System.StringComparison.Ordinal)
                           || text.Contains("client_min_messages", System.StringComparison.Ordinal)
                           || text.Contains("default_transaction_read_only", System.StringComparison.Ordinal))
            .ToList();
}
