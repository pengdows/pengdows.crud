using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for primitive-type parameter creation fast path.
/// Also covers the Validate(DbType, Type?) overload on DbTypeValidator.
/// </summary>
public class SqlDialectPrimitiveParameterTests
{
    // PostgreSQL: NeedsCommonConversions=false, PrepareParameterValue is no-op for primitives.
    // Clean, predictable behavior for testing the fast path without dialect-specific coercions.
    private static PostgreSqlDialect CreateDialect() =>
        new(new fakeDbFactory(SupportedDatabase.PostgreSql), NullLogger<PostgreSqlDialect>.Instance);

    // -------------------------------------------------------------------------
    // DbTypeValidator.Validate(DbType, Type?) overload
    // -------------------------------------------------------------------------

    [Fact]
    public void DbTypeValidator_ValidateWithType_NullType_DoesNotThrow()
    {
        // null clrType means value was null — always accepted regardless of DbType
        DbTypeValidator.Validate(DbType.Int32, (Type?)null);
        DbTypeValidator.Validate(DbType.String, (Type?)null);
        DbTypeValidator.Validate(DbType.Boolean, (Type?)null);
    }

    [Fact]
    public void DbTypeValidator_ValidateWithType_CompatibleTypes_DoNotThrow()
    {
        DbTypeValidator.Validate(DbType.Int32, typeof(int));
        DbTypeValidator.Validate(DbType.Int32, typeof(long));    // any numeric → any numeric DbType OK
        DbTypeValidator.Validate(DbType.Int64, typeof(long));
        DbTypeValidator.Validate(DbType.Int64, typeof(int));     // narrowing — provider handles overflow
        DbTypeValidator.Validate(DbType.Boolean, typeof(bool));
        DbTypeValidator.Validate(DbType.String, typeof(string));
        DbTypeValidator.Validate(DbType.String, typeof(Guid));   // Guid accepted for string DbType
        DbTypeValidator.Validate(DbType.Guid, typeof(Guid));
        DbTypeValidator.Validate(DbType.Guid, typeof(string));   // string accepted for Guid DbType
        DbTypeValidator.Validate(DbType.DateTime, typeof(DateTime));
        DbTypeValidator.Validate(DbType.DateTimeOffset, typeof(DateTimeOffset));
        DbTypeValidator.Validate(DbType.Decimal, typeof(decimal));
        DbTypeValidator.Validate(DbType.Double, typeof(double));
        DbTypeValidator.Validate(DbType.Object, typeof(object)); // Object accepts anything
        DbTypeValidator.Validate(DbType.Object, typeof(int));
    }

    [Fact]
    public void DbTypeValidator_ValidateWithType_IncompatibleString_Throws()
    {
        // string is not numeric
        Assert.Throws<ArgumentException>(() =>
            DbTypeValidator.Validate(DbType.Int32, typeof(string)));
    }

    [Fact]
    public void DbTypeValidator_ValidateWithType_IncompatibleBoolForNumeric_Throws()
    {
        // bool is not in NumericTypes
        Assert.Throws<ArgumentException>(() =>
            DbTypeValidator.Validate(DbType.Int32, typeof(bool)));
    }

    [Fact]
    public void DbTypeValidator_ValidateWithType_IncompatibleIntForBoolean_Throws()
    {
        // int is not in AcceptableTypes[DbType.Boolean]
        Assert.Throws<ArgumentException>(() =>
            DbTypeValidator.Validate(DbType.Boolean, typeof(int)));
    }

    [Fact]
    public void DbTypeValidator_ValidateWithType_EnumForNumericDbType_DoesNotThrow()
    {
        DbTypeValidator.Validate(DbType.Int32, typeof(DayOfWeek));   // enum
        DbTypeValidator.Validate(DbType.String, typeof(DayOfWeek));  // enum for string also OK
    }

    // -------------------------------------------------------------------------
    // CreateDbParameter<T>: primitive types produce correct Value and DbType
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateDbParameter_Int_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("myParam", DbType.Int32, 42);
        Assert.Equal(DbType.Int32, param.DbType);
        Assert.Equal(42, param.Value);
        Assert.Equal("myParam", param.ParameterName);
    }

    [Fact]
    public void CreateDbParameter_Long_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("p", DbType.Int64, 9_876_543_210L);
        Assert.Equal(DbType.Int64, param.DbType);
        Assert.Equal(9_876_543_210L, param.Value);
    }

    [Fact]
    public void CreateDbParameter_Bool_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("flag", DbType.Boolean, true);
        Assert.Equal(DbType.Boolean, param.DbType);
        Assert.Equal(true, param.Value);
    }

    [Fact]
    public void CreateDbParameter_Double_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("d", DbType.Double, 3.14);
        Assert.Equal(DbType.Double, param.DbType);
        Assert.Equal(3.14, param.Value);
    }

    [Fact]
    public void CreateDbParameter_String_SetsCorrectValueDbTypeAndSize()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("s", DbType.String, "hello");
        Assert.Equal(DbType.String, param.DbType);
        Assert.Equal("hello", param.Value);
        Assert.Equal(5, param.Size);
    }

    [Fact]
    public void CreateDbParameter_Guid_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var id = Guid.NewGuid();
        var param = dialect.CreateDbParameter("id", DbType.Guid, id);
        Assert.Equal(DbType.Guid, param.DbType);
        Assert.Equal(id, param.Value);
    }

    [Fact]
    public void CreateDbParameter_DateTime_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var dt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var param = dialect.CreateDbParameter("dt", DbType.DateTime, dt);
        Assert.Equal(DbType.DateTime, param.DbType);
        Assert.Equal(dt, param.Value);
    }

    [Fact]
    public void CreateDbParameter_NullableInt_WithValue_SetsCorrectValue()
    {
        var dialect = CreateDialect();
        int? value = 99;
        var param = dialect.CreateDbParameter("n", DbType.Int32, value);
        Assert.Equal(DbType.Int32, param.DbType);
        Assert.Equal(99, param.Value);
    }

    [Fact]
    public void CreateDbParameter_NullableInt_Null_SetsDBNull()
    {
        var dialect = CreateDialect();
        int? value = null;
        var param = dialect.CreateDbParameter("n", DbType.Int32, value);
        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public void CreateDbParameter_NullableGuid_Null_SetsDBNull()
    {
        var dialect = CreateDialect();
        Guid? value = null;
        var param = dialect.CreateDbParameter("g", DbType.Guid, value);
        Assert.Equal(DBNull.Value, param.Value);
    }

    [Fact]
    public void CreateDbParameter_Decimal_SetsPrecisionAndScale()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("amount", DbType.Decimal, 19.99m);
        Assert.Equal(DbType.Decimal, param.DbType);
        Assert.Equal(19.99m, param.Value);
        Assert.True(param.Precision >= 18, $"Expected Precision >= 18, got {param.Precision}");
        Assert.Equal(2, param.Scale);
    }

    [Fact]
    public void CreateDbParameter_Byte_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("b", DbType.Byte, (byte)255);
        Assert.Equal(DbType.Byte, param.DbType);
        Assert.Equal((byte)255, param.Value);
    }

    [Fact]
    public void CreateDbParameter_Short_SetsCorrectValueAndDbType()
    {
        var dialect = CreateDialect();
        var param = dialect.CreateDbParameter("s", DbType.Int16, (short)1000);
        Assert.Equal(DbType.Int16, param.DbType);
        Assert.Equal((short)1000, param.Value);
    }

    [Fact]
    public void CreateDbParameter_MismatchedType_StillThrows()
    {
        var dialect = CreateDialect();
        // string value with numeric DbType must still throw ArgumentException
        Assert.Throws<ArgumentException>(() =>
            dialect.CreateDbParameter("x", DbType.Int32, "not-a-number"));
    }

    [Fact]
    public void CreateDbParameter_NullString_SetsDBNull()
    {
        var dialect = CreateDialect();
        // Use explicit generic type argument to pass null for a reference type
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var param = dialect.CreateDbParameter<string>("s", DbType.String, null!);
#pragma warning restore CS8625
        Assert.Equal(DBNull.Value, param.Value);
    }
}
