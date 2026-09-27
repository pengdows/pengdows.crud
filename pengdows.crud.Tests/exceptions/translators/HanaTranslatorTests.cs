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

    // Connection failure (confirmed live 2026-09-27, saplabs/hanaexpress 2.00.088, Sap.Data.Hana.Net
    // 2.29.27): HanaException NativeError -10709 "Connection failed (RTE:[89006] ...)" both for a
    // closed port (rc=111 connection refused) and for the tenant's maximum_external_connections
    // limit (rc=104 connection reset by peer). The limit can't be told apart from any other failed
    // connect, so it is a plain ConnectionException, not TooManyConnectionsException.
    [Theory]
    [InlineData("Connection failed (RTE:[89006] System call 'connect' failed, rc=111:Connection refused {127.0.0.1:39999} (localhost:39999))")]
    [InlineData("Connection failed (RTE:[89006] System call 'recv' failed, rc=104:Connection reset by peer {172.17.0.1:46912 -> 172.17.0.2:39041} (172.17.0.1:46912 -> 172.17.0.2:39041))")]
    public void NativeError10709_ConnectionFailed_MapsTo_ConnectionException(string message)
    {
        var raw = new NumberedDbException(-10709, message);

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }
}
