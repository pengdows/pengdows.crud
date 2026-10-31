using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PostgreSQL per-pool defaults: the reader's server defaults are captured on a reader connection, so a difference on
/// the replica is sent on the replica's connections only.
/// </summary>
public class PostgreSqlPerPoolDefaultsTests
{
    private const string WriterConnectionString = "Host=writer-host;Database=test;EmulatedProduct=PostgreSql";
    private const string ReplicaConnectionString = "Host=replica-host;Database=test;EmulatedProduct=PostgreSql";

    [Fact]
    public void ReplicaWithADifferentDefault_SendsTheDifferenceOnTheReplicaOnly()
    {
        // The primary already matches the requirements. The replica reports client_min_messages = notice.
        var factory = FactoryWithDefaults(replicaClientMinMessages: "notice");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = WriterConnectionString,
            ReadOnlyConnectionString = ReplicaConnectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
        };

        using var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Read);
        connection.Open();

        var replica = StatementsOn(factory, "replica-host");
        Assert.Single(replica);
        Assert.Contains("client_min_messages = warning", replica[0], System.StringComparison.Ordinal);
        Assert.Empty(StatementsOn(factory, "writer-host"));
    }

    private static System.Collections.Generic.List<string> StatementsOn(fakeDbFactory factory, string host) =>
        factory.CreatedConnections
            .Where(c => c.ConnectionString.Contains(host, System.StringComparison.Ordinal))
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Where(t => t.Contains("client_min_messages", System.StringComparison.Ordinal))
            .ToList();

    // Each connection answers the probes with the defaults of the host it is opened against.
    private static fakeDbFactory FactoryWithDefaults(string replicaClientMinMessages)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        for (var i = 0; i < 12; i++)
        {
            var connection = new fakeDbConnection();
            connection.ScalarResolver = commandText =>
            {
                var onReplica = connection.ConnectionString.Contains("replica-host", System.StringComparison.Ordinal);
                if (commandText.Contains("standard_conforming_strings", System.StringComparison.OrdinalIgnoreCase))
                {
                    return "on";
                }

                if (commandText.Contains("client_min_messages", System.StringComparison.OrdinalIgnoreCase))
                {
                    return onReplica ? replicaClientMinMessages : "warning";
                }

                if (commandText.Contains("default_transaction_read_only", System.StringComparison.OrdinalIgnoreCase))
                {
                    return "off";
                }

                return null;
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }
}
