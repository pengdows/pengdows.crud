using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class InformixTranslatorTests
{
    private readonly InformixExceptionTranslator _translator = new();

    [Fact]
    public void SqlState23000_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new SqlStateDbException("23000", "duplicate value for a column with unique constraint");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.Informix, result.Database);
        Assert.Same(raw, result.InnerException);
    }

    [Fact]
    public void ErrorCodeMinus268_LoggedDatabase_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(-268, "ISAM error: duplicate value for a column with unique constraint");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus239_UnloggedDatabase_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new NumberedDbException(-239, "duplicate value for a column with unique constraint");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus691_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(-691, "Missing key for referential constraint");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus692_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(-692, "Key value for constraint is still being referenced");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Delete);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus391_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(-391, "Column has a NOT NULL constraint");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void ErrorCodeMinus530_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(-530, "Check constraint violated");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState08004_MapsTo_ConnectionException()
    {
        var raw = new SqlStateDbException("08004", "Unable to connect to database server");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void ErrorCodeMinus143_MapsTo_DeadlockException()
    {
        var raw = new NumberedDbException(-143, "ISAM error: deadlock detected");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public void ErrorCodeMinus244_MapsTo_SerializationConflictException()
    {
        var raw = new NumberedDbException(-244, "Could not do a physical-order read to fetch next row");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    // -908/-27001/-27002 are documented connection/communication failure codes (IBM docs).
    // This backport's InformixExceptionTranslator checks these numeric codes directly at the top
    // of Translate() alongside the SQLSTATE "08" prefix check - a deliberate strengthening over
    // relying on SqlState alone, since not every provider populates SqlState reliably (2.0.6's
    // Db2ExceptionTranslator documents the same concern for IBM.Data.Db2).
    [Theory]
    [InlineData(-908)]
    [InlineData(-27001)]
    [InlineData(-27002)]
    public void CommunicationFailureCodes_MapTo_ConnectionException(int code)
    {
        var raw = new NumberedDbException(code, "communication failure");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void UnknownError_MapsTo_GenericDatabaseOperationException()
    {
        var raw = new NumberedDbException(99999, "some unrecognized Informix failure");

        var result = _translator.Translate(SupportedDatabase.Informix, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Fact]
    public void Registry_Routes_Informix_To_InformixExceptionTranslator()
    {
        var registry = new DbExceptionTranslatorRegistry();

        Assert.IsType<InformixExceptionTranslator>(registry.Get(SupportedDatabase.Informix));
    }

    private sealed class NumberedDbException : DbException
    {
        public int Number { get; }

        public NumberedDbException(int number, string message) : base(message)
        {
            Number = number;
        }
    }

    private sealed class SqlStateDbException : DbException
    {
        public SqlStateDbException(string sqlState, string message) : base(message)
        {
            SqlState = sqlState;
        }

        public override string? SqlState { get; }
    }
}
