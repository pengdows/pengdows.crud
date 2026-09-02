#region

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
    public void NonEmbeddedDialects_AlwaysReportNone(SupportedDatabase db)
    {
        var dialect = CreateDialect(db);
        Assert.Equal(InMemoryKind.None, dialect.DetectInMemoryKind("Data Source=:memory:"));
    }
}
