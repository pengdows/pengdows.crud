using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SQL Server normal session-settings mode on a database at compatibility level 90 or above. The server's defaults are
/// read once at first connect. Each managed option whose default differs from its requirement is sent on each open;
/// a matching default sends nothing.
/// </summary>
public class SqlServerSessionSettingsDiffTests
{
    private const string SqlServerConnectionString = "Server=localhost;Database=test;EmulatedProduct=SqlServer";

    // @@OPTIONS bits for the options pengdows requires: QUOTED_IDENTIFIER, ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS and
    // CONCAT_NULL_YIELDS_NULL on, and NUMERIC_ROUNDABORT, NOCOUNT, XACT_ABORT and IMPLICIT_TRANSACTIONS off. This is
    // what a SQL Server connection reports by default.
    private const int DefaultOptionBits = 256 + 32 + 16 + 8 + 4096;

    private const int QuotedIdentifier = 256;
    private const int NoCount = 512;

    [Fact]
    public void ModernCompatibility_DefaultsMatchRequirements_SendsNoSessionStatements()
    {
        var factory = FactoryReportingDefaults(optionBits: DefaultOptionBits, isolationLevel: 2);

        using var context = new DatabaseContext(SqlServerConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Empty(SessionStatements(factory));
    }

    [Fact]
    public void ModernCompatibility_QuotedIdentifierDefaultOff_SendsOnlyThatOption()
    {
        var factory = FactoryReportingDefaults(optionBits: DefaultOptionBits & ~QuotedIdentifier, isolationLevel: 2);

        using var context = new DatabaseContext(SqlServerConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET QUOTED_IDENTIFIER ON;" }, SessionStatements(factory));
    }

    [Fact]
    public void ModernCompatibility_NoCountDefaultOn_SendsOnlyThatOption()
    {
        var factory = FactoryReportingDefaults(optionBits: DefaultOptionBits | NoCount, isolationLevel: 2);

        using var context = new DatabaseContext(SqlServerConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET NOCOUNT OFF;" }, SessionStatements(factory));
    }

    [Fact]
    public void ModernCompatibility_IsolationDefaultSerializable_SendsReadCommitted()
    {
        var factory = FactoryReportingDefaults(optionBits: DefaultOptionBits, isolationLevel: 4);

        using var context = new DatabaseContext(SqlServerConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;" }, SessionStatements(factory));
    }

    // Every connection the factory creates answers the probes: the compatibility level (modern), @@OPTIONS, and the
    // session's isolation level.
    private static fakeDbFactory FactoryReportingDefaults(int optionBits, int isolationLevel)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("compatibility_level", System.StringComparison.OrdinalIgnoreCase) ? 160
                    : commandText.Contains("@@OPTIONS", System.StringComparison.OrdinalIgnoreCase) ? optionBits
                    : commandText.Contains("transaction_isolation_level", System.StringComparison.OrdinalIgnoreCase) ? (short)isolationLevel
                    : null,
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }

    private static System.Collections.Generic.List<string> SessionStatements(fakeDbFactory factory) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.StartsWith("SET ", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
}
