using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Covers SqlDialect DuckDB-specific switch cases added in the Firebird/DuckDB exception-detection
/// work: IsUniqueViolation, IsForeignKeyViolation, IsNotNullViolation, IsCheckConstraintViolation,
/// and TryClassifyProviderException (via AnalyzeException).
/// </summary>
public class DuckDbConstraintViolationTests
{
    private static IDatabaseContext CreateContext() =>
        new DatabaseContext("Data Source=:memory:;EmulatedProduct=DuckDB",
            new fakeDbFactory(SupportedDatabase.DuckDB));

    private static IDatabaseContext CreateFirebirdContext() =>
        new DatabaseContext("Data Source=:memory:;EmulatedProduct=Firebird",
            new fakeDbFactory(SupportedDatabase.Firebird));

    // ── IsUniqueViolation ────────────────────────────────────────────────────

    [Fact]
    public void IsUniqueViolation_DuckDB_SqlState23505_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23505", "Duplicate key");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_DuckDB_DuplicateKeyMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("Duplicate key 'xyz' violates unique constraint 'pk_orders'");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_DuckDB_UniqueConstraintMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("unique constraint failed: table.col");
        Assert.True(ctx.GetDialect().IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_DuckDB_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("arithmetic overflow");
        Assert.False(ctx.GetDialect().IsUniqueViolation(ex));
    }

    // ── IsForeignKeyViolation ────────────────────────────────────────────────

    [Fact]
    public void IsForeignKeyViolation_DuckDB_SqlState23503_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23503", "foreign key constraint violated");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_DuckDB_ForeignKeyMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("Violates foreign key constraint fk_orders_customers");
        Assert.True(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    [Fact]
    public void IsForeignKeyViolation_DuckDB_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("NOT NULL constraint failed");
        Assert.False(ctx.GetDialect().IsForeignKeyViolation(ex));
    }

    // ── IsNotNullViolation ───────────────────────────────────────────────────

    [Fact]
    public void IsNotNullViolation_DuckDB_SqlState23502_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23502", "NOT NULL constraint violated");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_DuckDB_NotNullConstraintMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("NOT NULL constraint failed: orders.customer_id");
        Assert.True(ctx.GetDialect().IsNotNullViolation(ex));
    }

    [Fact]
    public void IsNotNullViolation_DuckDB_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("division by zero");
        Assert.False(ctx.GetDialect().IsNotNullViolation(ex));
    }

    // ── IsCheckConstraintViolation ───────────────────────────────────────────

    [Fact]
    public void IsCheckConstraintViolation_DuckDB_SqlState23514_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23514", "CHECK constraint violated");
        Assert.True(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_DuckDB_CheckConstraintMessage_ReturnsTrue()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("CHECK constraint chk_positive_value failed");
        Assert.True(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    [Fact]
    public void IsCheckConstraintViolation_DuckDB_UnrelatedMessage_ReturnsFalse()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("table not found");
        Assert.False(ctx.GetDialect().IsCheckConstraintViolation(ex));
    }

    // ── TryClassifyProviderException via AnalyzeException ───────────────────

    [Fact]
    public void AnalyzeException_DuckDB_SqlState23xxx_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new SqlStateDbException("23505", "Duplicate key");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_DuckDB_ConstraintErrorMessage_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateContext();
        var ex = new PlainDbException("Constraint Error: Duplicate key 'x'");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_Firebird_ViolationOfMessage_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateFirebirdContext();
        var ex = new PlainDbException("violation of PRIMARY KEY constraint \"PK_ORDER\" on table \"ORDERS\"");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_Firebird_NullMarkerMessage_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateFirebirdContext();
        var ex = new PlainDbException("validation error for column \"name\", value \"*** null ***\"");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_Firebird_CheckConstraintMessage_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateFirebirdContext();
        var ex = new PlainDbException("Operation violates CHECK constraint CHK_VALUE on table ORDERS");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
    }

    [Fact]
    public void AnalyzeException_Firebird_SqlState23_ClassifiesAsConstraintViolation()
    {
        using var ctx = CreateFirebirdContext();
        var ex = new SqlStateDbException("23000", "integrity constraint violated");
        var info = ctx.GetDialect().AnalyzeException(ex);
        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
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
