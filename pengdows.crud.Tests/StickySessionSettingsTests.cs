using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class StickySessionSettingsTests
{
    [Fact]
    public void ExecuteSessionSettings_AlwaysApplies_AndIncludesForeignKeysPragma()
    {
        // Arrange
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db",
            EnableMetrics = false
        };

        var context = new DatabaseContext(config, factory);

        using var physicalConn1 = factory.CreateConnection();
        physicalConn1.Open();
        var fakeConn1 = (fakeDbConnection)physicalConn1;

        // Act 1: First call — applies settings and emits PRAGMA foreign_keys = ON (SQLite behavior)
        context.ExecuteSessionSettings(physicalConn1, readOnly: false);
        var countAfterFirst = fakeConn1.ExecutedNonQueryTexts.Count;
        Assert.True(countAfterFirst > 0, "Settings should execute on first call");
        Assert.Contains(fakeConn1.ExecutedNonQueryTexts,
            sql => sql.Contains("PRAGMA foreign_keys = ON", StringComparison.OrdinalIgnoreCase));

        // Act 2: Second call on the same connection — zero-trust policy means we always re-apply.
        // We cannot guarantee without a round-trip that pool session state hasn't been reset.
        context.ExecuteSessionSettings(physicalConn1, readOnly: false);
        Assert.True(fakeConn1.ExecutedNonQueryTexts.Count > countAfterFirst,
            "Settings must re-execute on every call (zero-trust policy).");

        // Act 3: A different physical connection also receives settings
        using var physicalConn2 = factory.CreateConnection();
        physicalConn2.Open();
        var fakeConn2 = (fakeDbConnection)physicalConn2;

        context.ExecuteSessionSettings(physicalConn2, readOnly: false);
        Assert.Contains(fakeConn2.ExecutedNonQueryTexts,
            sql => sql.Contains("PRAGMA foreign_keys = ON", StringComparison.OrdinalIgnoreCase));
    }
}
