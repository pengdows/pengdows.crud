using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Characterization tests for SqlDialect.RenderInsertReturningClause, written BEFORE converting
/// its switch(DatabaseType) block into per-dialect virtual overrides (CLAUDE.md "Adding a New
/// Database" checklist item 9's sibling case). Real dialect instances only — not a generic stub
/// claiming a DatabaseType it doesn't inherit — so the refactor from enum-dispatch to
/// type-dispatch can't silently change which implementation a test actually exercises.
/// </summary>
public class RenderInsertReturningClauseCharacterizationTests
{
    private static PostgreSqlDialect Postgres() => new(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger.Instance);
    private static CockroachDbDialect Cockroach() => new(new fakeDbFactory(SupportedDatabase.CockroachDb), NullLogger.Instance);
    private static YugabyteDbDialect Yugabyte() => new(new fakeDbFactory(SupportedDatabase.YugabyteDb), NullLogger.Instance);
    private static SqlServerDialect SqlServer() => new(new fakeDbFactory(SupportedDatabase.SqlServer), NullLogger.Instance);
    private static SqliteDialect Sqlite() => new(new fakeDbFactory(SupportedDatabase.Sqlite), NullLogger.Instance);
    private static FirebirdDialect Firebird() => new(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger.Instance);
    private static DuckDbDialect DuckDb() => new(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger.Instance);
    private static MySqlDialect MySql() => new(new fakeDbFactory(SupportedDatabase.MySql), NullLogger.Instance);
    private static OracleDialect Oracle() => new(new fakeDbFactory(SupportedDatabase.Oracle), NullLogger.Instance);

    [Fact] public void Postgres_UsesReturning() => Assert.Equal(" RETURNING \"id\"", Postgres().RenderInsertReturningClause("\"id\""));
    [Fact] public void CockroachDb_UsesReturning() => Assert.Equal(" RETURNING \"id\"", Cockroach().RenderInsertReturningClause("\"id\""));
    [Fact] public void YugabyteDb_UsesReturning() => Assert.Equal(" RETURNING \"id\"", Yugabyte().RenderInsertReturningClause("\"id\""));
    [Fact] public void Sqlite_UsesReturning() => Assert.Equal(" RETURNING \"id\"", Sqlite().RenderInsertReturningClause("\"id\""));
    [Fact] public void Firebird_UsesReturning() => Assert.Equal(" RETURNING \"id\"", Firebird().RenderInsertReturningClause("\"id\""));
    [Fact] public void DuckDb_UsesReturning() => Assert.Equal(" RETURNING \"id\"", DuckDb().RenderInsertReturningClause("\"id\""));
    [Fact] public void SqlServer_UsesOutputInserted() => Assert.Equal(" OUTPUT INSERTED.\"id\"", SqlServer().RenderInsertReturningClause("\"id\""));
    [Fact] public void MySql_DefaultEmpty() => Assert.Equal(string.Empty, MySql().RenderInsertReturningClause("\"id\""));

    [Fact]
    public void Oracle_UsesItsOwnPreexistingOverride_UnaffectedByThisRefactor()
    {
        // OracleDialect.RenderInsertReturningClause is its own pre-existing override (Oracle's
        // RETURNING INTO binds through an ADO.NET OUTPUT parameter, not an inline placeholder) —
        // untouched by this refactor. Asserting only that it's non-empty and distinct from the
        // generic RETURNING clause locks in "still has its own behavior" without over-specifying
        // Oracle's exact placeholder syntax here (already covered by Oracle-specific tests).
        var clause = Oracle().RenderInsertReturningClause("\"id\"");
        Assert.NotEqual(string.Empty, clause);
        Assert.NotEqual(" RETURNING \"id\"", clause);
    }
}
