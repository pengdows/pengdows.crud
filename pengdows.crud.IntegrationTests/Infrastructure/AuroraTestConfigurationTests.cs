using testbed.Aurora;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public sealed class AuroraTestConfigurationTests
{
    [Fact]
    public void PostgreSql_FromEnvironment_BuildsIsolatedSchemaAndConnections()
    {
        var values = new Dictionary<string, string?>
        {
            ["AURORA_POSTGRES_HOST"] = "aurora-pg.example",
            ["AURORA_POSTGRES_PORT"] = "5432",
            ["AURORA_POSTGRES_DATABASE"] = "app",
            ["AURORA_POSTGRES_USER"] = "tester",
            ["AURORA_POSTGRES_PASSWORD"] = "secret",
            ["AURORA_POSTGRES_SSL_MODE"] = "Require"
        };

        var configuration = AuroraTestConfiguration.FromEnvironment(
            AuroraEngine.PostgreSql,
            values.GetValueOrDefault,
            () => 123456);

        Assert.Equal("aurora-pg.example", configuration.Host);
        Assert.Equal("aurora_pg_test_123456", configuration.TestNamespace);
        Assert.Contains("Database=app", configuration.AdminConnectionString);
        Assert.Contains("Search Path=aurora_pg_test_123456", configuration.TestConnectionString);
        Assert.Contains("SSL Mode=Require", configuration.TestConnectionString);
    }

    [Fact]
    public void MySql_FromEnvironment_BuildsIsolatedDatabaseAndConnections()
    {
        var values = new Dictionary<string, string?>
        {
            ["AURORA_MYSQL_HOST"] = "aurora-mysql.example",
            ["AURORA_MYSQL_PORT"] = "3306",
            ["AURORA_MYSQL_USER"] = "tester",
            ["AURORA_MYSQL_PASSWORD"] = "secret",
            ["AURORA_MYSQL_DATABASE"] = "app",
            ["AURORA_MYSQL_SSL_MODE"] = "Required"
        };

        var configuration = AuroraTestConfiguration.FromEnvironment(
            AuroraEngine.MySql,
            values.GetValueOrDefault,
            () => 123456);

        Assert.Equal("aurora_mysql_test_123456", configuration.TestNamespace);
        Assert.Contains("Database=app", configuration.AdminConnectionString);
        Assert.Contains("Database=aurora_mysql_test_123456", configuration.TestConnectionString);
        // Parsed rather than matched as text: MySqlConnector spells the key "SSL Mode" (REL-003).
        Assert.Equal(MySqlConnector.MySqlSslMode.Required,
            new MySqlConnector.MySqlConnectionStringBuilder(configuration.TestConnectionString).SslMode);
    }

    [Fact]
    public void FromEnvironment_ReportsMissingEngineSpecificValues()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AuroraTestConfiguration.FromEnvironment(
                AuroraEngine.MySql,
                _ => null,
                () => 1));

        Assert.Contains("AURORA_MYSQL_HOST", ex.Message);
        Assert.Contains("AURORA_MYSQL_PASSWORD", ex.Message);
    }
}
