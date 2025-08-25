#region

using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.FakeDb;

public class ConnectionFailureHelperTests
{
    [Fact]
    public void CreateFailOnOpenContext_ThrowsOnConnectionOpen()
    {
        using var context = ConnectionFailureHelper.CreateFailOnOpenContext();
        
        Assert.Throws<InvalidOperationException>(() => 
            context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateFailOnCommandContext_ThrowsOnCommandCreation()
    {
        await using var context = ConnectionFailureHelper.CreateFailOnCommandContext();
        await using var container = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteScalarAsync<int>());
    }

    [Fact]
    public void CreateFailOnTransactionContext_ThrowsOnBeginTransaction()
    {
        using var context = ConnectionFailureHelper.CreateFailOnTransactionContext();
        
        Assert.Throws<InvalidOperationException>(() => 
            context.BeginTransaction());
    }

    [Fact]
    public void CreateFailAfterCountContext_FailsAfterSpecifiedCount()
    {
        using var context = ConnectionFailureHelper.CreateFailAfterCountContext(1);
        
        // First connection should succeed
        using var conn1 = context.GetConnection(ExecutionType.Read);
        conn1.Open();
        Assert.Equal(ConnectionState.Open, conn1.State);
        conn1.Close();
        
        // Second connection should fail
        Assert.Throws<InvalidOperationException>(() => 
        {
            using var conn2 = context.GetConnection(ExecutionType.Read);
            conn2.Open();
        });
    }

    [Fact]
    public void CreateBrokenConnectionContext_HasBrokenConnection()
    {
        using var context = ConnectionFailureHelper.CreateBrokenConnectionContext();
        
        using var conn = context.GetConnection(ExecutionType.Read);
        Assert.Equal(ConnectionState.Broken, conn.State);
        
        Assert.Throws<InvalidOperationException>(() => conn.Open());
    }

    [Fact]
    public void CreateFailOnOpenContext_WithCustomException_ThrowsCustomException()
    {
        var customException = new TimeoutException("Custom timeout");
        using var context = ConnectionFailureHelper.CreateFailOnOpenContext(customException: customException);
        
        var thrownException = Assert.Throws<TimeoutException>(() => 
            context.GetConnection(ExecutionType.Read));
        Assert.Equal("Custom timeout", thrownException.Message);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Oracle)]
    public void CreateFailOnOpenContext_WorksWithAllDatabases(SupportedDatabase database)
    {
        using var context = ConnectionFailureHelper.CreateFailOnOpenContext(database);
        
        Assert.Throws<InvalidOperationException>(() => 
            context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public void ConfigureConnectionFailure_ConfiguresExistingConnection()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        ConnectionFailureHelper.ConfigureConnectionFailure(
            connection, 
            ConnectionFailureMode.FailOnOpen);
        
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Fact]
    public void ConfigureConnectionFailure_WithCustomException_UsesCustomException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        var customException = new UnauthorizedAccessException("Access denied");
        
        ConnectionFailureHelper.ConfigureConnectionFailure(
            connection, 
            ConnectionFailureMode.FailOnOpen,
            customException);
        
        var thrownException = Assert.Throws<UnauthorizedAccessException>(() => connection.Open());
        Assert.Equal("Access denied", thrownException.Message);
    }

    [Fact]
    public void ConfigureConnectionFailure_WithFailAfterCount_ConfiguresCorrectly()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        ConnectionFailureHelper.ConfigureConnectionFailure(
            connection, 
            ConnectionFailureMode.FailAfterCount,
            failAfterCount: 2);
        
        // First two opens should succeed
        connection.Open();
        connection.Close();
        connection.Open();
        connection.Close();
        
        // Third should fail
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Fact]
    public void CommonExceptions_ProvidesVariousExceptionTypes()
    {
        Assert.IsType<TimeoutException>(ConnectionFailureHelper.CommonExceptions.Timeout);
        Assert.IsType<InvalidOperationException>(ConnectionFailureHelper.CommonExceptions.NetworkError);
        Assert.IsType<UnauthorizedAccessException>(ConnectionFailureHelper.CommonExceptions.AuthenticationError);
        Assert.IsType<InvalidOperationException>(ConnectionFailureHelper.CommonExceptions.DatabaseUnavailable);
        Assert.IsType<ArgumentException>(ConnectionFailureHelper.CommonExceptions.InvalidConnectionString);
    }

    [Fact]
    public void CommonExceptions_CreateDbException_CreatesDbException()
    {
        var dbException = ConnectionFailureHelper.CommonExceptions.CreateDbException("Database error");
        
        Assert.NotNull(dbException);
        Assert.Equal("Database error", dbException.Message);
        Assert.Equal(-1, dbException.ErrorCode);
    }

    [Fact]
    public void CreateFailOnOpenContext_CanBeUsedWithDatabaseContext()
    {
        using var context = ConnectionFailureHelper.CreateFailOnOpenContext();
        
        // Attempting to create a SQL container should fail when it tries to get a connection
        Assert.Throws<InvalidOperationException>(() => 
            context.CreateSqlContainer("SELECT 1"));
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateFailOnCommandContext_FailsOnSqlContainerOperations()
    {
        await using var context = ConnectionFailureHelper.CreateFailOnCommandContext();
        
        // Creating the container succeeds, but executing commands fails
        await using var container = context.CreateSqlContainer("SELECT 1");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.ExecuteScalarAsync<int>());
    }
}