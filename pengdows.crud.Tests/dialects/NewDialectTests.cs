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
    public void CreateDialectForType_QuestDb_ReturnsQuestDbDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = SqlDialectFactory.CreateDialectForType(
            SupportedDatabase.QuestDb,
            factory,
            NullLogger<SqlDialect>.Instance);
        Assert.IsType<QuestDbDialect>(dialect);
        Assert.Equal(SupportedDatabase.QuestDb, dialect.DatabaseType);
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

    [Fact]
    public void CreateDialect_DetectsQuestDbFromVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var (schema, scalars) = DataSourceTestData.BuildFixture(SupportedDatabase.QuestDb);
        using var x = factory.CreateConnection();
        x.ConnectionString = "EmulatedProduct=PostgreSql;EmulatedVersion=QuestDB 7.3.10";
        x.Open();
        var tracked = new FakeTrackedConnection(x, schema, scalars);
        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);
        Assert.Equal(SupportedDatabase.QuestDb, dialect.DatabaseType);
        Assert.IsType<QuestDbDialect>(dialect);
    }

    [Fact]
    public void QuestDbDialect_DisablesUnsupportedFeatures()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);
        Assert.False(dialect.SupportsSavepoints);
        Assert.False(dialect.SupportsInsertOnConflict);
    }

    // ── QuestDb: PrepareConnectionStringForDataSource ────────────────────────

    [Fact]
    public void QuestDbDialect_PrepareConnectionStringForDataSource_DoesNotInjectMaxAutoPrepare()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);
        const string input = "Host=localhost;Port=8812;Username=admin;Password=quest;Database=qdb;";

        var result = dialect.PrepareConnectionStringForDataSource(input);

        // QuestDb sets PrepareStatements=false; injecting Npgsql auto-prepare
        // settings contradicts that and must not happen.
        Assert.DoesNotContain("MaxAutoPrepare", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AutoPrepareMinUsages", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── QuestDb: CreateDbParameter type conversions ──────────────────────────

    [Fact]
    public void QuestDbDialect_CreateDbParameter_DateTime_ConvertsToMicrosecondsAsInt64()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);
        var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedMicros = (dt - epoch).Ticks / 10;

        var param = dialect.CreateDbParameter("ts", DbType.DateTime, dt);

        Assert.Equal(DbType.Int64, param.DbType);
        Assert.Equal(expectedMicros, param.Value);
    }

    [Fact]
    public void QuestDbDialect_CreateDbParameter_DateTimeOffset_ConvertsToMicrosecondsAsInt64()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);
        var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expectedMicros = (dto.ToUniversalTime() - epoch).Ticks / 10;

        var param = dialect.CreateDbParameter("ts", DbType.DateTimeOffset, dto);

        Assert.Equal(DbType.Int64, param.DbType);
        Assert.Equal(expectedMicros, param.Value);
    }

    [Fact]
    public void QuestDbDialect_CreateDbParameter_String_SetsAnsiString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);

        var param = dialect.CreateDbParameter("name", DbType.String, "hello");

        Assert.Equal(DbType.AnsiString, param.DbType);
    }

    [Fact]
    public void QuestDbDialect_CreateDbParameter_AnsiString_SetsAnsiString()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);

        var param = dialect.CreateDbParameter("col", DbType.AnsiString, "world");

        Assert.Equal(DbType.AnsiString, param.DbType);
    }

    [Fact]
    public void QuestDbDialect_CreateDbParameter_Null_SetsDbnull()
    {
        var factory = new fakeDbFactory(SupportedDatabase.QuestDb);
        var dialect = new QuestDbDialect(factory, NullLogger<QuestDbDialect>.Instance);

        var param = dialect.CreateDbParameter<string?>("col", DbType.String, null);

        Assert.Equal(DBNull.Value, param.Value);
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
}
