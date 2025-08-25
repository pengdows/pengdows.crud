#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.FakeDb;

public class ConnectionFailureTests
{
    [Fact]
    public void FakeDbConnection_SetFailOnOpen_ThrowsOnOpen()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen();
        
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task FakeDbConnection_SetFailOnOpen_ThrowsOnOpenAsync()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen();
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void FakeDbConnection_SetCustomException_ThrowsCustomException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        var customException = new TimeoutException("Connection timeout");
        
        connection.SetFailOnOpen();
        connection.SetCustomFailureException(customException);
        
        var thrownException = Assert.Throws<TimeoutException>(() => connection.Open());
        Assert.Equal("Connection timeout", thrownException.Message);
    }

    [Fact]
    public void FakeDbConnection_SetFailAfterOpenCount_FailsAfterCount()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailAfterOpenCount(2);
        
        // First two opens should succeed
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        connection.Close();
        
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        connection.Close();
        
        // Third open should fail and set connection to broken
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.Equal(ConnectionState.Broken, connection.State);
    }

    [Fact]
    public void FakeDbConnection_BreakConnection_SetsToBrokenState()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        connection.BreakConnection();
        
        Assert.Equal(ConnectionState.Broken, connection.State);
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Fact]
    public void FakeDbConnection_BrokenConnection_CannotCreateCommand()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.BreakConnection();
        
        Assert.Throws<InvalidOperationException>(() => connection.CreateCommand());
    }

    [Fact]
    public void FakeDbConnection_BrokenConnection_CannotBeginTransaction()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.BreakConnection();
        
        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
    }

    [Fact]
    public void FakeDbConnection_SetFailOnCommand_ThrowsOnCreateCommand()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailOnCommand();
        connection.Open();
        
        Assert.Throws<InvalidOperationException>(() => connection.CreateCommand());
    }

    [Fact]
    public void FakeDbConnection_SetFailOnBeginTransaction_ThrowsOnBeginTransaction()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailOnBeginTransaction();
        connection.Open();
        
        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
    }

    [Fact]
    public void FakeDbConnection_ResetFailureConditions_ClearsAllFailures()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        // Set multiple failure conditions
        connection.SetFailOnOpen();
        connection.SetFailOnCommand();
        connection.SetFailOnBeginTransaction();
        connection.SetCustomFailureException(new TimeoutException("Test"));
        connection.SetFailAfterOpenCount(1);
        
        // Reset should clear all conditions
        connection.ResetFailureConditions();
        
        // All operations should now succeed
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        
        var command = connection.CreateCommand();
        Assert.NotNull(command);
        
        var transaction = connection.BeginTransaction();
        Assert.NotNull(transaction);
    }

    [Fact]
    public void FakeDbCommand_SetFailOnExecute_ThrowsOnExecuteNonQuery()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        var command = (FakeDbCommand)connection.CreateCommand();
        command.SetFailOnExecute();
        
        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
    }

    [Fact]
    public void FakeDbCommand_SetFailOnExecute_ThrowsOnExecuteScalar()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        var command = (FakeDbCommand)connection.CreateCommand();
        command.SetFailOnExecute();
        
        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
    }

    [Fact]
    public void FakeDbCommand_SetFailOnExecute_ThrowsOnExecuteReader()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        var command = (FakeDbCommand)connection.CreateCommand();
        command.SetFailOnExecute();
        
        Assert.Throws<InvalidOperationException>(() => command.ExecuteReader());
    }

    [Fact]
    public void FakeDbCommand_SetFailOnExecuteWithCustomException_ThrowsCustomException()
    {
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        var command = (FakeDbCommand)connection.CreateCommand();
        var customException = new TimeoutException("Command timeout");
        command.SetFailOnExecute(true, customException);
        
        var thrownException = Assert.Throws<TimeoutException>(() => command.ExecuteNonQuery());
        Assert.Equal("Command timeout", thrownException.Message);
    }

    [Fact]
    public void FakeDbFactory_CreateFailingFactory_CreatesConnectionsWithFailureMode()
    {
        var factory = FakeDbFactory.CreateFailingFactory(SupportedDatabase.Sqlite, ConnectionFailureMode.FailOnOpen);
        var connection = factory.CreateConnection();
        
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Theory]
    [InlineData(ConnectionFailureMode.FailOnOpen)]
    [InlineData(ConnectionFailureMode.FailOnCommand)]
    [InlineData(ConnectionFailureMode.FailOnTransaction)]
    public void FakeDbFactory_CreateFailingFactory_SupportsAllFailureModes(ConnectionFailureMode mode)
    {
        var factory = FakeDbFactory.CreateFailingFactory(SupportedDatabase.Sqlite, mode);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        switch (mode)
        {
            case ConnectionFailureMode.FailOnOpen:
                Assert.Throws<InvalidOperationException>(() => connection.Open());
                break;
            case ConnectionFailureMode.FailOnCommand:
                connection.Open();
                Assert.Throws<InvalidOperationException>(() => connection.CreateCommand());
                break;
            case ConnectionFailureMode.FailOnTransaction:
                connection.Open();
                Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
                break;
        }
    }
}