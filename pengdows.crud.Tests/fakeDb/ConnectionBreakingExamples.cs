#region

using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.FakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests.FakeDb;

/// <summary>
/// Examples demonstrating how to use the enhanced fakeDb connection breaking functionality
/// </summary>
public class ConnectionBreakingExamples
{
    [Fact]
    public void Example_BasicConnectionFailure()
    {
        // Create a connection that fails on open
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen();
        
        // This will throw when trying to open
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Fact]
    public void Example_CustomExceptionType()
    {
        // Use a custom exception for more realistic testing
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        var timeoutException = new TimeoutException("Connection timed out after 30 seconds");
        connection.SetCustomFailureException(timeoutException);
        connection.SetFailOnOpen();
        
        var thrownException = Assert.Throws<TimeoutException>(() => connection.Open());
        Assert.Equal("Connection timed out after 30 seconds", thrownException.Message);
    }

    [Fact]
    public void Example_FailAfterMultipleOperations()
    {
        // Connection works for first 2 opens, then fails
        var factory = new FakeDbFactory(SupportedDatabase.SqlServer);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.SetFailAfterOpenCount(2);
        
        // First open - succeeds
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        connection.Close();
        
        // Second open - succeeds
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        connection.Close();
        
        // Third open - fails and marks connection as broken
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.Equal(ConnectionState.Broken, connection.State);
    }

    [Fact]
    public void Example_DatabaseContextWithFailingConnection()
    {
        // Use helper to create a failing database context
        using var context = ConnectionFailureHelper.CreateFailOnOpenContext(SupportedDatabase.MySql);
        
        // Any operation requiring a connection will fail
        Assert.Throws<InvalidOperationException>(() => 
            context.GetConnection(ExecutionType.Read));
    }

    [Fact]
    public async Task Example_EntityHelperWithConnectionFailures()
    {
        // Test how EntityHelper handles connection failures
        await using var context = ConnectionFailureHelper.CreateFailOnOpenContext();
        var helper = new EntityHelper<TestEntity, long>(context);

        // Operations will fail due to connection issues
        await Assert.ThrowsAsync<InvalidOperationException>(async () => 
            await helper.RetrieveOneAsync(1));
    }

    [Fact]
    public void Example_TransactionFailures()
    {
        // Connection that fails when trying to begin transactions
        using var context = ConnectionFailureHelper.CreateFailOnTransactionContext();
        
        Assert.Throws<InvalidOperationException>(() => 
            context.BeginTransaction());
    }

    [Fact]
    public async Task Example_CommandExecutionFailures()
    {
        // Connection works, but commands fail to execute
        var factory = new FakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        var command = (FakeDbCommand)connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.SetFailOnExecute(true, ConnectionFailureHelper.CommonExceptions.Timeout);
        
        // All execute operations will throw TimeoutException
        Assert.Throws<TimeoutException>(() => command.ExecuteNonQuery());
        Assert.Throws<TimeoutException>(() => command.ExecuteScalar());
        Assert.Throws<TimeoutException>(() => command.ExecuteReader());
    }

    [Fact]
    public void Example_BrokenConnectionScenario()
    {
        // Simulate a connection that gets broken during operations
        var factory = new FakeDbFactory(SupportedDatabase.Oracle);
        var connection = (FakeDbConnection)factory.CreateConnection();
        connection.Open();
        
        // Something causes the connection to break
        connection.BreakConnection();
        Assert.Equal(ConnectionState.Broken, connection.State);
        
        // Now all operations fail
        Assert.Throws<InvalidOperationException>(() => connection.CreateCommand());
        Assert.Throws<InvalidOperationException>(() => connection.BeginTransaction());
        Assert.Throws<InvalidOperationException>(() => connection.Open());
    }

    [Fact]
    public void Example_RecoverFromFailures()
    {
        // Show how to reset failure conditions
        var factory = new FakeDbFactory(SupportedDatabase.Firebird);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        // Set up multiple failure modes
        connection.SetFailOnOpen();
        connection.SetFailOnCommand();
        connection.SetFailOnBeginTransaction();
        
        // All operations fail
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        
        // Reset failure conditions
        connection.ResetFailureConditions();
        
        // Now operations work normally
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
        
        var command = connection.CreateCommand();
        Assert.NotNull(command);
        
        var transaction = connection.BeginTransaction();
        Assert.NotNull(transaction);
    }

    [Fact]
    public void Example_TestingConnectionRetryLogic()
    {
        // Simulate testing retry logic by having connection fail then succeed
        var factory = new FakeDbFactory(SupportedDatabase.CockroachDb);
        var connection = (FakeDbConnection)factory.CreateConnection();
        
        // First attempt fails
        connection.SetFailOnOpen();
        Assert.Throws<InvalidOperationException>(() => connection.Open());
        
        // Reset and second attempt succeeds
        connection.ResetFailureConditions();
        connection.Open();
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Example_UsingFactoryWithFailureModes()
    {
        // Create factory pre-configured with failure modes
        var factory = FakeDbFactory.CreateFailingFactory(
            SupportedDatabase.DuckDB, 
            ConnectionFailureMode.FailAfterCount,
            ConnectionFailureHelper.CommonExceptions.NetworkError,
            failAfterCount: 3);
        
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        
        // Connection will work for first 3 operations, then fail with NetworkError
        var conn1 = context.GetConnection(ExecutionType.Read);
        conn1.Open(); conn1.Close(); // 1st
        
        var conn2 = context.GetConnection(ExecutionType.Read);  
        conn2.Open(); conn2.Close(); // 2nd
        
        var conn3 = context.GetConnection(ExecutionType.Read);
        conn3.Open(); conn3.Close(); // 3rd
        
        // 4th connection should fail with NetworkError
        var networkException = Assert.Throws<InvalidOperationException>(() => 
        {
            var conn4 = context.GetConnection(ExecutionType.Read);
            conn4.Open();
        });
        Assert.Equal("Network error", networkException.Message);
    }
}

// Simple test entity for examples
[Table("test_entities")]
internal class TestEntity
{
    [Id]
    public long Id { get; set; }
    public string Name { get; set; } = "";
}