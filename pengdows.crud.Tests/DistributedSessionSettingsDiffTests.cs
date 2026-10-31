using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TiDB's pessimistic-mode setting is managed like every other setting: read once per pool, and sent only when the
/// captured value differs. CockroachDB and YugabyteDB send no extra settings, as before.
/// </summary>
public class DistributedSessionSettingsDiffTests
{
    [Fact]
    public void TiDb_PessimisticDefaultOff_SendsPessimisticMode()
    {
        var factory = FactoryReporting(SupportedDatabase.TiDb, new()
        {
            ["tidb_pessimistic_txn_default"] = "0",
        });

        using var context = new DatabaseContext("Server=localhost;Database=test;EmulatedProduct=TiDb", factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.Equal(new[] { "SET tidb_pessimistic_txn_default = ON;" },
            SessionStatementsContaining(factory, "tidb_pessimistic_txn_default"));
    }

    // Every connection answers each probe with the value the test names; a probe the test does not name is unknown.
    private static fakeDbFactory FactoryReporting(SupportedDatabase database,
        System.Collections.Generic.Dictionary<string, string> values)
    {
        var factory = new fakeDbFactory(database) { ServerVersion = "16.0.0" };
        for (var i = 0; i < 12; i++)
        {
            var connection = new fakeDbConnection
            {
                ScalarResolver = commandText =>
                    values.FirstOrDefault(pair => commandText.Contains(pair.Key, System.StringComparison.OrdinalIgnoreCase))
                        is { Key: not null } match ? match.Value : null,
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }

    // The statements are sent as one batch, one per line; the lines that set the named pragma are the ones checked.
    private static System.Collections.Generic.List<string> SessionStatementsContaining(fakeDbFactory factory, string name) =>
        factory.CreatedConnections
            .SelectMany(connection => connection.ExecutedNonQueryTexts)
            .SelectMany(text => text.Split('\n'))
            .Where(line => line.Contains(name, System.StringComparison.Ordinal))
            .ToList();
}
