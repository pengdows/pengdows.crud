using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SQLite normal session-settings mode. Each pragma is read once at creation; only a pragma whose value differs from
/// what pengdows requires is sent on each open. synchronous = NORMAL is required only when the database is in WAL.
/// </summary>
public class SqliteSessionSettingsDiffTests
{
    private const string SqliteConnectionString = "Data Source=test;EmulatedProduct=Sqlite";

    // query_only is always sent (the pool does not reset it); the other pragmas are sent only when they differ.
    [Fact]
    public void AllOtherPragmasCompliant_OnlyQueryOnlyIsSent()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "1");

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "PRAGMA query_only = OFF;" }, SessionStatements(factory));
    }

    [Fact]
    public void ForeignKeysDefaultOff_SendsForeignKeysOnBeforeQueryOnly()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "0", queryOnly: "0", journalMode: "wal", synchronous: "1");

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "PRAGMA foreign_keys = ON;\nPRAGMA query_only = OFF;" }, SessionStatements(factory));
    }

    [Fact]
    public void WalWithSynchronousFull_SendsSynchronousNormal()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "2");

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "PRAGMA query_only = OFF;\nPRAGMA synchronous = NORMAL;" }, SessionStatements(factory));
    }

    [Fact]
    public void RollbackJournal_SynchronousNotRequired_SendsOnlyQueryOnly()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "delete", synchronous: "2");

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "PRAGMA query_only = OFF;" }, SessionStatements(factory));
    }

    // Every connection the factory creates answers the pragma probes with the values the test names.
    private static fakeDbFactory FactoryReportingPragmas(string foreignKeys, string queryOnly, string journalMode,
        string synchronous)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        for (var i = 0; i < 8; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("foreign_keys", System.StringComparison.OrdinalIgnoreCase) ? foreignKeys
                    : commandText.Contains("query_only", System.StringComparison.OrdinalIgnoreCase) ? queryOnly
                    : commandText.Contains("journal_mode", System.StringComparison.OrdinalIgnoreCase) ? journalMode
                    : commandText.Contains("synchronous", System.StringComparison.OrdinalIgnoreCase) ? synchronous
                    : null,
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }

    private static System.Collections.Generic.List<string> SessionStatements(fakeDbFactory factory) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .Where(text => text.Contains("foreign_keys", System.StringComparison.Ordinal)
                           || text.Contains("query_only", System.StringComparison.Ordinal)
                           || text.Contains("synchronous", System.StringComparison.Ordinal))
            .ToList();
}
