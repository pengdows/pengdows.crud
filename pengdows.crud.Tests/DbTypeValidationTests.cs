using System;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Proves that CreateDbParameter validates CLR type compatibility with DbType
/// before execution, catching mismatches early with clear error messages.
/// </summary>
public class DbTypeValidationTests
{
    private async Task<IDatabaseContext> CreateContext(SupportedDatabase db = SupportedDatabase.Sqlite)
    {
        var factory = new fakeDbFactory(db);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test.db;EmulatedProduct={db}",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        return new DatabaseContext(config, factory);
    }

    [Fact]
    public async Task StringValue_WithIntDbType_Throws()
    {
        await using var ctx = await CreateContext();
        var ex = Assert.Throws<ArgumentException>(() =>
            ctx.CreateDbParameter("p", DbType.Int32, "hello"));
        Assert.Contains("String", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public async Task DateTimeValue_WithBooleanDbType_Throws()
    {
        // Use a provider that doesn't pre-coerce DateTime (SQL Server doesn't override CreateDbParameter)
        await using var ctx = await CreateContext(SupportedDatabase.SqlServer);
        var ex = Assert.Throws<ArgumentException>(() =>
            ctx.CreateDbParameter("p", DbType.Boolean, DateTime.UtcNow));
        Assert.Contains("DateTime", ex.Message);
        Assert.Contains("Boolean", ex.Message);
    }

    [Fact]
    public async Task IntValue_WithIntDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Int32, 42);
        Assert.Equal(42, param.Value);
    }

    [Fact]
    public async Task LongValue_WithInt16DbType_Succeeds_ProviderHandlesNarrowing()
    {
        // Numeric narrowing (long→short) is allowed at the validator level.
        // The provider will throw at execution time if the value overflows.
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Int16, 42L);
        Assert.Equal(42L, param.Value);
    }

    [Fact]
    public async Task IntValue_WithInt64DbType_Succeeds_NumericWidening()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Int64, 42);
        Assert.Equal(42, param.Value);
    }

    [Fact]
    public async Task NullValue_WithAnyDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter<string?>("p", DbType.String, null);
        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task DBNullValue_WithAnyDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter<object?>("p", DbType.Int32, DBNull.Value);
        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task BoolValue_WithBooleanDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Boolean, true);
        // Value should be set (may be coerced for positional providers)
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task DecimalValue_WithStringDbType_Throws()
    {
        await using var ctx = await CreateContext();
        var ex = Assert.Throws<ArgumentException>(() =>
            ctx.CreateDbParameter("p", DbType.String, 42.5m));
        Assert.Contains("Decimal", ex.Message);
        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public async Task GuidValue_WithGuidDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var guid = Guid.NewGuid();
        var param = ctx.CreateDbParameter("p", DbType.Guid, guid);
        // May be coerced to string for some providers, but should not throw
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task StringValue_WithStringDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.String, "hello");
        Assert.Equal("hello", param.Value);
    }

    [Fact]
    public async Task ByteArrayValue_WithBinaryDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var bytes = new byte[] { 1, 2, 3 };
        var param = ctx.CreateDbParameter("p", DbType.Binary, bytes);
        Assert.Equal(bytes, param.Value);
    }

    [Fact]
    public async Task FloatValue_WithDoubleDbType_Succeeds_NumericWidening()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Double, 3.14f);
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task DateTimeValue_WithDateTimeDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var dt = DateTime.UtcNow;
        var param = ctx.CreateDbParameter("p", DbType.DateTime, dt);
        // SQLite coerces DateTime to ISO-8601 string; other providers keep the DateTime.
        // Either way, the value should be non-null.
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task DateTimeOffsetValue_WithDateTimeOffsetDbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var dto = DateTimeOffset.UtcNow;
        var param = ctx.CreateDbParameter("p", DbType.DateTimeOffset, dto);
        // May be coerced for some providers, but should not throw
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task EnumValue_WithInt32DbType_Succeeds()
    {
        await using var ctx = await CreateContext();
        var param = ctx.CreateDbParameter("p", DbType.Int32, DayOfWeek.Monday);
        Assert.NotEqual(DBNull.Value, param.Value);
    }

    [Fact]
    public async Task BoolValue_WithInt32DbType_Throws()
    {
        // Use a provider that doesn't coerce bool to numeric (SQLite allows 0/1 coercion).
        await using var ctx = await CreateContext(SupportedDatabase.SqlServer);
        var ex = Assert.Throws<ArgumentException>(() =>
            ctx.CreateDbParameter("p", DbType.Int32, true));
        Assert.Contains("Boolean", ex.Message);
        Assert.Contains("Int32", ex.Message);
    }
}
