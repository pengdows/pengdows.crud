using System;
using System.Collections.Generic;
using System.Reflection;
using pengdows.crud.dialects;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlDialectSessionSettingsScriptTests
{
    [Fact]
    public void BuildSessionSettingsScript_EmptyExpected_ReturnsEmpty()
    {
        var expected = new Dictionary<string, string>();
        var current = new Dictionary<string, string>();
        var script = BuildScript(expected, current);
        Assert.Equal(string.Empty, script);
    }

    [Fact]
    public void BuildSessionSettingsScript_FormatsSingleSetting()
    {
        var expected = new Dictionary<string, string> { ["ansi_nulls"] = "ON" };
        var current = new Dictionary<string, string>();
        var script = BuildScript(expected, current);
        Assert.Equal("SET ansi_nulls = ON;", script);
    }

    [Fact]
    public void BuildSessionSettingsScript_SkipsUnchangedSettings()
    {
        var expected = new Dictionary<string, string>
        {
            ["ansi_nulls"] = "ON",
            ["quoted_identifier"] = "ON"
        };
        var current = new Dictionary<string, string>
        {
            ["quoted_identifier"] = "ON"
        };
        var script = BuildScript(expected, current);
        Assert.Equal("SET ansi_nulls = ON;", script);
    }

    private static string BuildScript(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> current)
    {
        var method = typeof(SqlDialect).GetMethod(
            "BuildSessionSettingsScript",
            BindingFlags.NonPublic | BindingFlags.Static);
        var formatter = new Func<string, string, string>((key, value) => $"SET {key} = {value};");
        return (string)method!.Invoke(null, new object[] { expected, current, formatter })!;
    }
}