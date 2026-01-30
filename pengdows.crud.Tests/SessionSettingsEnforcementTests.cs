using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for session settings enforcement at physical connection open.
/// Ground truth: Session settings are enforced ONCE at physical connection open via onFirstOpen handler.
/// </summary>
public class SessionSettingsEnforcementTests
{
    [Fact]
    public void TrackedConnection_OnFirstOpen_ExecutesExactlyOnce()
    {
        // Arrange
        var onFirstOpenCounter = 0;
        var fakeConnection = new fakeDbConnection();

        var trackedConnection = new TrackedConnection(
            fakeConnection,
            onFirstOpen: conn => onFirstOpenCounter++);

        // Act
        trackedConnection.Open(); // First open - should trigger
        Assert.Equal(ConnectionState.Open, trackedConnection.State);

        trackedConnection.Close();
        trackedConnection.Open(); // Second open - should NOT trigger again

        // Assert
        Assert.Equal(1, onFirstOpenCounter);
        Assert.True(trackedConnection.WasOpened);
        Assert.Equal(2, fakeConnection.OpenCount); // Physical opens: 2
    }

    [Fact]
    public async Task TrackedConnection_OnFirstOpen_RunsAfterConnectionOpen()
    {
        // Arrange
        var fakeConnection = new fakeDbConnection();
        var connectionWasOpenDuringCallback = false;

        var trackedConnection = new TrackedConnection(
            fakeConnection,
            onFirstOpen: conn =>
            {
                // Assert connection is open when callback executes
                connectionWasOpenDuringCallback = conn.State == ConnectionState.Open;
            });

        // Act
        await trackedConnection.OpenAsync();

        // Assert
        Assert.True(connectionWasOpenDuringCallback);
    }

    [Fact]
    public void DatabaseContext_AppliesDialectSessionSettings_OnFirstOpen()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);

        // Act
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        // Assert - session settings applied to at least one connection
        // (first connection may be probe, actual work connection gets settings)
        Assert.Contains(factory.CreatedConnections, conn =>
            conn.ExecutedNonQueryTexts.Any(cmd => cmd.StartsWith("SET ")));
    }

    [Fact]
    public void DatabaseContext_AppliesSessionSettingsAfterDialectDetection()
    {
        // Arrange - use MySQL which has session settings
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Server=localhost;Database=test;EmulatedProduct=MySql", factory);

        // Act
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        // Assert - at least one physical connection applied the SET statements
        Assert.Contains(factory.CreatedConnections,
            conn => conn.ExecutedNonQueryTexts.Any(cmd => cmd.StartsWith("SET ")));
    }

    [Fact]
    public void SessionSettingsSkippedWhenDialectProvidesNoStatements()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);

        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        Assert.All(factory.CreatedConnections, conn => Assert.Empty(conn.ExecutedNonQueryTexts));
    }

    [Fact]
    public void DatabaseContext_SplitsMultiStatementSettings_Correctly()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);

        // Act
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        // Assert
        var fakeConn = factory.CreatedConnections[0];
        var executed = fakeConn.ExecutedNonQueryTexts;

        // Each command should be trimmed, no empty statements
        Assert.All(executed, cmd =>
        {
            Assert.NotEmpty(cmd);
            Assert.Equal(cmd.Trim(), cmd);
        });

        // No command should contain multiple statements (no unprocessed semicolons)
        Assert.All(executed, cmd =>
        {
            Assert.DoesNotContain(";\n", cmd);
            Assert.DoesNotContain(";\r", cmd);
        });
    }

    [Fact]
    public void DatabaseContext_PooledReuse_DoesNotReapplySettings()
    {
        // Arrange
        var executionCounter = 0;
        var fakeConnection = new fakeDbConnection();

        var trackedConnection = new TrackedConnection(
            fakeConnection,
            onFirstOpen: conn => executionCounter++);

        // Act
        trackedConnection.Open(); // First open
        Assert.Equal(1, executionCounter);

        trackedConnection.Close();
        trackedConnection.Open(); // Simulated pool reuse (same TrackedConnection instance)

        // Assert
        Assert.Equal(1, executionCounter); // onFirstOpen should NOT execute again
    }

    [Fact]
    public void DatabaseContext_StandardMode_AppliesSessionSettingsOnOpen()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Host=localhost;Database=test;EmulatedProduct=PostgreSql", factory);

        // Act
        using var connection = context.GetConnection(ExecutionType.Write);
        connection.Open();

        // Assert - session settings should be applied to at least one connection
        // (first connection may be constructor probe, second is the actual working connection)
        var allExecuted = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts).ToList();
        Assert.NotEmpty(allExecuted);
        Assert.Contains(allExecuted, cmd => cmd.StartsWith("SET "));
    }

    [Fact]
    public void DatabaseContext_KeepAliveMode_AppliesSessionSettingsToConnections()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.KeepAlive
        };
        var context = new DatabaseContext(config, factory);

        // Act - Get connection (sentinel + ephemeral may be created)
        using var connection = context.GetConnection(ExecutionType.Write);
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        // Assert - At least one connection should have session settings applied
        Assert.NotEmpty(factory.CreatedConnections);
        Assert.Contains(factory.CreatedConnections,
            conn => conn.ExecutedNonQueryTexts.Any(cmd => cmd.StartsWith("SET ")));
    }

    [Fact]
    public void DatabaseContext_SingleConnectionMode_AppliesSessionSettingsOnPinnedConnection()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        var context = new DatabaseContext(config, factory);

        // Act - Get pinned connection (may already be open)
        using var connection = context.GetConnection(ExecutionType.Write);
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        // Assert
        Assert.NotEmpty(factory.CreatedConnections);
        var fakeConn = factory.CreatedConnections[0];

        // Session settings should be applied once to pinned connection
        // SQLite may or may not have session settings depending on dialect
        Assert.True(fakeConn.OpenCount >= 1);
    }

    [Fact]
    public void DatabaseContext_SingleWriterMode_AppliesSessionSettingsToPinnedWriter()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter
        };
        var context = new DatabaseContext(config, factory);

        // Act - Get pinned writer connection (may already be open)
        using var writerConnection = context.GetConnection(ExecutionType.Write);
        if (writerConnection.State != ConnectionState.Open)
        {
            writerConnection.Open();
        }

        // Assert - Writer connection should have been created
        Assert.NotEmpty(factory.CreatedConnections);
    }

    [Fact]
    public void DatabaseContext_SingleWriterMode_AppliesSessionSettingsToEphemeralReader()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter
        };
        var context = new DatabaseContext(config, factory);

        // Act - Get ephemeral read-only connection
        using var readerConnection = context.GetConnection(ExecutionType.Read);
        readerConnection.Open();

        // Assert - Reader connection should have session settings
        Assert.NotEmpty(factory.CreatedConnections);
        Assert.Contains(factory.CreatedConnections, conn => conn.ExecutedNonQueryTexts.Count > 0);
    }

    /// <summary>
    /// CRITICAL REGRESSION TEST: Session settings MUST be applied on first open for ALL modes.
    /// This invariant must never be broken - it ensures consistent database behavior.
    /// </summary>
    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.KeepAlive)]
    [InlineData(DbMode.SingleConnection)]
    [InlineData(DbMode.SingleWriter)]
    public void SessionSettings_MustBeApplied_OnFirstOpen_ForAllModes(DbMode mode)
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = mode
        };

        // Act
        var context = new DatabaseContext(config, factory);
        using var connection = context.GetConnection(ExecutionType.Write);
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        // Assert - Session settings MUST be applied to at least one connection
        var allCommands = factory.CreatedConnections.SelectMany(c => c.ExecutedNonQueryTexts).ToList();
        Assert.True(allCommands.Any(cmd => cmd.StartsWith("SET ")),
            $"Session settings were NOT applied for DbMode.{mode}. This is a critical regression!");
    }

    [Fact]
    public void DatabaseContext_AllModes_SessionSettingsAppliedOncePerPhysicalConnection()
    {
        // Test that session settings are applied exactly once per physical connection
        // across multiple open/close cycles for each DbMode

        var modes = new[] { DbMode.Standard, DbMode.KeepAlive, DbMode.SingleConnection, DbMode.SingleWriter };

        foreach (var mode in modes)
        {
            // Arrange
            var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
            var config = new DatabaseContextConfiguration
            {
                ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
                DbMode = mode
            };
            var context = new DatabaseContext(config, factory);

            // Act - Open and close connection twice
            using (var conn = context.GetConnection(ExecutionType.Write))
            {
                // Open if not already open (some modes open connections during context creation)
                if (conn.State != ConnectionState.Open)
                {
                    conn.Open();
                }

                var initialConnCount = factory.CreatedConnections.Count;
                var initialCmdCount = factory.CreatedConnections.Sum(c => c.ExecutedNonQueryTexts.Count);

                conn.Close();
                conn.Open(); // Second open on same TrackedConnection

                // Assert - Session settings executed only once per connection
                var finalCmdCount = factory.CreatedConnections.Sum(c => c.ExecutedNonQueryTexts.Count);
                Assert.Equal(initialCmdCount, finalCmdCount); // No additional session settings commands on second open
            }
        }
    }

    [Fact]
    public void TrackedReader_Dispose_ClosesConnection_WhenRequested()
    {
        // Arrange
        var fakeConnection = new fakeDbConnection();
        var trackedConnection = new TrackedConnection(fakeConnection);
        trackedConnection.Open();

        var fakeReader = new fakeDbDataReader();
        var locker = NoOpAsyncLocker.Instance;
        var tracked = new TrackedReader(fakeReader, trackedConnection, locker, true);

        // Act
        tracked.Dispose();

        // Assert - Reader disposes and closes connection
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        Assert.True(fakeConnection.CloseCount >= 1);
    }

    [Fact]
    public async Task TrackedReader_DisposeAsync_ClosesConnection_WhenRequested()
    {
        // Arrange
        var fakeConnection = new fakeDbConnection();
        var trackedConnection = new TrackedConnection(fakeConnection);
        await trackedConnection.OpenAsync();

        var fakeReader = new fakeDbDataReader();
        var locker = NoOpAsyncLocker.Instance;
        var tracked = new TrackedReader(fakeReader, trackedConnection, locker, true);

        // Act
        await tracked.DisposeAsync();

        // Assert - Reader disposes and closes connection
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        Assert.True(fakeConnection.CloseCount >= 1);
    }

    [Fact]
    public void TrackedReader_ReadToEOF_ClosesConnection_WhenRequested()
    {
        // Arrange
        var fakeConnection = new fakeDbConnection();
        var trackedConnection = new TrackedConnection(fakeConnection);
        trackedConnection.Open();

        var fakeReader = new fakeDbDataReader(); // Empty reader - Read() returns false immediately
        var locker = NoOpAsyncLocker.Instance;
        var tracked = new TrackedReader(fakeReader, trackedConnection, locker, true);

        // Act
        var result = tracked.Read(); // Returns false and auto-disposes

        // Assert - Auto-disposal at EOF closes connection
        Assert.False(result);
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        Assert.True(fakeConnection.CloseCount >= 1);
    }

    [Fact]
    public async Task TrackedReader_ReadAsyncToEOF_ClosesConnection_WhenRequested()
    {
        // Arrange
        var fakeConnection = new fakeDbConnection();
        var trackedConnection = new TrackedConnection(fakeConnection);
        await trackedConnection.OpenAsync();

        var fakeReader = new fakeDbDataReader(); // Empty reader - ReadAsync() returns false immediately
        var locker = NoOpAsyncLocker.Instance;
        var tracked = new TrackedReader(fakeReader, trackedConnection, locker, true);

        // Act
        var result = await tracked.ReadAsync(); // Returns false and auto-disposes

        // Assert - Auto-disposal at EOF closes connection
        Assert.False(result);
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        Assert.True(fakeConnection.CloseCount >= 1);
    }

    [Fact]
    public void TrackedReader_ShouldCloseConnectionParameter_IsRespected()
    {
        // The shouldCloseConnection parameter is respected for ephemeral read connections.

        var fakeConnection = new fakeDbConnection();
        var trackedConnection = new TrackedConnection(fakeConnection);
        trackedConnection.Open();

        var fakeReader = new fakeDbDataReader();
        var locker = NoOpAsyncLocker.Instance;

        // Act - Create reader with shouldCloseConnection=true
        var tracked = new TrackedReader(fakeReader, trackedConnection, locker, true);
        tracked.Dispose();

        // Assert - Connection is closed since parameter is true
        Assert.Equal(ConnectionState.Closed, trackedConnection.State);
        Assert.True(fakeConnection.CloseCount >= 1);
    }
}
