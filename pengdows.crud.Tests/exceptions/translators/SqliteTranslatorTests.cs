using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SqliteTranslatorTests
{
    private readonly SqliteExceptionTranslator _translator = new();

    [Theory]
    [InlineData("UNIQUE constraint failed")]
    [InlineData("PRIMARY KEY constraint failed")]
    public void UniqueMessages_MapTo_UniqueConstraintViolationException(string message)
    {
        var raw = new SqliteMessageDbException(message);

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void ForeignKeyMessage_MapsTo_ForeignKeyViolationException()
    {
        var raw = new SqliteMessageDbException("FOREIGN KEY constraint failed");

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void NotNullMessage_MapsTo_NotNullViolationException()
    {
        var raw = new SqliteMessageDbException("NOT NULL constraint failed: jobs.name");

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void CheckMessage_MapsTo_CheckConstraintViolationException()
    {
        var raw = new SqliteMessageDbException("CHECK constraint failed: jobs");

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void UnknownConstraintCode19_DoesNotMapTo_ConcurrencyConflictException()
    {
        var raw = new SqliteMessageDbException("constraint failed");

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Theory]
    [InlineData(14)] // SQLITE_CANTOPEN
    [InlineData(26)] // SQLITE_NOTADB
    public void SqliteConnectionErrorCode_MapsTo_ConnectionException(int errorCode)
    {
        var raw = new NumberedDbException(errorCode, "unable to open database file");

        var result = _translator.Translate(SupportedDatabase.Sqlite, raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.Sqlite, result.Database);
        Assert.NotNull(result.InnerException);
    }
}
