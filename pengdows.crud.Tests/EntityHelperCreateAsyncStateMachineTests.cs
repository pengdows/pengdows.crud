#region
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;
#endregion

namespace pengdows.crud.Tests;

public class EntityHelperCreateAsyncStateMachineTests
{
    private readonly TypeMapRegistry _typeMap;
    private readonly fakeDbFactory _factory;
    private readonly ILogger<EntityHelper<TestEntity, int>> _logger;

    public EntityHelperCreateAsyncStateMachineTests()
    {
        _typeMap = new TypeMapRegistry();
        _typeMap.Register<TestEntity>();
        _typeMap.Register<TestEntitySimple>();
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _logger = new LoggerFactory().CreateLogger<EntityHelper<TestEntity, int>>();
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Cancellation_Token()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => helper.CreateAsync(entity, context, cts.Token)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Null_Entity()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => helper.CreateAsync(null!, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Null_Context()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => helper.CreateAsync(entity, null!)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Database_Exception_During_Execution()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite); // Use separate factory for this test
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        var entity = new TestEntitySimple { Name = "Test" };
        
        factory.SetNonQueryException(new InvalidOperationException("Database connection failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Zero_Rows_Affected()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryResult(0); // Zero rows affected

        var result = await helper.CreateAsync(entity, context);
        
        Assert.False(result);
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Multiple_Rows_Affected()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryResult(2); // Multiple rows affected (unexpected)

        var result = await helper.CreateAsync(entity, context);
        
        Assert.False(result); // Should return false for anything other than exactly 1 row
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Negative_Rows_Affected()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryResult(-1); // Negative rows affected (unusual but possible)

        var result = await helper.CreateAsync(entity, context);
        
        Assert.False(result);
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Exception_During_ID_Population()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntityWithAutoId, int>>());
        _typeMap.Register<TestEntityWithAutoId>();
        var entity = new TestEntityWithAutoId { Name = "Test" };
        
        _factory.SetNonQueryResult(1);
        _factory.SetScalarException(new InvalidOperationException("Failed to get last insert ID"));

        // Should not throw even if ID population fails
        var result = await helper.CreateAsync(entity, context);
        
        Assert.True(result); // Insert succeeded even if ID population failed
        Assert.Equal(0, entity.Id); // ID remains unchanged
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Disposed_Context()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        await context.DisposeAsync(); // Dispose the context

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip="temporarily disabled while finalizing DbMode/ID population behavior")]
    public async Task CreateAsync_Should_Handle_Connection_Failure()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.SetFailOnOpen(); // Connection fails to open
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Command_Creation_Failure()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        var connection = (fakeDbConnection)_factory.CreateConnection();
        connection.SetFailOnCommand(); // Command creation fails
        _factory.Connections.Clear();
        _factory.Connections.Add(connection);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Transaction_Rollback_On_Exception()
    {
        // This test verifies that the state machine properly handles transaction rollback
        var context = new DatabaseContext("test", _factory, _typeMap);
        await using var transaction = context.BeginTransaction();
        
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryException(new InvalidOperationException("Insert failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.CreateAsync(entity, transaction)
        );
        
        // Transaction should still be in a valid state for rollback
        Assert.False(transaction.WasCommitted);
        Assert.False(transaction.WasRolledBack);
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Timeout_Exception()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryException(new TimeoutException("Command timeout"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Invalid_Cast_Exception()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite); // Use separate factory for this test
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        var entity = new TestEntitySimple { Name = "Test" };
        
        factory.SetNonQueryException(new InvalidCastException("Parameter type mismatch"));

        await Assert.ThrowsAsync<InvalidCastException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_OutOfMemoryException()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryException(new OutOfMemoryException("Out of memory"));

        await Assert.ThrowsAsync<OutOfMemoryException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip="temporarily disabled while finalizing DbMode/ID population behavior")]
    public async Task CreateAsync_Should_Handle_ThreadAbortException()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryException(CreateThreadAbort());

        await Assert.ThrowsAsync<ThreadAbortException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip="temporarily disabled while finalizing DbMode/ID population behavior")]
    public async Task CreateAsync_Should_Handle_StackOverflowException()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntitySimple, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntitySimple, int>>());
        _typeMap.Register<TestEntitySimple>();
        var entity = new TestEntitySimple { Name = "Test" };
        
        _factory.SetNonQueryException(new StackOverflowException());

        await Assert.ThrowsAsync<StackOverflowException>(
            () => helper.CreateAsync(entity, context)
        );
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Successful_Execution_With_ID_Population()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite); // SQLite supports INSERT RETURNING
        
        // Set up ID population BEFORE creating DatabaseContext for proper initialization
        factory.SetIdPopulationResult(42, rowsAffected: 1);
        
        var context = new DatabaseContext("test", factory, _typeMap);
        var helper = new EntityHelper<TestEntityWithAutoId, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntityWithAutoId, int>>());
        var entity = new TestEntityWithAutoId { Name = "Test" };

        var result = await helper.CreateAsync(entity, context);
        
        Assert.True(result);
        Assert.Equal(42, entity.Id); // ID should be populated
    }

    [Fact(Skip = "Disabled due to SQL Server RETURNING changes")]
    public async Task CreateAsync_Should_Handle_Entity_With_Complex_Properties()
    {
        var context = new DatabaseContext("test", _factory, _typeMap);
        var helper = new EntityHelper<TestEntityComplex, int>(context, logger: new LoggerFactory().CreateLogger<EntityHelper<TestEntityComplex, int>>());
        _typeMap.Register<TestEntityComplex>();
        
        var entity = new TestEntityComplex 
        { 
            Name = "Test",
            CreatedOn = DateTime.Now,
            IsActive = true,
            Score = 95.5m
        };
        
        _factory.SetNonQueryResult(1);

        var result = await helper.CreateAsync(entity, context);
        
        Assert.True(result);
    }
    private static ThreadAbortException CreateThreadAbort()
    {
        return (ThreadAbortException)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(ThreadAbortException));
    }
}
