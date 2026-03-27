using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SqlServerTranslatorTests
{
    private readonly SqlServerExceptionTranslator _translator = new();

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void SqlError_MapsTo_UniqueConstraintViolationException(int number)
    {
        var raw = new NumberedDbException(number, "duplicate key");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
        Assert.Equal(SupportedDatabase.SqlServer, result.Database);
        Assert.NotNull(result.InnerException);
    }

    [Fact]
    public void SqlError547_WithForeignKeyMessage_MapsTo_ForeignKeyViolationException()
    {
        var raw = new NumberedDbException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void SqlError515_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(515, "Cannot insert the value NULL");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SqlError547_WithCheckMessage_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(547, "The INSERT statement conflicted with the CHECK constraint");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlError1205_MapsTo_DeadlockException()
    {
        var raw = new NumberedDbException(1205, "deadlock victim");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void SqlTimeout_MapsTo_CommandTimeoutException()
    {
        var raw = new NumberedDbException(-2, "Execution timeout expired");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
    }

    [Fact]
    public void UnknownSqlError_MapsTo_DatabaseOperationException()
    {
        var raw = new NumberedDbException(50000, "some database failure");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Theory]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(233)]
    [InlineData(10061)]
    public void ConnectionErrorCode_MapsTo_ConnectionException(int number)
    {
        var raw = new NumberedDbException(number, "connection failure");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.SqlServer, result.Database);
        Assert.NotNull(result.InnerException);
    }

    [Theory]
    [InlineData("The connection is closed")]
    [InlineData("The connection is broken")]
    [InlineData("Connection has been closed")]
    public void ConnectionMessage_Closed_Or_Broken_MapsTo_ConnectionException(string message)
    {
        var raw = new NumberedDbException(50000, message);

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
    }

    [Fact]
    public void Timeout_ByLooksLikeTimeout_Maps_CommandTimeoutException()
    {
        // TimeoutException is not a DbException so errorCode = null (not -2).
        // LooksLikeTimeout fires first via the `exception is TimeoutException` branch.
        var raw = new TimeoutException("wait for lock expired");

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
        Assert.True(result.IsTransient);
    }

    // Regression: distributed-lock resource names contain "timeout" (e.g. "lock-timeout-<guid>"),
    // which caused LooksLikeTimeout to fire before the PK-violation error code was checked,
    // translating a UniqueConstraintViolationException as a CommandTimeoutException.
    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void PkViolation_WithTimeoutInResourceName_MapsTo_UniqueConstraintViolationException(int errorCode)
    {
        var msg = $"Violation of PRIMARY KEY constraint 'PK_HangFire_hf_lock'. " +
                  $"Cannot insert duplicate key in object 'HangFire.hf_lock'. " +
                  $"The duplicate key value is (lock-timeout-8daaef85-02cb-4691-b8b4-187049fc3618).";
        var raw = new NumberedDbException(errorCode, msg);

        var result = _translator.Translate(SupportedDatabase.SqlServer, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }
}
