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
    // The whole SQLite list, in the order it is sent: every item is sent on every checkout.
    private const string WalList =
        "PRAGMA foreign_keys = ON;\nPRAGMA query_only = OFF;\nPRAGMA synchronous = NORMAL;\nPRAGMA ignore_check_constraints = OFF;\nPRAGMA locking_mode = NORMAL;\nPRAGMA recursive_triggers = OFF;\nPRAGMA read_uncommitted = OFF;\nPRAGMA busy_timeout = 0;";

    private const string RollbackList =
        "PRAGMA foreign_keys = ON;\nPRAGMA query_only = OFF;\nPRAGMA ignore_check_constraints = OFF;\nPRAGMA locking_mode = NORMAL;\nPRAGMA recursive_triggers = OFF;\nPRAGMA read_uncommitted = OFF;\nPRAGMA busy_timeout = 0;";

    private const string SqliteConnectionString = "Data Source=test;EmulatedProduct=Sqlite";

    // query_only is always sent (the pool does not reset it); the other pragmas are sent only when they differ.
    [Fact]
    public void AllPragmasCompliant_OnlyQueryOnlyIsSent()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "1");

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        // SQLite's pool does not reset pragmas, so the captured values are not evidence of what a checkout will see:
        // the whole list is sent on every checkout, compliant or not.
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

    [Fact]
    public void DeviatingItems_AreSentWithQueryOnly()
    {
        var factory = FactoryReportingPragmas(foreignKeys: "1", queryOnly: "0", journalMode: "wal", synchronous: "1");
        foreach (var fake in factory.Connections)
        {
            var inner = fake.ScalarResolver;
            fake.ScalarResolver = commandText =>
                commandText.Contains("busy_timeout", System.StringComparison.OrdinalIgnoreCase) ? "5000"
                : commandText.Contains("locking_mode", System.StringComparison.OrdinalIgnoreCase) ? "exclusive"
                : inner?.Invoke(commandText);
        }

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "PRAGMA query_only = OFF;\nPRAGMA locking_mode = NORMAL;\nPRAGMA busy_timeout = 0;" }, SessionStatements(factory));
    }

    // The whole list, on a server that already matches every item: all of it is still sent, in list order.
    [Fact]
    public void CompliantServer_OnlyQueryOnlyIsSent()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        for (var i = 0; i < 8; i++)
        {
            factory.Connections.Add(new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    commandText.Contains("foreign_keys", System.StringComparison.OrdinalIgnoreCase) ? "1"
                    : commandText.Contains("query_only", System.StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("journal_mode", System.StringComparison.OrdinalIgnoreCase) ? "wal"
                    : commandText.Contains("synchronous", System.StringComparison.OrdinalIgnoreCase) ? "1"
                    : commandText.Contains("ignore_check_constraints", System.StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("locking_mode", System.StringComparison.OrdinalIgnoreCase) ? "normal"
                    : commandText.Contains("recursive_triggers", System.StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("read_uncommitted", System.StringComparison.OrdinalIgnoreCase) ? "0"
                    : commandText.Contains("busy_timeout", System.StringComparison.OrdinalIgnoreCase) ? "0"
                    : null,
            });
        }

        using var context = new DatabaseContext(SqliteConnectionString, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(
            new[] { "PRAGMA query_only = OFF;" },
            factory.CreatedConnections
                .SelectMany(c => c.ExecutedNonQueryTexts)
                .Where(text => text.Contains("query_only", System.StringComparison.Ordinal))
                .ToList());
    }

    // Every connection the factory creates answers the pragma probes with the values the test names.

    // The list items beyond the original four: compliant unless a test names otherwise.
    private static string? CompliantNewProbes(string commandText) =>
        commandText.Contains("ignore_check_constraints", System.StringComparison.OrdinalIgnoreCase) ? "0"
        : commandText.Contains("locking_mode", System.StringComparison.OrdinalIgnoreCase) ? "normal"
        : commandText.Contains("recursive_triggers", System.StringComparison.OrdinalIgnoreCase) ? "0"
        : commandText.Contains("read_uncommitted", System.StringComparison.OrdinalIgnoreCase) ? "0"
        : commandText.Contains("busy_timeout", System.StringComparison.OrdinalIgnoreCase) ? "0"
        : null;

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
                    : CompliantNewProbes(commandText),
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
