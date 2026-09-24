using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class HanaTranslatorTests
{
    private readonly HanaExceptionTranslator _translator = new();

    private static ISqlDialect TestDialect() =>
        SqlDialectFactory.CreateDialectForType(SupportedDatabase.SapHana, new fakeDbFactory(SupportedDatabase.SapHana), NullLogger.Instance);

    [Fact]
    public void NativeError301_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(301, "unique constraint violated");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.SapHana, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void NativeError461_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(461, "insert/update violates foreign key constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void NativeError462_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(462, "delete/update violates foreign key constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void NativeError287_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(287, "cannot insert NULL value");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void NativeError677_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(677, "check condition violated");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void NativeError133_MapsTo_DeadlockException()
    {
        var raw = new NumberedDbException(133, "transaction rolled back by detected deadlock");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void NativeError131_MapsTo_CommandTimeoutException()
    {
        var raw = new NumberedDbException(131, "transaction rolled back by lock wait timeout");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void UnknownError_MapsTo_GenericDatabaseOperationException()
    {
        var raw = new NumberedDbException(99999, "some unrecognized HANA failure");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Registry_Routes_SapHana_To_HanaExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<HanaExceptionTranslator>(registry.Get(SupportedDatabase.SapHana));
    }

    // Kept from 2.0.6 (not in 3.0): 129 is the live-confirmed NativeError for a write on a
    // read-only HANA transaction.
    [Fact]
    public void NativeError129_MapsTo_ReadOnlyViolationException()
    {
        var raw = new NumberedDbException(129,
            "cannot change this transaction's access mode from read-only to update directly");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<ReadOnlyViolationException>(result);
    }
}
