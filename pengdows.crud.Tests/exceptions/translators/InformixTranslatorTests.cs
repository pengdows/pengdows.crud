using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class InformixTranslatorTests
{
    private readonly InformixExceptionTranslator _translator = new();

    private static ISqlDialect TestDialect() =>
        SqlDialectFactory.CreateDialectForType(SupportedDatabase.Informix, new fakeDbFactory(SupportedDatabase.Informix), NullLogger.Instance);

    [Fact]
    public void SqlState23000_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new SqlStateDbException("23000", "duplicate value for a column with unique constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Informix, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ErrorCodeMinus268_LoggedDatabase_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(-268, "ISAM error: duplicate value for a column with unique constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus239_UnloggedDatabase_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(-239, "duplicate value for a column with unique constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus691_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(-691, "Missing key for referential constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus692_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(-692, "Key value for constraint is still being referenced");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus391_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(-391, "Column has a NOT NULL constraint");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus530_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(-530, "Check constraint violated");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState08004_MapsTo_ConnectionException_BeforeConstraintDelegation()
    {
        var raw = new SqlStateDbException("08004", "Unable to connect to database server");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void ErrorCodeMinus143_MapsTo_DeadlockException()
    {
        var raw = new NumberedDbException(-143, "ISAM error: deadlock detected");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void ErrorCodeMinus244_MapsTo_SerializationConflictException()
    {
        var raw = new NumberedDbException(-244, "Could not do a physical-order read to fetch next row");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    // -908/-27001/-27002 are documented connection/communication failure codes (IBM docs). 2.0.6's
    // translator checks them directly alongside the SQLSTATE "08" prefix — not every provider
    // populates SqlState reliably — so they map to ConnectionException. (3.0 lets them fall
    // through to the generic DatabaseOperationException; kept 2.0.6's stronger behavior.)
    [Theory]
    [InlineData(-908)]
    [InlineData(-27001)]
    [InlineData(-27002)]
    public void CommunicationFailureCodes_MapTo_ConnectionException(int code)
    {
        var raw = new NumberedDbException(code, "communication failure");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void UnknownError_MapsTo_GenericDatabaseOperationException()
    {
        var raw = new NumberedDbException(99999, "some unrecognized Informix failure");

        var result = _translator.Translate(TestDialect(), raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Registry_Routes_Informix_To_InformixExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<InformixExceptionTranslator>(registry.Get(SupportedDatabase.Informix));
    }
}
