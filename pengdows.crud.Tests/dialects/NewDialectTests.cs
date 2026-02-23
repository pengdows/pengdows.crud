using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public class NewDialectTests
{
    [Fact]
    public void CreateDialectForType_YugabyteDb_ReturnsYugabyteDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.YugabyteDb,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<YugabyteDbDialect>(dialect);
        Assert.Equal(SupportedDatabase.YugabyteDb, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialectForType_TiDb_ReturnsTiDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.TiDb);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.TiDb,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<TiDbDialect>(dialect);
        Assert.Equal(SupportedDatabase.TiDb, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialectForType_CockroachDb_ReturnsCockroachDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.CockroachDb);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.CockroachDb,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<CockroachDbDialect>(dialect);
        Assert.Equal(SupportedDatabase.CockroachDb, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialect_DetectsYugabyteDbFromVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var (schema, scalars) = DataSourceTestData.BuildFixture(SupportedDatabase.YugabyteDb);
        using var x = factory.CreateConnection();
        // fakeDb supports EmulatedVersion
        x.ConnectionString = "EmulatedProduct=PostgreSql;EmulatedVersion=PostgreSQL 11.2-YB-2.19.0.0-b0";
        x.Open();
        var tracked = new FakeTrackedConnection(x, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.YugabyteDb, dialect.DatabaseType);
        Assert.IsType<YugabyteDbDialect>(dialect);
    }

    [Fact]
    public void CreateDialect_DetectsTiDbFromVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var (schema, scalars) = DataSourceTestData.BuildFixture(SupportedDatabase.TiDb);
        using var x = factory.CreateConnection();
        x.ConnectionString = "EmulatedProduct=MySql;EmulatedVersion=5.7.25-TiDB-v6.5.0";
        x.Open();
        var tracked = new FakeTrackedConnection(x, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.TiDb, dialect.DatabaseType);
        Assert.IsType<TiDbDialect>(dialect);
    }

    // ── YugabyteDb ───────────────────────────────────────────────────────────

    [Fact]
    public void YugabyteDbDialect_PrepareStatements_IsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.YugabyteDb);
        var dialect = new YugabyteDbDialect(factory, NullLogger<YugabyteDbDialect>.Instance);
        Assert.False(dialect.PrepareStatements);
    }

    // ── TiDb ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TiDbDialect_PrepareStatements_IsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.TiDb);
        var dialect = new TiDbDialect(factory, NullLogger<TiDbDialect>.Instance);
        Assert.False(dialect.PrepareStatements);
    }

    // ── Snowflake ─────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDialectForType_Snowflake_ReturnsSnowflakeDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.Snowflake,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<SnowflakeDialect>(dialect);
        Assert.Equal(SupportedDatabase.Snowflake, dialect.DatabaseType);
    }

    [Fact]
    public void CreateDialect_DetectsSnowflakeFromProductName()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var (schema, scalars) = DataSourceTestData.BuildFixture(SupportedDatabase.Snowflake);
        using var x = factory.CreateConnection();
        x.ConnectionString = "EmulatedProduct=Snowflake";
        x.Open();
        var tracked = new FakeTrackedConnection(x, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.Snowflake, dialect.DatabaseType);
        Assert.IsType<SnowflakeDialect>(dialect);
    }

    [Fact]
    public void SnowflakeDialect_ParameterMarker_IsColon()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
        Assert.Equal(":", dialect.ParameterMarker);
    }

    [Fact]
    public void SnowflakeDialect_SupportsSavepoints_IsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
        Assert.False(dialect.SupportsSavepoints);
    }

    [Fact]
    public void SnowflakeDialect_SupportsMerge_IsTrue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
        Assert.True(dialect.SupportsMerge);
    }

    [Fact]
    public void SnowflakeDialect_PrepareParameterValue_NormalizesDateTimeOffsetToUtcDateTime()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Snowflake);
        var dialect = new SnowflakeDialect(factory, NullLogger<SnowflakeDialect>.Instance);
        var value = new DateTimeOffset(2026, 2, 21, 14, 30, 45, 123, TimeSpan.FromHours(-5));

        var prepared = dialect.PrepareParameterValue(value, DbType.DateTimeOffset);

        var preparedDateTime = Assert.IsType<DateTime>(prepared);
        Assert.Equal(value.UtcDateTime, preparedDateTime);
    }
}