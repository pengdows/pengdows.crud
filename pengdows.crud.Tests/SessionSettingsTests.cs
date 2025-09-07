using System.Linq;
using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionSettingsTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql, DbMode.Standard)]
    [InlineData(SupportedDatabase.MariaDb, DbMode.Standard)]
    public void AppliesDialectSessionSettings_OnFirstOpen(SupportedDatabase db, DbMode mode)
    {
        var factory = new fakeDbFactory(db);
        var ctx = new DatabaseContext(
            new DatabaseContextConfiguration
            {
                ConnectionString = $"Data Source=test;EmulatedProduct={db}",
                DbMode = mode,
                ReadWriteMode = ReadWriteMode.ReadWrite
            },
            factory,
            NullLoggerFactory.Instance,
            null);

        // Trigger a new connection in Standard mode
        using var tracked = ctx.GetConnection(ExecutionType.Read);
        tracked.Open();

        // Inspect the underlying fake connection for executed non-queries
        using var cmd = tracked.CreateCommand();
        var fakeConn = (fakeDbConnection)cmd.Connection!;
        var executed = string.Join("\n",
            fakeConn.ExecutedNonQueryTexts.Select(s => s.Trim()).ToArray());

        Assert.Contains("SET SESSION", executed);
        Assert.Contains("sql_mode", executed, StringComparison.OrdinalIgnoreCase);
    }
}

