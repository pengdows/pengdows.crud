using System.Linq;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// MariaDB normal session-settings mode. MariaDB names its read-only variable tx_read_only; the same diff rule applies:
/// the server's value is read once at first connect, and only a difference from the requirement is sent per open.
/// </summary>
public class MariaDbSessionSettingsDiffTests
{
    private const string MariaDbConnectionString = "Server=localhost;Database=test;EmulatedProduct=MariaDb";

    [Fact]
    public void ReadOnlyContext_DefaultAlreadyReadOnly_SendsNoReadOnlySwitch()
    {
        var required = RequiredSqlMode();
        var factory = FactoryReportingDefaults(sqlMode: required, txReadOnly: "1");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = MariaDbConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
        };

        using var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Read);
        connection.Open();

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void ReadOnlyContext_DefaultReadWrite_SendsOnlyTheMariaDbReadOnlySwitch()
    {
        var required = RequiredSqlMode();
        var factory = FactoryReportingDefaults(sqlMode: required, txReadOnly: "0");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = MariaDbConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
        };

        using var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Read);
        connection.Open();

        Assert.Equal(new[] { "SET SESSION tx_read_only = 1;" }, SessionStatements(factory));
    }

    [Fact]
    public void WriteContext_DefaultReadOnly_SendsOnlyTheMariaDbReadWriteReset()
    {
        var required = RequiredSqlMode();
        var factory = FactoryReportingDefaults(sqlMode: required, txReadOnly: "1");

        using var context = new DatabaseContext(MariaDbConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET SESSION tx_read_only = 0;" }, SessionStatements(factory));
    }

    // The requirement is whatever the context sends when the server disagrees with it; the test reads it from there.
    private static string RequiredSqlMode()
    {
        var probe = FactoryReportingDefaults(sqlMode: "NOT_THE_REQUIREMENT", txReadOnly: "0");
        using (var context = new DatabaseContext(MariaDbConnectionString, probe))
        {
            using var connection = context.GetConnection(ExecutionType.Write);
            connection.Open();
        }

        var statement = SessionStatements(probe).Single(text => text.Contains("sql_mode", System.StringComparison.Ordinal));
        return Regex.Match(statement, "sql_mode = '([^']*)'").Groups[1].Value;
    }

    // The fake answers the two probes MariaDB makes: sql_mode and tx_read_only. Its version is pinned so the requirement
    // is the same for the probe and the test.
    private static fakeDbFactory FactoryReportingDefaults(string sqlMode, string txReadOnly)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MariaDb) { ServerVersion = "11.4.0" };
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("sql_mode", System.StringComparison.OrdinalIgnoreCase) ? sqlMode
                    : commandText.Contains("tx_read_only", System.StringComparison.OrdinalIgnoreCase) ? txReadOnly
                    : null,
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }

    private static System.Collections.Generic.List<string> SessionStatements(fakeDbFactory factory) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.Contains("sql_mode", System.StringComparison.Ordinal)
                           || text.Contains("tx_read_only", System.StringComparison.Ordinal))
            .ToList();
}
