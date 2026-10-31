using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PostgreSQL normal session-settings mode on a connection that sends its settings per checkout (a non-native data
/// source; a native data source bakes them into the startup options instead). The defaults are read once at first
/// connect, and only the settings that differ from the requirement are sent on each open.
/// </summary>
public class PostgreSqlSessionSettingsDiffTests
{
    private const string PostgresConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql";

    [Fact]
    public void WriteContext_DefaultsMatchRequirements_SendsNoSessionStatements()
    {
        var factory = FactoryReportingDefaults(standardConformingStrings: "on", clientMinMessages: "warning",
            defaultTransactionReadOnly: "off");

        using var context = new DatabaseContext(PostgresConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void ReadOnlyContext_OnlyTheReadOnlySettingDiffers_SendsOnlyThatSetting()
    {
        var factory = FactoryReportingDefaults(standardConformingStrings: "on", clientMinMessages: "warning",
            defaultTransactionReadOnly: "off");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = PostgresConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly,
        };

        using var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Read);
        connection.Open();

        Assert.Equal(new[] { "SET default_transaction_read_only = on;" }, SessionStatements(factory));
    }

    [Fact]
    public void WriteContext_OnlyClientMinMessagesDiffers_SendsOnlyThatSetting()
    {
        var factory = FactoryReportingDefaults(standardConformingStrings: "on", clientMinMessages: "notice",
            defaultTransactionReadOnly: "off");

        using var context = new DatabaseContext(PostgresConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET client_min_messages = warning;" }, SessionStatements(factory));
    }

    // Every connection the factory creates answers the three probes with the defaults the test names.
    private static fakeDbFactory FactoryReportingDefaults(string standardConformingStrings, string clientMinMessages,
        string defaultTransactionReadOnly)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("standard_conforming_strings", System.StringComparison.OrdinalIgnoreCase)
                        ? standardConformingStrings
                    : commandText.Contains("client_min_messages", System.StringComparison.OrdinalIgnoreCase)
                        ? clientMinMessages
                    : commandText.Contains("default_transaction_read_only", System.StringComparison.OrdinalIgnoreCase)
                        ? defaultTransactionReadOnly
                    : null,
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }

    private static System.Collections.Generic.List<string> SessionStatements(fakeDbFactory factory) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.Contains("standard_conforming_strings", System.StringComparison.Ordinal)
                           || text.Contains("client_min_messages", System.StringComparison.Ordinal)
                           || text.Contains("default_transaction_read_only", System.StringComparison.Ordinal))
            .ToList();
}
