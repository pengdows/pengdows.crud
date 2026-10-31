using System.Linq;
using System.Text.RegularExpressions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Each pool's defaults come from that pool's own server. A read replica can be configured differently from the primary,
/// so the reader's defaults are captured on a reader connection, and a difference on the reader is sent on the reader's
/// connections only.
/// </summary>
public class PerPoolSessionDefaultsTests
{
    private const string WriterConnectionString = "Server=writer-host;Database=test;EmulatedProduct=MySql";
    private const string ReplicaConnectionString = "Server=replica-host;Database=test;EmulatedProduct=MySql";

    [Fact]
    public void ReaderOnAReplicaWithDifferentDefaults_SendsTheReplicasDifferenceOnTheReplica()
    {
        var required = RequiredSqlMode();
        // The primary already matches the requirement. The replica's sql_mode lacks ANSI_QUOTES.
        var factory = FactoryWithDefaults(writerSqlMode: required,
            replicaSqlMode: "STRICT_ALL_TABLES,ONLY_FULL_GROUP_BY,NO_ENGINE_SUBSTITUTION");
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

        var replicaStatements = factory.CreatedConnections
            .Where(c => c.ConnectionString.Contains("replica-host", System.StringComparison.Ordinal))
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Where(t => t.Contains("sql_mode", System.StringComparison.Ordinal))
            .ToList();
        var writerStatements = factory.CreatedConnections
            .Where(c => c.ConnectionString.Contains("writer-host", System.StringComparison.Ordinal))
            .SelectMany(c => c.ExecutedNonQueryTexts)
            .Where(t => t.Contains("sql_mode", System.StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(replicaStatements);
        Assert.Empty(writerStatements);
    }

    // The requirement is what the context sends when the server disagrees with it, read from that statement.
    private static string RequiredSqlMode()
    {
        var probe = FactoryWithDefaults(writerSqlMode: "NOT_THE_REQUIREMENT", replicaSqlMode: "NOT_THE_REQUIREMENT");
        using (var context = new DatabaseContext(WriterConnectionString, probe))
        {
            using var connection = context.GetConnection(ExecutionType.Write);
            connection.Open();
        }

        var statement = probe.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts)
            .Single(t => t.Contains("sql_mode", System.StringComparison.Ordinal));
        return Regex.Match(statement, "sql_mode = '([^']*)'").Groups[1].Value;
    }

    // Each connection answers the sql_mode and transaction_read_only probes with the defaults of the endpoint it is
    // opened against.
    private static fakeDbFactory FactoryWithDefaults(string writerSqlMode, string replicaSqlMode)
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql) { ServerVersion = "8.4.0" };
        for (var i = 0; i < 12; i++)
        {
            var connection = new fakeDbConnection();
            connection.ScalarResolver = commandText =>
            {
                var onReplica = connection.ConnectionString.Contains("replica-host", System.StringComparison.Ordinal);
                if (commandText.Contains("sql_mode", System.StringComparison.OrdinalIgnoreCase))
                {
                    return onReplica ? replicaSqlMode : writerSqlMode;
                }

                if (commandText.Contains("transaction_read_only", System.StringComparison.OrdinalIgnoreCase))
                {
                    return "0";
                }

                return null;
            };
            factory.Connections.Add(connection);
        }

        return factory;
    }
}
