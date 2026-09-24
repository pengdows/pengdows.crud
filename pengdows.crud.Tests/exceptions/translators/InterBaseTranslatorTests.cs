using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class InterBaseTranslatorTests
{
    private readonly InterBaseExceptionTranslator _translator = new();

    private static ISqlDialect TestDialect() =>
        SqlDialectFactory.CreateDialectForType(SupportedDatabase.InterBase, new fakeDbFactory(SupportedDatabase.InterBase), NullLogger.Instance);

    [Fact]
    public void ErrorCode335544665_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(335544665, "violation of PRIMARY or UNIQUE KEY constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.InterBase, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ErrorCode335544466_MapsTo_ForeignKeyViolationException_OnInsert()
    {
        var raw = new NumberedDbException(335544466, "violation of FOREIGN KEY constraint, missing parent");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544466_MapsTo_ForeignKeyViolationException_OnDelete()
    {
        var raw = new NumberedDbException(335544466, "violation of FOREIGN KEY constraint, referencing child rows");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544347_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(335544347, "validation error for column NAME, value \"*** null ***\"");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void ErrorCode335544558_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(335544558, "operation violates CHECK constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    // InterBaseDialect has no TryClassifyProviderException override (unlike Informix/Hana), so
    // deadlock/timeout classification for InterBase falls all the way through to
    // SqlDialect.ClassifyException's generic message-keyword fallback.
    [Fact]
    public void DeadlockLikeMessage_MapsTo_DeadlockException_ViaGenericMessageFallback()
    {
        var raw = new NumberedDbException(1, "deadlock");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void UnknownError_MapsTo_GenericDatabaseOperationException()
    {
        var raw = new NumberedDbException(99999, "some unrecognized InterBase failure");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Registry_Routes_InterBase_To_InterBaseExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<InterBaseExceptionTranslator>(registry.Get(SupportedDatabase.InterBase));
    }
}
