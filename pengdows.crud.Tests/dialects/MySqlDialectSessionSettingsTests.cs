using System;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

    [Fact]
    public void GetFinalSessionSettings_ReadOnlyFalse_AppendsReadWriteIntent()
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

        var settings = dialect.GetFinalSessionSettings(readOnly: false);

        Assert.Contains(dialect.GetBaseSessionSettings().TrimEnd(';'), settings, StringComparison.Ordinal);
        Assert.Contains("transaction_read_only = 0", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFinalSessionSettings_ReadOnlyTrue_AppendsReadOnlyIntent()
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

        var settings = dialect.GetFinalSessionSettings(readOnly: true);

        Assert.Contains("transaction_read_only = 1", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PrepareConnectionStringForDataSource_NullOrWhitespace_ReturnsInputUnchanged(string? input)
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

        var result = dialect.PrepareConnectionStringForDataSource(input!);

        Assert.Equal(input, result);
    }
}