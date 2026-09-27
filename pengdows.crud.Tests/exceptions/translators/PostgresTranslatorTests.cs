using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.enums;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class PostgresTranslatorTests
{
    private readonly PostgresExceptionTranslator _translator = new();
    private static ISqlDialect TestDialect(SupportedDatabase database) =>
        SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database), NullLogger.Instance);

    [Fact]
    public void SqlState23505_MapsTo_UniqueConstraintViolationException()
    {
        var raw = new SqlStateDbException("23505", "duplicate key value violates unique constraint");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Insert);

        Assert.IsType<UniqueConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState23503_MapsTo_ForeignKeyViolationException()
    {
        var raw = new SqlStateDbException("23503", "insert or update violates foreign key constraint");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Insert);

        Assert.IsType<ForeignKeyViolationException>(result);
    }

    [Fact]
    public void SqlState23502_MapsTo_NotNullViolationException()
    {
        var raw = new SqlStateDbException("23502", "null value violates not-null constraint");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Insert);

        Assert.IsType<NotNullViolationException>(result);
    }

    [Fact]
    public void SqlState23514_MapsTo_CheckConstraintViolationException()
    {
        var raw = new SqlStateDbException("23514", "new row violates check constraint");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Insert);

        Assert.IsType<CheckConstraintViolationException>(result);
    }

    [Fact]
    public void SqlState40P01_MapsTo_DeadlockException()
    {
        var raw = new SqlStateDbException("40P01", "deadlock detected");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Update);

        Assert.IsType<DeadlockException>(result);
    }

    [Fact]
    public void SqlState40001_MapsTo_SerializationConflictException()
    {
        var raw = new SqlStateDbException("40001", "could not serialize access due to concurrent update");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Update);

        Assert.IsType<SerializationConflictException>(result);
    }

    [Fact]
    public void SqlState40003_MapsTo_AmbiguousResultException()
    {
        // SQLSTATE 40003 (statement_completion_unknown) -- CockroachDB's real-world "result is
        // ambiguous" error, raised when its distributed consensus layer loses track of a commit's
        // outcome (network partition/node failure under contention). Standard PostgreSQL defines
        // but never actually raises this code; PostgresExceptionTranslator is shared across the
        // whole PostgreSql/CockroachDb/YugabyteDb/AuroraPostgreSql wire-protocol family, so this
        // classification lives here even though only CockroachDb is confirmed to trigger it.
        // Deliberately NOT SerializationConflictException: unlike 40001, retry is not
        // automatically safe here -- the write might have already applied.
        var raw = new SqlStateDbException("40003", "result is ambiguous (error=context canceled [exhausted])");

        var result = _translator.Translate(TestDialect(SupportedDatabase.CockroachDb), raw, DbOperationKind.Update);

        Assert.IsType<AmbiguousResultException>(result);
        Assert.IsNotType<SerializationConflictException>(result);
    }

    [Fact]
    public void TimeoutMessage_MapsTo_CommandTimeoutException()
    {
        var raw = new SqlStateDbException("57014", "canceling statement due to statement timeout");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Query);

        Assert.IsType<CommandTimeoutException>(result);
    }

    [Fact]
    public void UnknownSqlState_MapsTo_DatabaseOperationException()
    {
        var raw = new SqlStateDbException("XX000", "internal error");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Query);

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

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal(SupportedDatabase.PostgreSql, result.Database);
        Assert.NotNull(result.InnerException);
    }

    // Server connection limit (confirmed live 2026-09-27, postgres:latest with max_connections=6,
    // Npgsql 9.0.3): PostgresException SqlState "53300", message "53300: sorry, too many clients
    // already". Class 53 is "insufficient resources", not the 08 connection class, so it needs its
    // own check. CockroachDB and YugabyteDB report the same SQLSTATE through the same translator.
    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.CockroachDb)]
    [InlineData(SupportedDatabase.YugabyteDb)]
    public void SqlState53300_TooManyConnections_MapsTo_ConnectionException(SupportedDatabase database)
    {
        var raw = new SqlStateDbException("53300", "53300: sorry, too many clients already");

        var result = _translator.Translate(TestDialect(database), raw, DbOperationKind.Query);

        Assert.IsType<ConnectionException>(result);
        Assert.Equal("53300", result.SqlState);
    }

    // Other class-53 states are resource exhaustion inside a working session, not connection failures.
    [Theory]
    [InlineData("53100")]
    [InlineData("53200")]
    public void OtherInsufficientResourceStates_AreNot_ConnectionException(string sqlState)
    {
        var raw = new SqlStateDbException(sqlState, "insufficient resources");

        var result = _translator.Translate(TestDialect(SupportedDatabase.PostgreSql), raw, DbOperationKind.Query);

        Assert.IsNotType<ConnectionException>(result);
    }
}
