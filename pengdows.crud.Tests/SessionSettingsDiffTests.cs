using System.Linq;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Normal session-settings mode: the server's defaults are read once per pool at first connect, compared with
/// what pengdows requires, and only the differences are sent on each open. A setting that already matches
/// its requirement sends nothing.
/// </summary>
public class SessionSettingsDiffTests
{
    private const string MySqlConnectionString = "Server=localhost;Database=test;EmulatedProduct=MySql";

    [Fact]
    public void WriteContext_DefaultsAlreadyMatchRequirements_SendsNoSessionStatements()
    {
        var required = RequiredMySqlSqlMode();
        var factory = FactoryReportingDefaults(sqlMode: required, transactionReadOnly: "0");

        using var context = new DatabaseContext(MySqlConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Empty(SessionStatements(factory));
    }

    // A server reports the same flags in its own order. The comparison is a set comparison, so reordering is not a
    // difference. Verified live: a MySQL 8.4 server started with the requirement reports it reordered.
    [Fact]
    public void WriteContext_DefaultsWithTheSameFlagsInAnotherOrder_SendsNoSessionStatements()
    {
        var required = RequiredMySqlSqlMode();
        var reversed = string.Join(",", required.Split(',').Reverse());
        var factory = FactoryReportingDefaults(sqlMode: reversed, transactionReadOnly: "0");

        using var context = new DatabaseContext(MySqlConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void ReadOnlyContext_OnlyTheReadOnlySwitchDiffers_SendsOnlyThatSwitch()
    {
        var required = RequiredMySqlSqlMode();
        var factory = FactoryReportingDefaults(sqlMode: required, transactionReadOnly: "0");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = MySqlConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
        };

        using var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Read);
        connection.Open();

        Assert.Equal(new[] { "SET SESSION transaction_read_only = 1;" }, SessionStatements(factory));
    }

    // The sql_mode value pengdows requires is whatever its first-open baseline sends today, so the test does not
    // restate the flag list. A probe context reports the statement; the test reads the value out of it.
    // The probe uses the same factory setup as the test, but reports a sql_mode that differs, so the context sends
    // its requirement. The requirement is then read from that statement.
    private static string RequiredMySqlSqlMode()
    {
        var probe = FactoryReportingDefaults(sqlMode: "NOT_THE_REQUIREMENT", transactionReadOnly: "0");
        using (var context = new DatabaseContext(MySqlConnectionString, probe))
        {
            using var connection = context.GetConnection(ExecutionType.Write);
            connection.Open();
        }

        var statement = SessionStatements(probe).Single(text => text.Contains("sql_mode", System.StringComparison.Ordinal));
        return Regex.Match(statement, "sql_mode = '([^']*)'").Groups[1].Value;
    }

    // Every connection the factory creates answers the probes for these two variables, so the context's
    // detection and any capture see the defaults the test sets.
    private static fakeDbFactory FactoryReportingDefaults(string sqlMode, string transactionReadOnly)
    {
        // A fixed server version keeps the requirement the same for the probe and the test: legacy sql_mode flags
        // depend on the version the context detects.
        var factory = new fakeDbFactory(SupportedDatabase.MySql) { ServerVersion = "8.4.0" };
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("sql_mode", System.StringComparison.OrdinalIgnoreCase) ? sqlMode
                    : commandText.Contains("transaction_read_only", System.StringComparison.OrdinalIgnoreCase) ? transactionReadOnly
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
                           || text.Contains("transaction_read_only", System.StringComparison.Ordinal))
            .ToList();
}
