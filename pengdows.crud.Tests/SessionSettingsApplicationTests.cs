using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
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

        var preamble = ctx.SessionSettingsPreamble;

        Assert.False(string.IsNullOrWhiteSpace(preamble));
        Assert.Contains("sql_mode", preamble, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANSI_QUOTES", preamble, StringComparison.OrdinalIgnoreCase);
    }
}
