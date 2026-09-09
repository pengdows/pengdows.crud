using pengdows.crud.enums;
using Xunit;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// Locks down the per-provider column-type mappings consolidated here from the previously
/// independent (and copy-pasted) private helpers in Core/AuditFieldTests.cs,
/// Core/CompositeKeyTests.cs, Core/MergeConflictTests.cs, and Core/VersionedUpsertConflictTests.cs
/// — see CLAUDE.md's "Adding a New Database" checklist item 19. Every case here is the union of
/// what those four helpers previously defined independently.
/// </summary>
public class IntegrationObjectNameHelperTypeTests
{
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "INTEGER")]
    [InlineData(SupportedDatabase.Oracle, "NUMBER(19)")]
    [InlineData(SupportedDatabase.Firebird, "BIGINT")]
    [InlineData(SupportedDatabase.PostgreSql, "BIGINT")]
    [InlineData(SupportedDatabase.Spanner, "BIGINT")]
    public void BigIntType_ReturnsExpectedTypeName(SupportedDatabase provider, string expected)
    {
        Assert.Equal(expected, IntegrationObjectNameHelper.BigIntType(provider));
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "INTEGER")]
    [InlineData(SupportedDatabase.Firebird, "INTEGER")]
    [InlineData(SupportedDatabase.PostgreSql, "INT")]
    [InlineData(SupportedDatabase.SqlServer, "INT")]
    public void IntType_ReturnsExpectedTypeName(SupportedDatabase provider, string expected)
    {
        Assert.Equal(expected, IntegrationObjectNameHelper.IntType(provider));
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "TEXT")]
    [InlineData(SupportedDatabase.SqlServer, "NVARCHAR(255)")]
    [InlineData(SupportedDatabase.Oracle, "VARCHAR2(255)")]
    [InlineData(SupportedDatabase.Firebird, "VARCHAR(255)")]
    [InlineData(SupportedDatabase.PostgreSql, "VARCHAR(255)")]
    public void StringType_ReturnsExpectedTypeName(SupportedDatabase provider, string expected)
    {
        Assert.Equal(expected, IntegrationObjectNameHelper.StringType(provider));
    }

    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "NUMERIC(18,2)")]
    [InlineData(SupportedDatabase.PostgreSql, "DECIMAL(18,2)")]
    // Spanner's PostgreSQL interface rejects a precision/scale modifier on NUMERIC/DECIMAL
    // outright — confirmed live: "P0001: Type modifier is not supported for type <numeric>."
    // (breaks CompositeKeyTests/RoundTripTests/every test creating a custom table with a decimal
    // column). Spanner's NUMERIC is a fixed-precision type; declare it bare, with no modifier.
    [InlineData(SupportedDatabase.Spanner, "NUMERIC")]
    public void DecimalType_ReturnsExpectedTypeName(SupportedDatabase provider, string expected)
    {
        Assert.Equal(expected, IntegrationObjectNameHelper.DecimalType(provider));
    }

    // Sqlite is deliberately "TEXT", not "DATETIME": SqliteDialect.CreateDbParameter always stores
    // DateTime values as ISO-8601 text (DbType.String, "o" format — see SqliteDialect.cs), so TEXT
    // is the technically correct declared affinity. AuditFieldTests.cs previously declared this
    // column "DATETIME" (a pre-existing inconsistency vs. the other two source files, reconciled
    // here) — SQLite's NUMERIC affinity for "DATETIME" happens to behave identically for a
    // non-numeric-looking ISO-8601 string, which is why the inconsistency never surfaced as a
    // test failure, but TEXT is the honest declaration matching what's actually stored.
    [Theory]
    [InlineData(SupportedDatabase.Sqlite, "TEXT")]
    [InlineData(SupportedDatabase.SqlServer, "DATETIME2")]
    [InlineData(SupportedDatabase.MySql, "DATETIME")]
    [InlineData(SupportedDatabase.MariaDb, "DATETIME")]
    [InlineData(SupportedDatabase.Spanner, "TIMESTAMPTZ")]
    [InlineData(SupportedDatabase.PostgreSql, "TIMESTAMP")]
    [InlineData(SupportedDatabase.Oracle, "TIMESTAMP")]
    public void DateTimeType_ReturnsExpectedTypeName(SupportedDatabase provider, string expected)
    {
        Assert.Equal(expected, IntegrationObjectNameHelper.DateTimeType(provider));
    }
}
