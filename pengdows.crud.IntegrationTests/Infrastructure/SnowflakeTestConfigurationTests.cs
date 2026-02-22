using testbed.Snowflake;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public class SnowflakeTestConfigurationTests
{
    [Fact]
    public void FromEnvironment_CreateDatabaseMode_UsesGeneratedDatabase()
    {
        var env = new Dictionary<string, string?>
        {
            ["SNOWFLAKE_ACCOUNT"] = "acct",
            ["SNOWFLAKE_USER"] = "user",
            ["SNOWFLAKE_PASSWORD"] = "pass",
            ["SNOWFLAKE_WAREHOUSE"] = "wh",
            ["SNOWFLAKE_DATABASE"] = "ADMIN_DB",
            ["SNOWFLAKE_SCHEMA"] = "TEST_SCHEMA",
            ["SNOWFLAKE_TEST_PREFIX"] = "PFX",
            ["SNOWFLAKE_CREATE_DATABASE"] = "true"
        };

        var config = SnowflakeTestConfiguration.FromEnvironment(env.GetValueOrDefault, () => 123);

        Assert.True(config.UseDatabaseIsolation);
        Assert.Equal("ADMIN_DB", config.AdminDatabase);
        Assert.Equal("PFX_123", config.TestDatabase);
        Assert.Equal("TEST_SCHEMA", config.TestSchema);
        Assert.Contains("db=PFX_123", config.TestConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("schema=TEST_SCHEMA", config.TestConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromEnvironment_SchemaMode_UsesGeneratedSchema()
    {
        var env = new Dictionary<string, string?>
        {
            ["SNOWFLAKE_ACCOUNT"] = "acct",
            ["SNOWFLAKE_USER"] = "user",
            ["SNOWFLAKE_PASSWORD"] = "pass",
            ["SNOWFLAKE_WAREHOUSE"] = "wh",
            ["SNOWFLAKE_DATABASE"] = "BASE_DB",
            ["SNOWFLAKE_TEST_PREFIX"] = "PFX"
        };

        var config = SnowflakeTestConfiguration.FromEnvironment(env.GetValueOrDefault, () => 456);

        Assert.False(config.UseDatabaseIsolation);
        Assert.Equal("BASE_DB", config.TestDatabase);
        Assert.Equal("PFX_456", config.TestSchema);
        Assert.Contains("db=BASE_DB", config.TestConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("schema=PFX_456", config.TestConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromEnvironment_SchemaMode_MissingDatabase_Throws()
    {
        var env = new Dictionary<string, string?>
        {
            ["SNOWFLAKE_ACCOUNT"] = "acct",
            ["SNOWFLAKE_USER"] = "user",
            ["SNOWFLAKE_PASSWORD"] = "pass",
            ["SNOWFLAKE_WAREHOUSE"] = "wh"
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => SnowflakeTestConfiguration.FromEnvironment(env.GetValueOrDefault, () => 1));

        Assert.Contains("SNOWFLAKE_DATABASE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
