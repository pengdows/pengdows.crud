using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for strict eager initialization of DatabaseContext.
/// Verifies that both RW and RO connections are validated at construction time.
/// </summary>
public class DatabaseContextStrictInitTests
{
    [Fact]
    public void Constructor_RWConnectionFailure_ThrowsWithPhaseAndRole()
    {
        // Arrange - factory that always fails on open
        var factory = fakeDbFactory.CreateFailingFactory(SupportedDatabase.Sqlite, ConnectionFailureMode.FailOnOpen);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        // Act & Assert
        var ex = Assert.Throws<ConnectionFailedException>(() =>
            new DatabaseContext(config, factory));

        Assert.Equal("InitConnect", ex.Phase);
        Assert.Equal("ReadWrite", ex.Role);
    }

    [Fact]
    public void Constructor_RWConnectionFailure_PreservesInnerException()
    {
        // Arrange - factory with custom exception
        var innerException = new InvalidOperationException("Simulated provider failure");
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.Sqlite, ConnectionFailureMode.FailOnOpen, innerException);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
        };

        // Act & Assert
        var ex = Assert.Throws<ConnectionFailedException>(() =>
            new DatabaseContext(config, factory));

        Assert.NotNull(ex.InnerException);
        Assert.Same(innerException, ex.InnerException);
    }

    [Fact]
    public void Constructor_WithExplicitROConnectionString_FailingRO_ThrowsConnectionFailed()
    {
        // Arrange - factory that succeeds on first open (RW) but fails on second (RO)
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        // Pre-enqueue: first connection succeeds (for RW init), second fails (for RO validation)
        var successConn = new fakeDbConnection();
        successConn.EmulatedProduct = SupportedDatabase.PostgreSql;

        var failConn = new fakeDbConnection();
        failConn.EmulatedProduct = SupportedDatabase.PostgreSql;
        failConn.SetFailOnOpen();

        factory.Connections.Add(successConn);
        factory.Connections.Add(failConn);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=primary;Database=testdb",
            ReadOnlyConnectionString = "Host=replica;Database=testdb",
            DbMode = DbMode.Standard
        };

        // Act & Assert
        var ex = Assert.Throws<ConnectionFailedException>(() =>
            new DatabaseContext(config, factory));

        Assert.Equal("ReadOnlyValidation", ex.Phase);
        Assert.Equal("ReadOnly", ex.Role);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Constructor_WithDerivedROConnectionString_DoesNotTestROConnection()
    {
        // Arrange - factory that succeeds on first open but would fail on subsequent opens
        // No explicit ReadOnlyConnectionString, so RO is derived from RW and should NOT be tested
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            DbMode = DbMode.Standard
            // No ReadOnlyConnectionString - it's derived
        };

        // Act - should succeed because RO validation is skipped when RO is derived
        using var context = new DatabaseContext(config, factory);

        // Assert
        Assert.NotNull(context);
    }

    [Fact]
    public void Constructor_WithExplicitROConnectionString_ValidatesROConnection()
    {
        // Arrange - factory where all connections succeed
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=primary;Database=testdb",
            ReadOnlyConnectionString = "Host=replica;Database=testdb",
            DbMode = DbMode.Standard
        };

        // Act - should succeed because both RW and RO connections work
        using var context = new DatabaseContext(config, factory);

        // Assert
        Assert.NotNull(context);
    }
}
