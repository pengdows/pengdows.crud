using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Live on Aurora MySQL 3.10 (8.0.42) and Aurora PostgreSQL 17.9, the context reported MySql and
/// PostgreSql: detection found Aurora (aurora_version answers), but the dialect's product info then
/// re-inferred the type from the banner, which carries no Aurora marker ("8.0.42", "PostgreSQL 17.9
/// on aarch64..."), and overwrote it. The product the detection pass found has to survive.
/// </summary>
public class AuroraContextProductTests
{
    private static DatabaseContext Context(SupportedDatabase engine, string versionProbe, string auroraProbe,
        string auroraVersion, string serverVersion)
    {
        var factory = new fakeDbFactory(engine);
        foreach (var _ in Enumerable.Range(0, 4))
        {
            var connection = new fakeDbConnection { EmulatedProduct = engine };
            connection.SetServerVersion(serverVersion);
            connection.SetScalarResultForCommand(versionProbe, serverVersion);
            connection.SetScalarResultForCommand(auroraProbe, auroraVersion);
            factory.Connections.Add(connection);
        }

        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=aurora-cluster.example",
            DbMode = DbMode.Standard
        }, factory);
    }

    [Fact]
    public void AuroraMySql_ContextReportsAuroraMySql()
    {
        using var context = Context(SupportedDatabase.MySql, "SELECT VERSION()", DatabaseDetectionService.AuroraMySqlProbe,
            "3.10.3", "8.0.42");

        Assert.Equal(SupportedDatabase.AuroraMySql, context.Product);
    }

    [Fact]
    public void AuroraPostgreSql_ContextReportsAuroraPostgreSql()
    {
        using var context = Context(SupportedDatabase.PostgreSql, "SELECT version()", DatabaseDetectionService.AuroraPostgreSqlProbe,
            "17.9.3", "PostgreSQL 17.9 on aarch64-unknown-linux-gnu, compiled by aarch64-unknown-linux-gnu-gcc (GCC) 10.5.0, 64-bit");

        Assert.Equal(SupportedDatabase.AuroraPostgreSql, context.Product);
    }

    [Fact]
    public void PlainMySql_ContextStillReportsMySql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var context = new DatabaseContext("Data Source=mysql.example;EmulatedProduct=MySql", factory);

        Assert.Equal(SupportedDatabase.MySql, context.Product);
    }
}
