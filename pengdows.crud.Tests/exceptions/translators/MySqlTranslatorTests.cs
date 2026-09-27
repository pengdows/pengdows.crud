using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class MySqlTranslatorTests
{
    private readonly MySqlExceptionTranslator _translator = new();
    private static ISqlDialect TestDialect(SupportedDatabase database) =>
        SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Theory]
    [InlineData(1062)]
    [InlineData(1169)]
    public void DuplicateErrors_MapTo_UniqueConstraintViolationException(int number)
    {
        var raw = new NumberedDbException(number, "duplicate entry");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Theory]
    [InlineData(1216)]
    [InlineData(1451)]
    [InlineData(1452)]
    public void ForeignKeyErrors_MapTo_ForeignKeyViolationException(int number)
    {
        var raw = new NumberedDbException(number, "foreign key constraint fails");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void Error1048_MapsTo_NotNullViolationException()
    {
        var raw = new NumberedDbException(1048, "Column cannot be null");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void Error3819_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(3819, "Check constraint is violated");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void Error1213_MapsTo_DeadlockException()
    {
        var raw = new NumberedDbException(1213, "Deadlock found when trying to get lock");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void Error1461_MapsTo_DatabaseOperationException()
    {
        var raw = new NumberedDbException(1461, "Can't create more than max_prepared_stmt_count statements");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
    }

    [Fact]
    public void UnknownError_MapsTo_DatabaseOperationException()
    {
        var raw = new NumberedDbException(9999, "unknown mysql error");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<ConcurrencyConflictException>(result);
    }

    [Theory]
    [InlineData(1042)]
    [InlineData(1043)]
    [InlineData(1044)]
    public void ConnectionErrorCode_MapsTo_ConnectionException(int number)
    {
        var raw = new NumberedDbException(number, "connection failure");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.MySql, result.Database);
        Assert.NotNull(result.InnerException);
    }

    // =========================================================================
    // Additional coverage for uncovered branches
    // =========================================================================

    /// <summary>Error code 1205 — InnoDB lock-wait timeout via numeric error code.</summary>
    [Fact]
    public void Error1205_MapsToTimeoutResult()
    {
        var raw = new NumberedDbException(1205, "Lock wait timeout exceeded; try restarting transaction");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Update);

        Assert.NotNull(result);
        Assert.IsAssignableFrom<DatabaseException>(result);
    }

    /// <summary>Error code 4025 — MariaDB check constraint violation.</summary>
    [Fact]
    public void Error4025_MapsTo_CheckConstraintViolationException()
    {
        var raw = new NumberedDbException(4025, "CONSTRAINT `chk_amount` failed for `shop`.`orders`");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    /// <summary>Message containing "constraint" and "failed for" — check constraint by message pattern.</summary>
    [Fact]
    public void MessagePattern_ConstraintFailedFor_MapsTo_CheckConstraintViolationException()
    {
        // No specific error code — relies on message pattern matching
        var raw = new NumberedDbException(0, "constraint failed for `orders`");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    /// <summary>Timeout detection via message text (LooksLikeTimeout path).</summary>
    [Fact]
    public void MessageContainingTimeout_IsTranslatedAsTimeout()
    {
        var raw = new SqlStateDbException("HY000",
            "Query execution was interrupted, maximum statement execution time exceeded (timeout)");

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.NotNull(result);
        Assert.IsAssignableFrom<DatabaseException>(result);
    }

    // Server connection limit (confirmed live 2026-09-27, mysql:latest and mariadb:latest with
    // --max-connections=3, MySqlConnector 2.4.0): MySqlException Number 1040 "Too many connections".
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void Error1040_TooManyConnections_MapsTo_TooManyConnectionsException(SupportedDatabase database)
    {
        var raw = new NumberedDbException(1040, "Too many connections");

        var result = _translator.Translate(TestDialect(database), raw, DbOperationKind.Query);

        Assert.IsType<TooManyConnectionsException>(result);
        Assert.IsAssignableFrom<ConnectionException>(result);
        Assert.True(result.IsTransient);
    }

    // Per-account limits (confirmed live 2026-09-27 on mysql:latest and mariadb:latest,
    // MySqlConnector 2.4.0): an account created WITH MAX_USER_CONNECTIONS n gets 1226 naming the
    // max_user_connections resource; the global max_user_connections gets 1203.
    [Theory]
    [InlineData(1226, "User 'app' has exceeded the 'max_user_connections' resource (current value: 3)")]
    [InlineData(1203, "User app2 already has more than 'max_user_connections' active connections")]
    public void PerAccountConnectionLimit_MapsTo_TooManyConnectionsException(int number, string message)
    {
        var raw = new NumberedDbException(number, message);

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.IsType<TooManyConnectionsException>(result);
        Assert.True(result.IsTransient);
    }

    // 1226 is also raised for the other per-account quotas, which are not a concurrent-connection
    // limit: max_connections_per_hour and max_questions/max_updates only clear with the hour.
    [Theory]
    [InlineData("User 'app' has exceeded the 'max_questions' resource (current value: 10)")]
    [InlineData("User 'app' has exceeded the 'max_updates' resource (current value: 10)")]
    [InlineData("User 'app' has exceeded the 'max_connections_per_hour' resource (current value: 10)")]
    public void Error1226_OtherResources_AreNot_TooManyConnectionsException(string message)
    {
        var raw = new NumberedDbException(1226, message);

        var result = _translator.Translate(TestDialect(SupportedDatabase.MySql), raw, DbOperationKind.Query);

        Assert.IsNotType<TooManyConnectionsException>(result);
    }
}
