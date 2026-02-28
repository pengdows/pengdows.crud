using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class ExtendedDialectSafetyTests
{
    [Fact]
    public void Snowflake_EnforcesUTCAndFormat()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);

        var settings = dialect.GetBaseSessionSettings();

        Assert.Contains("SET TIMEZONE = 'UTC'", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIMESTAMP_OUTPUT_FORMAT = 'YYYY-MM-DD HH24:MI:SS.FF3'", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Firebird_EnforcesUtf8Names()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var dialect = new FirebirdDialect(factory, NullLogger<FirebirdDialect>.Instance);

        var settings = dialect.GetBaseSessionSettings();

        Assert.Contains("SET NAMES UTF8", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET SQL DIALECT 3", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CockroachDb_EnforcesUtf8AndLockTimeout()
    {
        var factory = new fakeDbFactory(SupportedDatabase.CockroachDb);
        var dialect = new CockroachDbDialect(factory, NullLogger<CockroachDbDialect>.Instance);

        var settings = dialect.GetBaseSessionSettings();

        Assert.Contains("SET client_encoding = 'UTF8'", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET lock_timeout = '30s'", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void YugabyteDb_EnforcesUtf8AndLockTimeout()
    {
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb);
        var dialect = new YugabyteDbDialect(factory, NullLogger<YugabyteDbDialect>.Instance);

        var settings = dialect.GetBaseSessionSettings();

        Assert.Contains("SET client_encoding = 'UTF8'", settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET lock_timeout = '30s'", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Snowflake_InterrogatesAndCalculatesDelta()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        // Mock DB already has UTC but wrong format and missing lock timeout
        // Snowflake SHOW PARAMETERS returns rows with 'key' and 'value' among other columns
        factory.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object> { ["key"] = "TIMEZONE", ["value"] = "UTC" },
            new Dictionary<string, object> { ["key"] = "TIMESTAMP_OUTPUT_FORMAT", ["value"] = "YYYY-MM-DD" },
            new Dictionary<string, object> { ["key"] = "LOCK_TIMEOUT", ["value"] = "10000" }
        });

        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
        using var rawConn = factory.CreateConnection();
        var conn = new TrackedConnection(rawConn);
        await conn.OpenAsync();

        await dialect.DetectDatabaseInfoAsync(conn);
        var settings = dialect.GetBaseSessionSettings();

        // Should NOT contain TIMEZONE (already UTC)
        Assert.DoesNotContain("TIMEZONE", settings);
        // Should contain the ones that differ
        Assert.Contains("TIMESTAMP_OUTPUT_FORMAT = 'YYYY-MM-DD HH24:MI:SS.FF3'", settings);
        Assert.Contains("LOCK_TIMEOUT = 30000", settings);
    }

    [Fact]
    public async Task Firebird_InterrogatesAndCalculatesDelta()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        // Mock Firebird with Dialect 1
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ScalarResults.Enqueue(1); // SQL Dialect 1
        factory.Connections.Insert(0, conn);

        var dialect = new FirebirdDialect(factory, NullLogger<FirebirdDialect>.Instance);
        using var rawTConn = factory.CreateConnection();
        var tconn = new TrackedConnection(rawTConn);
        await tconn.OpenAsync();

        await dialect.DetectDatabaseInfoAsync(tconn);
        var settings = dialect.GetBaseSessionSettings();

        // NAMES UTF8 is always sent because we can't reliably interrogate it, 
        // but SQL DIALECT 3 should be there because we detected 1.
        Assert.Contains("SET NAMES UTF8", settings);
        Assert.Contains("SET SQL DIALECT 3", settings);
    }
}
