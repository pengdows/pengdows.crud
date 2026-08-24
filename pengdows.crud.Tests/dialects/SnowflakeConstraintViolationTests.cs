using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Covers SqlDialect's Snowflake-specific switch cases: IsUniqueViolation,
/// IsForeignKeyViolation, IsNotNullViolation, IsCheckConstraintViolation, and
/// TryClassifyProviderException (via AnalyzeException).
///
/// Snowflake parses UNIQUE/PRIMARY KEY/FOREIGN KEY/CHECK constraint DDL but does not enforce
/// any of them at runtime (see SnowflakeDialect.EnforcesConstraints,
/// EnforcesForeignKeyConstraints, SupportsUniqueConstraints, SupportsCheckConstraints — all
/// false), so those exception categories structurally cannot occur. NOT NULL is the one
/// constraint Snowflake actually enforces (error 100072, SQLSTATE 23502, message "NULL result
/// in a non-nullable column" per Snowflake's own error catalog).
/// </summary>
public class SnowflakeConstraintViolationTests
{
    private static IDatabaseContext CreateContext() =>
        new DatabaseContext("Data Source=test;EmulatedProduct=Snowflake",
            new fakeDbFactory(SupportedDatabase.Snowflake));

    // ── IsUniqueViolation / IsForeignKeyViolation / IsCheckConstraintViolation ──
    // Never true for Snowflake — those constraints are declarative-only, never enforced.

    [Fact]
    public void IsUniqueViolation_Snowflake_AlwaysReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23505", "would-be duplicate key message");
        Assert.False(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_Snowflake_AlwaysReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23503", "would-be foreign key message");
        Assert.False(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_Snowflake_AlwaysReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23514", "would-be check constraint message");
        Assert.False(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    // ── IsNotNullViolation ───────────────────────────────────────────────────

    [Fact]
    public void IsNotNullViolation_Snowflake_SqlState23502_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23502", "NULL result in a non-nullable column");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_Snowflake_NonNullableMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        // No SqlState populated — message-only fallback, matching Snowflake's actual wording
        // ("non-nullable"), which the generic default fallback's "not null"/"not-null" check
        // does not match.
        var ex = new PlainDbException("NULL result in a non-nullable column");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_Snowflake_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("SQL compilation error: syntax error");
        Assert.False(ctx.GetDialect().IsNotNullViolation(ex));
    }

    // ── TryClassifyProviderException via AnalyzeException ───────────────────

    [Fact]
    public void AnalyzeException_Snowflake_NotNullSqlState_ClassifiesAsConstraintViolationNotNull()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23502", "NULL result in a non-nullable column");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.NotNull, info.ConstraintKind);
    }

    [Fact]
    public void AnalyzeException_Snowflake_UnrelatedMessage_ClassifiesAsUnknown()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("SQL compilation error: syntax error");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.Unknown, info.Category);
    }

    // ── Helper types ─────────────────────────────────────────────────────────

    private sealed class PlainDbException : DbException
    {
        public PlainDbException(string message) : base(message) { }
    }

    private sealed class SqlStateDbException : DbException
    {
        public new string SqlState { get; }
        public SqlStateDbException(string sqlState, string message) : base(message) { SqlState = sqlState; }
    }
}
