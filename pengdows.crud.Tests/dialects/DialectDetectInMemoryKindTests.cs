#region

using System;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// ISqlDialect.DetectInMemoryKind is the single source of truth for recognizing an in-memory
/// connection string; DatabaseContext no longer maintains its own per-product parsing switch.
/// Only SQLite and DuckDB have any in-memory concept — every other dialect always reports None.
/// </summary>
public class DialectDetectInMemoryKindTests
{
    private static ISqlDialect CreateDialect(SupportedDatabase db) =>
        SqlDialectFactory.CreateDialectForType(db, new fakeDbFactory(db), NullLogger.Instance);

    [Theory]
    [InlineData("Data Source=:memory:", InMemoryKind.Isolated)]
    [InlineData("Data Source=file:memdb1?mode=memory&cache=shared", InMemoryKind.Shared)]
    [InlineData("Data Source=file.db", InMemoryKind.None)]
    public void Sqlite_DetectsInMemoryKind(string connectionString, InMemoryKind expected)
    {
        var dialect = CreateDialect(SupportedDatabase.Sqlite);
        Assert.Equal(expected, dialect.DetectInMemoryKind(connectionString));
    }

    [Theory]
    [InlineData("Data Source=:memory:", InMemoryKind.Isolated)]
    [InlineData("Data Source=:memory:;cache=shared", InMemoryKind.Shared)]
    [InlineData("Data Source=file.duckdb", InMemoryKind.None)]
    public void DuckDb_DetectsInMemoryKind(string connectionString, InMemoryKind expected)
    {
        var dialect = CreateDialect(SupportedDatabase.DuckDB);
        Assert.Equal(expected, dialect.DetectInMemoryKind(connectionString));
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.Unknown)]
    // Access IS embedded/file-based (see DialectCoerceConnectionModeTests), but unlike
    // Sqlite/DuckDB it has no in-memory concept at all — confirmed live, Jet/ACE has no
    // ":memory:"-equivalent connection mode — so it belongs in this "always None" group too.
    [InlineData(SupportedDatabase.Access)]
    public void NonEmbeddedDialects_AlwaysReportNone(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        Assert.Equal(InMemoryKind.None, dialect.DetectInMemoryKind("Data Source=:memory:"));
    }

    /// <summary>
    /// Generalizes <see cref="NonEmbeddedDialects_AlwaysReportNone"/>'s hand-picked list into a
    /// default-deny invariant over the WHOLE enum: only SQLite and DuckDB genuinely have an
    /// in-memory concept, so every other value — including any future one — must report
    /// <see cref="InMemoryKind.None"/> for a <c>:memory:</c>-shaped connection string, with no one
    /// needing to remember to add it to a list. A hand-picked <c>InlineData</c> list only proves
    /// what someone thought to test; a new database that's never added to it isn't caught
    /// failing, it's just silently never checked at all — the exact failure mode the four
    /// `InlineData` cases above already had (Access needed adding by hand and easily could have
    /// been forgotten).
    /// </summary>
    [Fact]
    public void EveryDatabaseExceptSqliteAndDuckDb_ReportsNoneForMemoryConnectionString()
    {
        foreach (SupportedDatabase db in Enum.GetValues(typeof(SupportedDatabase)))
        {
            if (db is SupportedDatabase.Sqlite or SupportedDatabase.DuckDB)
            {
                continue;
            }

            var dialect = CreateDialect(db);
            Assert.Equal(InMemoryKind.None, dialect.DetectInMemoryKind("Data Source=:memory:"));
        }
    }
}
