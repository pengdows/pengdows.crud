using System;
using System.Data;
using System.Linq.Expressions;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

// ---------------------------------------------------------------------------
// Test entities (file-scoped to avoid name collisions)
// ---------------------------------------------------------------------------

[Table("customer")]
file class CnrCustomer
{
    [Id(false)]
    [Column("customer_id", DbType.Int64)]
    public long Id { get; set; }

    [Column("customer_name", DbType.String)]
    public string Name { get; set; } = string.Empty;

    [Column("is_active", DbType.Boolean)]
    public bool IsActive { get; set; }

    [Column("age", DbType.Int32)]
    public int Age { get; set; }

    [Column("score", DbType.Decimal)]
    public decimal Score { get; set; }

    [Column("external_id", DbType.Guid)]
    public Guid ExternalId { get; set; }

    [Column("created_on", DbType.DateTime)]
    public DateTime CreatedOn { get; set; }

    // Unmapped — no [Column] attribute
    public string NotMapped { get; set; } = string.Empty;
}

// Base class for inheritance test
file class CnrAuditBase
{
    [Column("created_by", DbType.String)]
    public string CreatedBy { get; set; } = string.Empty;
}

[Table("invoice")]
file class CnrInvoice : CnrAuditBase
{
    [Id(false)]
    [Column("invoice_id", DbType.Int64)]
    public long InvoiceId { get; set; }

    [Column("total", DbType.Decimal)]
    public decimal Total { get; set; }
}

// ---------------------------------------------------------------------------
// ExpressionMemberExtractor tests
// ---------------------------------------------------------------------------

public class ExpressionMemberExtractorTests
{
    [Fact]
    public void ExtractProperty_DirectStringAccess_ReturnsCorrectProperty()
    {
        var prop = ExpressionMemberExtractor.ExtractProperty<CnrCustomer, string>(x => x.Name);
        Assert.Equal("Name", prop.Name);
    }

    [Fact]
    public void ExtractProperty_IntProperty_StripsConvertNode()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.Age;
        var prop = ExpressionMemberExtractor.ExtractProperty(expr);
        Assert.Equal("Age", prop.Name);
    }

    [Fact]
    public void ExtractProperty_BoolProperty_StripsConvertNode()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.IsActive;
        var prop = ExpressionMemberExtractor.ExtractProperty(expr);
        Assert.Equal("IsActive", prop.Name);
    }

    [Fact]
    public void ExtractProperty_GuidProperty_StripsConvertNode()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.ExternalId;
        var prop = ExpressionMemberExtractor.ExtractProperty(expr);
        Assert.Equal("ExternalId", prop.Name);
    }

    [Fact]
    public void ExtractProperty_DecimalProperty_StripsConvertNode()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.Score;
        var prop = ExpressionMemberExtractor.ExtractProperty(expr);
        Assert.Equal("Score", prop.Name);
    }

    [Fact]
    public void ExtractProperty_DateTimeProperty_StripsConvertNode()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.CreatedOn;
        var prop = ExpressionMemberExtractor.ExtractProperty(expr);
        Assert.Equal("CreatedOn", prop.Name);
    }

    [Fact]
    public void ExtractProperty_TValueOverload_NoConvertNeeded()
    {
        var prop = ExpressionMemberExtractor.ExtractProperty<CnrCustomer, int>(x => x.Age);
        Assert.Equal("Age", prop.Name);
    }

    [Fact]
    public void ExtractProperty_MethodCall_ThrowsArgumentException()
    {
        Expression<Func<CnrCustomer, object?>> expr = x => x.Name.ToUpper();
        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionMemberExtractor.ExtractProperty(expr));
        Assert.Contains("method call", ex.Message);
    }

    [Fact]
    public void ExtractProperty_NestedMemberAccess_ThrowsArgumentException()
    {
        // x.Name.Length: Length is declared on string, not CnrCustomer
        Expression<Func<CnrCustomer, int>> expr = x => x.Name.Length;
        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionMemberExtractor.ExtractProperty(expr));
        Assert.Contains("not declared on", ex.Message);
    }

    [Fact]
    public void ExtractProperty_NullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExpressionMemberExtractor.ExtractProperty<CnrCustomer, string>(null!));
    }

    [Fact]
    public void ExtractProperty_CoalesceExpression_ThrowsArgumentException()
    {
        Expression<Func<CnrCustomer, string>> expr = x => x.Name ?? "default";
        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionMemberExtractor.ExtractProperty(expr));
        Assert.Contains("null-coalescing", ex.Message);
    }
}

// ---------------------------------------------------------------------------
// ColumnName / WrappedColumnName via context
// ---------------------------------------------------------------------------

public class ColumnNameResolutionTests : IDisposable
{
    private readonly IDatabaseContext _context;

    public ColumnNameResolutionTests()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _context = new DatabaseContext("Data Source=:memory:", factory);
    }

    public void Dispose() => _context.Dispose();

    // -- ColumnName: returns raw DB column name --

    [Fact]
    public void ColumnName_StringProperty_ReturnsMappedName()
    {
        var name = _context.ColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal("customer_name", name);
    }

    [Fact]
    public void ColumnName_IntProperty_ReturnsMappedName()
    {
        var name = _context.ColumnName<CnrCustomer, int>(x => x.Age);
        Assert.Equal("age", name);
    }

    [Fact]
    public void ColumnName_BoolProperty_ReturnsMappedName()
    {
        var name = _context.ColumnName<CnrCustomer, bool>(x => x.IsActive);
        Assert.Equal("is_active", name);
    }

    [Fact]
    public void ColumnName_GuidProperty_ReturnsMappedName()
    {
        var name = _context.ColumnName<CnrCustomer, Guid>(x => x.ExternalId);
        Assert.Equal("external_id", name);
    }

    [Fact]
    public void ColumnName_IdProperty_ReturnsMappedName()
    {
        var name = _context.ColumnName<CnrCustomer, long>(x => x.Id);
        Assert.Equal("customer_id", name);
    }

    [Fact]
    public void ColumnName_ObjectOverload_MatchesTypedOverload()
    {
        var typed = _context.ColumnName<CnrCustomer, int>(x => x.Age);
        var erased = _context.ColumnName<CnrCustomer>(x => x.Age);
        Assert.Equal(typed, erased);
    }

    [Fact]
    public void ColumnName_UnmappedProperty_ThrowsSqlGenerationException()
    {
        Assert.Throws<SqlGenerationException>(() =>
            _context.ColumnName<CnrCustomer, string>(x => x.NotMapped));
    }

    [Fact]
    public void ColumnName_MethodCallExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _context.ColumnName<CnrCustomer, string>(x => x.Name.ToUpper()));
    }

    // -- WrappedColumnName: returns dialect-quoted name --

    [Fact]
    public void WrappedColumnName_StringProperty_ReturnsQuotedName()
    {
        var wrapped = _context.WrappedColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal("\"customer_name\"", wrapped);
    }

    [Fact]
    public void WrappedColumnName_IntProperty_ReturnsQuotedName()
    {
        var wrapped = _context.WrappedColumnName<CnrCustomer, int>(x => x.Age);
        Assert.Equal("\"age\"", wrapped);
    }

    [Fact]
    public void WrappedColumnName_ObjectOverload_MatchesTypedOverload()
    {
        var typed = _context.WrappedColumnName<CnrCustomer, int>(x => x.Age);
        var erased = _context.WrappedColumnName<CnrCustomer>(x => x.Age);
        Assert.Equal(typed, erased);
    }

    [Fact]
    public void WrappedColumnName_EquivalentToWrapObjectNameOfColumnName()
    {
        var col = _context.ColumnName<CnrCustomer, string>(x => x.Name);
        var expected = _context.WrapObjectName(col);
        var actual = _context.WrappedColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal(expected, actual);
    }

    // -- Inheritance: property declared on base type --

    [Fact]
    public void ColumnName_InheritedProperty_ResolvesCorrectly()
    {
        var name = _context.ColumnName<CnrInvoice, string>(x => x.CreatedBy);
        Assert.Equal("created_by", name);
    }

    [Fact]
    public void WrappedColumnName_InheritedProperty_ResolvesCorrectly()
    {
        var wrapped = _context.WrappedColumnName<CnrInvoice, string>(x => x.CreatedBy);
        Assert.Equal("\"created_by\"", wrapped);
    }

    // -- TransactionContext forwards to parent registry --

    [Fact]
    public async Task ColumnName_ThroughTransactionContext_ResolvesCorrectly()
    {
        await using var tx = await _context.BeginTransactionAsync();
        var name = tx.ColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal("customer_name", name);
    }

    [Fact]
    public async Task WrappedColumnName_ThroughTransactionContext_MatchesContext()
    {
        await using var tx = await _context.BeginTransactionAsync();
        var fromTx = tx.WrappedColumnName<CnrCustomer, string>(x => x.Name);
        var fromCtx = _context.WrappedColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal(fromCtx, fromTx);
    }

    // -- Null guards --

    [Fact]
    public void ColumnName_NullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _context.ColumnName<CnrCustomer, string>(null!));
    }

    [Fact]
    public void WrappedColumnName_NullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _context.WrappedColumnName<CnrCustomer, string>(null!));
    }
}

// ---------------------------------------------------------------------------
// Dialect-specific wrapping: SQL Server uses ANSI double-quotes
// ---------------------------------------------------------------------------

public class WrappedColumnNameDialectTests : IDisposable
{
    private readonly IDatabaseContext _sqlServer;

    public WrappedColumnNameDialectTests()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        _sqlServer = new DatabaseContext("Server=.;Database=test;", factory);
    }

    public void Dispose() => _sqlServer.Dispose();

    [Fact]
    public void WrappedColumnName_SqlServer_UsesAnsiDoubleQuote()
    {
        var wrapped = _sqlServer.WrappedColumnName<CnrCustomer, string>(x => x.Name);
        Assert.Equal("\"customer_name\"", wrapped);
    }
}
