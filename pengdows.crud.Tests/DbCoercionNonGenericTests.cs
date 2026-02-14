using System;
using System.Data;
using Moq;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests the DbCoercion&lt;T&gt; non-generic IDbCoercion interface methods
/// that delegate to the typed implementations.
/// </summary>
public class DbCoercionNonGenericTests
{
    [Fact]
    public void TryRead_ExactTargetType_DelegatesToTyped()
    {
        IDbCoercion coercion = new GuidCoercion();
        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid);

        var success = coercion.TryRead(dbValue, typeof(Guid), out var result);

        Assert.True(success);
        Assert.Equal(guid, result);
    }

    [Fact]
    public void TryRead_NullableTargetType_ReturnsFalse()
    {
        // DbCoercion<T> without struct constraint treats T? as T (nullable annotation only),
        // so typeof(T?) == typeof(T) at runtime. Passing typeof(Nullable<Guid>) won't match.
        IDbCoercion coercion = new GuidCoercion();
        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid);

        var success = coercion.TryRead(dbValue, typeof(Guid?), out var result);

        // Nullable<Guid> != Guid at the Type level, so this returns false
        Assert.False(success);
    }

    [Fact]
    public void TryRead_WrongTargetType_ReturnsFalse()
    {
        IDbCoercion coercion = new GuidCoercion();
        var guid = Guid.NewGuid();
        var dbValue = new DbValue(guid);

        var success = coercion.TryRead(dbValue, typeof(string), out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryRead_WhenTypedFails_ReturnsFalse()
    {
        IDbCoercion coercion = new GuidCoercion();
        var dbValue = new DbValue(null);

        var success = coercion.TryRead(dbValue, typeof(Guid), out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TryWrite_WithTypedValue_Delegates()
    {
        IDbCoercion coercion = new GuidCoercion();
        var guid = Guid.NewGuid();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        var success = coercion.TryWrite(guid, param.Object);

        Assert.True(success);
        param.VerifySet(p => p.Value = guid, Times.Once);
        param.VerifySet(p => p.DbType = DbType.Guid, Times.Once);
    }

    [Fact]
    public void TryWrite_WithNull_DelegatesDefault()
    {
        IDbCoercion coercion = new GuidCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        var success = coercion.TryWrite(null, param.Object);

        Assert.True(success);
        param.VerifySet(p => p.Value = Guid.Empty, Times.Once);
    }

    [Fact]
    public void TryWrite_WrongType_ReturnsFalse()
    {
        IDbCoercion coercion = new GuidCoercion();
        var param = new Mock<System.Data.Common.DbParameter>();
        param.SetupAllProperties();

        var success = coercion.TryWrite("not a guid", param.Object);

        Assert.False(success);
    }

    [Fact]
    public void TargetType_ReturnsCorrectType()
    {
        IDbCoercion coercion = new GuidCoercion();
        Assert.Equal(typeof(Guid), coercion.TargetType);
    }
}
