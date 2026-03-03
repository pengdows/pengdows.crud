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

        Assert.Contains("ALTER SESSION SET TIMEZONE = 'UTC'", settings, StringComparison.OrdinalIgnoreCase);
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
}
