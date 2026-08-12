using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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

        var row = new Dictionary<string, object?> { { "version", "3.0" } };
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

        // ParameterMarkerPattern reflects the dialect's marker/named-parameter settings.
        // Sqlite: ParameterMarker = "@", SupportsNamedParameters = true.
        Assert.Equal("@\\w+", info.ParameterMarkerPattern);
        Assert.Matches(info.ParameterMarkerPattern, "@customerId");
        Assert.DoesNotMatch(info.ParameterMarkerPattern, "no marker here");

        // ParameterNameMaxLength reflects dialect setting
        Assert.Equal(255, info.ParameterNameMaxLength);
        Assert.NotEqual(0, info.ParameterNameMaxLength);

        // ParameterNamePatternRegex should match valid names and reject invalid ones
        var validName = "valid";
        var invalidName = "1invalid";
        Assert.Matches(info.ParameterNamePatternRegex, validName);
        Assert.DoesNotMatch(info.ParameterNamePatternRegex, invalidName);
    }

    [Fact]
    public void ParameterMarkerPattern_PositionalDialect_HasNoNameSuffix()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Unknown.ToString());
        var dialect = new Sql92Dialect(factory, NullLogger<Sql92Dialect>.Instance);

        var info = new DataSourceInformation(dialect);

        // Sql92 fallback dialect: ParameterMarker = "?", SupportsNamedParameters = false.
        Assert.Equal("\\?", info.ParameterMarkerPattern);
        Assert.Matches(info.ParameterMarkerPattern, "?");
        Assert.DoesNotMatch(info.ParameterMarkerPattern, "@customerId");
    }

    [Fact]
    public void Create_Throws_OnNullArguments()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        Assert.Throws<ArgumentNullException>(() =>
            DataSourceInformation.Create(null!, factory, NullLoggerFactory.Instance));

        var tracked = BuildSqliteConnection();
        Assert.Throws<ArgumentNullException>(() =>
            DataSourceInformation.Create(tracked, null!, NullLoggerFactory.Instance));
    }
}