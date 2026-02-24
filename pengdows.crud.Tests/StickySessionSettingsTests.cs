using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class StickySessionSettingsTests
{
    [Fact]
    public void ExecuteSessionSettings_WhenIntentChanges_ReexecutesSettings()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db",
            EnableMetrics = false
        };
        
        var context = new DatabaseContext(config, factory);
        
        // Simulate a physical connection from the pool
        using var physicalConn = factory.CreateConnection();
        physicalConn.Open();
        
        // Get the command history from the fake connection
        var fakeConn = (fakeDbConnection)physicalConn;

        // Act 1: Initial Read
        context.ExecuteSessionSettings(physicalConn, readOnly: true);
        var firstSettingsCount = fakeConn.ExecutedNonQueryTexts.Count;
        Assert.True(firstSettingsCount > 0, "Settings should execute on first call");

        // Act 2: Second Read (Should be skipped)
        context.ExecuteSessionSettings(physicalConn, readOnly: true);
        Assert.Equal(firstSettingsCount, fakeConn.ExecutedNonQueryTexts.Count);

        // Act 3: Intent Change to Write (MUST RE-EXECUTE)
        context.ExecuteSessionSettings(physicalConn, readOnly: false);
        
        // Assert
        Assert.True(fakeConn.ExecutedNonQueryTexts.Count > firstSettingsCount, 
            "Settings MUST re-execute when intent changes, even on the same physical connection.");
    }
}
