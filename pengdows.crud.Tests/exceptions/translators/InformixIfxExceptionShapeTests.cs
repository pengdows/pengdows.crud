using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

/// <summary>
/// CONFIRMED live (Informix 15, Informix.Net.Core-lnx): every integrity violation is an IfxException
/// with SQLSTATE 23000 whose DbException.ErrorCode is only the HRESULT (-2146232009); the real Informix
/// code is <c>Errors[0].NativeError</c> (-268 unique, -530 check, -391 not null, -691/-692 foreign
/// key). This mirrors that shape (no top-level Number/NativeError property). Every violation used to
/// be reported as a unique violation because only SQLSTATE 23000 was ever seen.
/// </summary>
public class InformixIfxExceptionShapeTests
{
    private const int IfxHResult = -2146232009;

    private sealed class IfxErrorShape
    {
        public IfxErrorShape(int nativeError, string message)
        {
            NativeError = nativeError;
            Message = message;
        }

        public string Message { get; }
        public string SQLState => "23000";
        public int NativeError { get; }
    }

    private sealed class IfxExceptionShape : DbException
    {
        public IfxExceptionShape(int nativeError, string message) : base("ERROR [23000] " + message)
        {
            HResult = IfxHResult;
            Errors = new List<IfxErrorShape> { new(nativeError, message) };
        }

        public List<IfxErrorShape> Errors { get; }
        public override string SqlState => "23000";
    }

    private static ISqlDialect Dialect() =>
        SqlDialectFactory.CreateDialectForType(SupportedDatabase.Informix,
            new fakeDbFactory(SupportedDatabase.Informix), NullLogger.Instance);

    public static IEnumerable<object[]> Violations() => new[]
    {
        new object[] { -268, "Unique constraint (informix.u100_1) violated.", typeof(UniqueConstraintViolationException), DbConstraintKind.Unique },
        new object[] { -530, "Check constraint (informix.c100_4) failed.", typeof(CheckConstraintViolationException), DbConstraintKind.Check },
        new object[] { -391, "Cannot insert a null into column (p1.v).", typeof(NotNullViolationException), DbConstraintKind.NotNull },
        new object[] { -691, "Missing key in referenced table for referential constraint (informix.r101_6).", typeof(ForeignKeyViolationException), DbConstraintKind.ForeignKey },
        new object[] { -692, "Key value for constraint (informix.u100_1) is still being referenced.", typeof(ForeignKeyViolationException), DbConstraintKind.ForeignKey }
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public void Translate_UsesTheNativeErrorFromTheErrorsCollection(int nativeError, string message, Type expected,
        DbConstraintKind _)
    {
        var raw = new IfxExceptionShape(nativeError, message);

        var result = new InformixExceptionTranslator().Translate(Dialect(), raw, DbOperationKind.Insert);

        Assert.IsType(expected, result);
        Assert.Equal(nativeError, result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(Violations))]
    public void AnalyzeException_ReportsTheConstraintKindOfTheNativeError(int nativeError, string message,
        Type _, DbConstraintKind expectedKind)
    {
        var info = Dialect().AnalyzeException(new IfxExceptionShape(nativeError, message));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(expectedKind, info.ConstraintKind);
        Assert.Equal(nativeError, info.ProviderErrorCode);
    }

    [Fact]
    public void Translate_MapsSessionLimitFromTheErrorsCollectionToTooManyConnections()
    {
        var raw = new IfxExceptionShape(-25571, "Cannot create a user thread.");

        var result = new InformixExceptionTranslator().Translate(Dialect(), raw, DbOperationKind.Query);

        var tooMany = Assert.IsType<TooManyConnectionsException>(result);
        Assert.True(tooMany.IsTransient);
        Assert.Equal(-25571, tooMany.ErrorCode);
    }
}
