using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SQL Server per-pool defaults: the reader's server options are captured on the reader pool's own connection, so a
/// difference on a replica is sent on the replica's connections only.
/// </summary>
public class SqlServerPerPoolDefaultsTests
{
    private const string WriterConnectionString = "Server=writer-host;Database=test;EmulatedProduct=SqlServer";
    private const string ReplicaConnectionString = "Server=replica-host;Database=test;EmulatedProduct=SqlServer";

    // The default @@OPTIONS bits that match the requirements (see SqlServerSessionSettingsDiffTests).
    private const int DefaultOptionBits = 256 + 32 + 16 + 8 + 4096;
    private const int QuotedIdentifier = 256;

    [Fact]
    public void ReplicaWithADifferentDefault_SendsTheDifferenceOnTheReplicaOnly()
    {
        // The primary matches the requirements. The replica's QUOTED_IDENTIFIER default is off.
        var factory = FactoryWithDefaults(replicaOptionBits: DefaultOptionBits & ~QuotedIdentifier);
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

        Assert.Equal(new[] { "SET QUOTED_IDENTIFIER ON;" }, StatementsOn(factory, "replica-host"));
        Assert.Empty(StatementsOn(factory, "writer-host"));
    }

    private static System.Collections.Generic.List<string> StatementsOn(fakeDbFactory factory, string host) =>
        factory.CreatedConnections
            .Where(c => c.ConnectionString.Contains(host, System.StringComparison.Ordinal))
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Where(t => t.Contains("QUOTED_IDENTIFIER", System.StringComparison.Ordinal))
            .ToList();

    // Each connection answers the probes with the defaults of the host it is opened against.
    private static fakeDbFactory FactoryWithDefaults(int replicaOptionBits)
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        for (var i = 0; i < 12; i++)
        {
            var connection = new fakeDbConnection();
            connection.ScalarResolver = commandText =>
            {
                var onReplica = connection.ConnectionString.Contains("replica-host", System.StringComparison.Ordinal);
                if (commandText.Contains("compatibility_level", System.StringComparison.OrdinalIgnoreCase))
                {
                    return 160;
                }

                if (commandText.Contains("@@OPTIONS", System.StringComparison.OrdinalIgnoreCase))
                {
                    return onReplica ? replicaOptionBits : DefaultOptionBits;
                }

                if (commandText.Contains("transaction_isolation_level", System.StringComparison.OrdinalIgnoreCase))
                {
                    return (short)2;
                }

                return null;
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }
}
