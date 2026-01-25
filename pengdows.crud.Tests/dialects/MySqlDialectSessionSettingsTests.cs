using System;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class MySqlDialectSessionSettingsTests
{
    [Fact]
    public void GetConnectionSessionSettings_EmptyCache_UsesDefaultSqlMode()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        var field = typeof(MySqlDialect).GetField("_sessionSettings", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(dialect, string.Empty);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=MySql",
            DbMode = DbMode.Standard
        };
        using var ctx = new DatabaseContext(config, factory);

        var settings = dialect.GetConnectionSessionSettings(ctx, false);

        Assert.Contains("sql_mode", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANSI_QUOTES", settings, StringComparison.OrdinalIgnoreCase);
    }
}