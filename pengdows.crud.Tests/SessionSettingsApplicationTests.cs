using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionSettingsApplicationTests
{
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void StandardMode_InitializesSessionSettings_ForAnsiQuotes(SupportedDatabase db)
    {
        var factory = new fakeDbFactory(db);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={db}",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var field = typeof(DatabaseContext)
            .GetField("_connectionSessionSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        var settings = (string?)field!.GetValue(ctx);

        Assert.False(string.IsNullOrWhiteSpace(settings));
        Assert.Contains("sql_mode", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANSI_QUOTES", settings, StringComparison.OrdinalIgnoreCase);
    }
}