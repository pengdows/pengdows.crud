using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DataSourceInformationParameterTests
{
    private static ITrackedConnection BuildSqliteConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";

        var row = new Dictionary<string, object> { { "version", "3.0" } };
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });
        conn.EnqueueReaderResult(new[] { row });

        conn.Open();
        return new TrackedConnection(conn);
    }

    [Fact]
    public void ParameterProperties_ExposeDialectSettings()
    {
        var tracked = BuildSqliteConnection();
        var info = DataSourceInformation.Create(
            tracked,
            new fakeDbFactory(SupportedDatabase.Sqlite),
            NullLoggerFactory.Instance);

        // ParameterMarkerPattern defaults to empty
        Assert.Equal(string.Empty, info.ParameterMarkerPattern);
        Assert.NotEqual("@", info.ParameterMarkerPattern);

        // ParameterNameMaxLength reflects dialect setting
        Assert.Equal(255, info.ParameterNameMaxLength);
        Assert.NotEqual(0, info.ParameterNameMaxLength);

        // ParameterNamePatternRegex should match valid names and reject invalid ones
        var validName = "valid";
        var invalidName = "1invalid";
        Assert.True(info.ParameterNamePatternRegex.IsMatch(validName));
        Assert.False(info.ParameterNamePatternRegex.IsMatch(invalidName));
    }

    [Fact]
    public void Create_Throws_OnNullArguments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        Assert.Throws<ArgumentNullException>(
            () => DataSourceInformation.Create(null!, factory, NullLoggerFactory.Instance));

        var tracked = BuildSqliteConnection();
        Assert.Throws<ArgumentNullException>(
            () => DataSourceInformation.Create(tracked, null!, NullLoggerFactory.Instance));
    }
}
