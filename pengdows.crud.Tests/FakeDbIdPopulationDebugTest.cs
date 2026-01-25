#region

using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class FakeDbIdPopulationDebugTest
{
    [Fact]
    public void FakeDb_Should_Return_Configured_Scalar_Values()
    {
        // Test the fakeDb directly to ensure our setup works
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, 1);

        var connection = factory.CreateConnection();
        connection.Open();

        // Test NonQuery (INSERT)
        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO test (name) VALUES ('test')";
        var rowsAffected = insertCmd.ExecuteNonQuery();
        Assert.Equal(1, rowsAffected);

        // Test Scalar for SCOPE_IDENTITY
        var scalarCmd = connection.CreateCommand();
        scalarCmd.CommandText = "SELECT SCOPE_IDENTITY()";
        var result = scalarCmd.ExecuteScalar();
        Assert.Equal(42, result);
    }

    [Fact]
    public void FakeDb_Should_Return_Configured_Insert_Returning_Values()
    {
        // Test INSERT RETURNING path (SQLite/PostgreSQL style)
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.SetIdPopulationResult(42, 1);

        var connection = factory.CreateConnection();
        connection.Open();

        // Test INSERT...RETURNING directly
        var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO test (name) VALUES ('test') RETURNING id";
        var result = cmd.ExecuteScalar();
        Assert.Equal(42, result);
    }

    [Fact]
    public void FakeDb_Should_Handle_Multiple_Scalar_Calls()
    {
        // Test that we can handle multiple scalar calls on the same connection
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        factory.SetIdPopulationResult(42, 1);

        var connection = factory.CreateConnection();
        connection.Open();

        // First call - might be used during initialization
        var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "SELECT @@VERSION";
        var version = versionCmd.ExecuteScalar();
        Assert.NotNull(version);

        // Second call - the ID query
        var idCmd = connection.CreateCommand();
        idCmd.CommandText = "SELECT SCOPE_IDENTITY()";
        var id = idCmd.ExecuteScalar();
        Assert.Equal(42, id);
    }
}