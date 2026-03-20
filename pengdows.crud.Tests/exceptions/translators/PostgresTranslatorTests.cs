using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class PostgresTranslatorTests
{
    private readonly PostgresExceptionTranslator _translator = new();

    [Fact]
    public void SqlState23505_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new SqlStateDbException("23505", "duplicate key value violates unique constraint");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState23503_MapsTo_ForeignKeyViolationException()
    {
        var raw = new SqlStateDbException("23503", "insert or update violates foreign key constraint");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void SqlState23502_MapsTo_NotNullViolationException()
    {
        var raw = new SqlStateDbException("23502", "null value violates not-null constraint");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SqlState23514_MapsTo_CheckConstraintViolationException()
    {
        var raw = new SqlStateDbException("23514", "new row violates check constraint");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState40P01_MapsTo_DeadlockException()
    {
        var raw = new SqlStateDbException("40P01", "deadlock detected");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void SqlState40001_MapsTo_SerializationConflictException()
    {
        var raw = new SqlStateDbException("40001", "could not serialize access due to concurrent update");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    [Fact]
    public void TimeoutMessage_MapsTo_CommandTimeoutException()
    {
        var raw = new SqlStateDbException("57014", "canceling statement due to statement timeout");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
    }

    [Fact]
    public void UnknownSqlState_MapsTo_DatabaseOperationException()
    {
        var raw = new SqlStateDbException("XX000", "internal error");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Theory]
    [InlineData("08000")]
    [InlineData("08003")]
    [InlineData("08006")]
    [InlineData("08P01")]
    public void SqlState08xx_MapsTo_ConnectionException(string sqlState)
    {
        var raw = new SqlStateDbException(sqlState, "connection failure");

        var result = _translator.Translate(SupportedDatabase.PostgreSql, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.PostgreSql, result.Database);
        Assert.NotNull(result.InnerException);
    }
}
