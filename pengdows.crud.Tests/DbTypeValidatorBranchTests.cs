using System;
using System.Data;
using Xunit;

namespace pengdows.crud.Tests;

public class DbTypeValidatorBranchTests
{
    [Fact]
    public void Validate_Type_EnumWithUnsupportedDbType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Boolean, typeof(DayOfWeek)));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Type_UnknownDbType_WithNumericType_Throws()
    {
        var unknown = (DbType)9999;

        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(unknown, typeof(int)));
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public void Validate_Type_UnknownDbType_WithNonNumericType_DoesNotThrow()
    {
        var unknown = (DbType)9999;

        DbTypeValidator.Validate(unknown, typeof(DateTime));
    }

    [Fact]
    public void Validate_Object_EnumWithUnsupportedDbType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Boolean, DayOfWeek.Monday));
        Assert.Contains("enum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Object_AcceptableMapMismatch_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Guid, DateTime.UtcNow));
        Assert.Contains("DateTime", ex.Message);
    }

    [Fact]
    public void Validate_Object_UnknownDbType_WithNumericValue_Throws()
    {
        var unknown = (DbType)9999;

        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(unknown, 123m));
        Assert.Contains("Decimal", ex.Message);
    }

    [Fact]
    public void Validate_Object_UnknownDbType_WithNonNumericValue_DoesNotThrow()
    {
        var unknown = (DbType)9999;

        DbTypeValidator.Validate(unknown, "pass-through");
        DbTypeValidator.Validate(unknown, DBNull.Value);
    }

    [Fact]
    public void Validate_Object_EnumWithNumericDbType_DoesNotThrow()
    {
        DbTypeValidator.Validate(DbType.Int32, DayOfWeek.Monday);
    }

    [Fact]
    public void Validate_Object_DbTypeObject_AcceptsAnyType()
    {
        DbTypeValidator.Validate(DbType.Object, new object());
    }

    [Fact]
    public void Validate_Object_NumericDbType_WithNumericValue_DoesNotThrow()
    {
        DbTypeValidator.Validate(DbType.Int64, 123);
    }

    [Fact]
    public void Validate_Object_NumericDbType_WithNonNumeric_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => DbTypeValidator.Validate(DbType.Int32, "abc"));
        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public void Validate_Object_AcceptableMappedType_DoesNotThrow()
    {
        DbTypeValidator.Validate(DbType.String, "ok");
    }
}
