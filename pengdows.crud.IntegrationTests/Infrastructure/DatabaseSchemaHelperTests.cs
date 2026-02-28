using pengdows.crud.enums;

namespace pengdows.crud.IntegrationTests.Infrastructure;

public sealed class DatabaseSchemaHelperTests
{
    [Fact]
    public void TryGetResetCommands_ReturnsSnowflakeSchemaResetCommands_WhenSchemaPresent()
    {
        var commands = DatabaseSchemaHelper.TryGetResetCommands(
            SupportedDatabase.Snowflake,
            "account=test;user=tester;db=PENGDOWS_TEST;schema=TEST_SCHEMA");

        Assert.NotNull(commands);
        Assert.Equal(
            [
                "DROP SCHEMA IF EXISTS \"TEST_SCHEMA\" CASCADE",
                "CREATE SCHEMA IF NOT EXISTS \"TEST_SCHEMA\"",
                "USE DATABASE \"PENGDOWS_TEST\"",
                "USE SCHEMA \"TEST_SCHEMA\""
            ],
            commands);
    }

    [Fact]
    public void TryGetResetCommands_ReturnsNull_WhenSnowflakeSchemaMissing()
    {
        var commands = DatabaseSchemaHelper.TryGetResetCommands(
            SupportedDatabase.Snowflake,
            "account=test;user=tester;db=PENGDOWS_TEST");

        Assert.Null(commands);
    }

    [Fact]
    public void TryGetResetCommands_ReturnsNull_ForNonSnowflakeProviders()
    {
        var commands = DatabaseSchemaHelper.TryGetResetCommands(
            SupportedDatabase.PostgreSql,
            "Host=localhost;Database=testdb;Search Path=public");

        Assert.Null(commands);
    }

    [Fact]
    public void TryGetResetCommands_EscapesSnowflakeSchemaNames()
    {
        var commands = DatabaseSchemaHelper.TryGetResetCommands(
            SupportedDatabase.Snowflake,
            "account=test;schema=TEST\"SCHEMA");

        Assert.NotNull(commands);
        Assert.Equal(
            [
                "DROP SCHEMA IF EXISTS \"TEST\"\"SCHEMA\" CASCADE",
                "CREATE SCHEMA IF NOT EXISTS \"TEST\"\"SCHEMA\"",
                "USE SCHEMA \"TEST\"\"SCHEMA\""
            ],
            commands);
    }
}
