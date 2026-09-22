using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Characterization tests for SqlDialect.GetNaturalKeyLookupQuery, written BEFORE converting its
/// switch(DatabaseType)/DatabaseType== checks into per-dialect virtual overrides (CLAUDE.md
/// "Adding a New Database" checklist item 10 — explicitly flagged there as a recurring bug
/// source: "Oracle needs ROWNUM = 1, Db2 needs FETCH FIRST 1 ROWS ONLY"). Exact generated SQL
/// text is asserted for every dialect the current switch special-cases, so the refactor is
/// provably behavior-preserving.
/// </summary>
public class NaturalKeyLookupQueryCharacterizationTests
{
    private static SqlServerDialect SqlServer() => new(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
    private static SybaseDialect Sybase() => new(new fakeDbFactory(SupportedDatabase.Sybase), NullLogger.Instance);
    private static OracleDialect Oracle() => new(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger.Instance);
    private static Db2Dialect Db2() => new(new fakeDbFactory(SupportedDatabase.Db2), NullLogger.Instance);
    private static PostgreSqlDialect Postgres() => new(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);

    [Fact]
    public void SqlServer_UsesTopOne_NoOrderBySuppressed_NoTailClause()
    {
        var sql = SqlServer().GetNaturalKeyLookupQuery("t", "id", new[] { "name" }, new[] { "@name" });

        Assert.Equal(
            "SELECT TOP 1 \"id\" FROM \"t\" WHERE \"name\" = @name ORDER BY \"id\" DESC",
            sql);
    }

    [Fact]
    public void Sybase_UsesTopOne_NoOrderBySuppressed_NoTailClause()
    {
        var sql = Sybase().GetNaturalKeyLookupQuery("t", "id", new[] { "name" }, new[] { "@name" });

        Assert.Equal(
            "SELECT TOP 1 \"id\" FROM \"t\" WHERE \"name\" = @name ORDER BY \"id\" DESC",
            sql);
    }

    [Fact]
    public void Oracle_NoOrderBy_UsesFetchFirstTail()
    {
        // OracleDialect's own pre-existing override calls base.GetNaturalKeyLookupQuery (which
        // used to append "AND ROWNUM = 1"), strips that literal suffix, and appends the modern
        // ANSI "FETCH FIRST 1 ROWS ONLY" instead — the base class's own "AND ROWNUM = 1" text
        // never survives to the caller for Oracle. This refactor makes the virtual hook emit
        // "FETCH FIRST 1 ROWS ONLY" for Oracle directly, so the real final output asserted here
        // doesn't change even though the mechanism producing it gets much simpler.
        var sql = Oracle().GetNaturalKeyLookupQuery("t", "id", new[] { "name" }, new[] { ":name" });

        Assert.Equal(
            "SELECT \"id\" FROM \"t\" WHERE \"name\" = :name FETCH FIRST 1 ROWS ONLY",
            sql);
    }

    [Fact]
    public void Db2_HasOrderBy_UsesFetchFirstTail()
    {
        var sql = Db2().GetNaturalKeyLookupQuery("t", "id", new[] { "name" }, new[] { "@name" });

        Assert.Equal(
            "SELECT \"id\" FROM \"t\" WHERE \"name\" = @name ORDER BY \"id\" DESC FETCH FIRST 1 ROWS ONLY",
            sql);
    }

    [Fact]
    public void Postgres_NoIdentityColumns_NoOrderBy_UsesLimitTail()
    {
        var sql = Postgres().GetNaturalKeyLookupQuery("t", "id", new[] { "name" }, new[] { "@name" });

        Assert.Equal(
            "SELECT \"id\" FROM \"t\" WHERE \"name\" = @name LIMIT 1",
            sql);
    }
}
