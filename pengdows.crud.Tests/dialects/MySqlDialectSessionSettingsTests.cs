using System.Collections.Generic;
using System.Linq;
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
    public void GetConnectionSessionSettings_CapturedSqlModeMatches_StillReturnsTheFullEnforcementSql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);
        var required = RequiredSqlMode(dialect);

        // The writer's captured values: the server's sql_mode already has every required flag, and read-only is off.
        SetCapturedWriterDefaults(dialect, new Dictionary<string, object?>
        {
            ["SELECT @@SESSION.sql_mode"] = required,
            ["SELECT @@SESSION.transaction_read_only"] = 0L,
        });

        Assert.Contains("SET SESSION sql_mode = ", dialect.GetConnectionSessionSettings(null!, false));
    }

    [Fact]
    public void GetConnectionSessionSettings_NothingCaptured_SendsSqlMode()
    {
        var dialect = new MySqlDialect(new fakeDbFactory(SupportedDatabase.MySql), NullLogger<MySqlDialect>.Instance);

        Assert.Contains("SET SESSION sql_mode = ", dialect.GetConnectionSessionSettings(null!, false));
    }

    // The requirement is whatever the dialect sends when nothing is captured; the test reads it from there.
    private static string RequiredSqlMode(MySqlDialect dialect)
    {
        var statement = dialect.GetConnectionSessionSettings(null!, false)
            .Split('\n').Single(line => line.Contains("sql_mode", StringComparison.Ordinal));
        return System.Text.RegularExpressions.Regex.Match(statement, "sql_mode = '([^']*)'").Groups[1].Value;
    }

    private static void SetCapturedWriterDefaults(MySqlDialect dialect, Dictionary<string, object?> captured)
    {
        var field = typeof(MySqlDialect).GetField("_sessionDefaults", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(dialect, captured);
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