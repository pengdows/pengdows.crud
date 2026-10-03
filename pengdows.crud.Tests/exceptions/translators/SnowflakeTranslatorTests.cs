using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SnowflakeTranslatorTests
{
    private static SnowflakeDialect Dialect() => new(new fakeDbFactory(SupportedDatabase.Snowflake), NullLogger.Instance);

    // Standard Snowflake tables parse constraint DDL but don't enforce it; hybrid tables enforce
    // PRIMARY KEY, UNIQUE and FOREIGN KEY (REV-065). Error shapes from Snowflake's documentation
    // (CREATE HYBRID TABLE; hybrid tables tutorial) — hybrid tables aren't available to the trial
    // account the integration tests use, so these aren't confirmed live.
    [Theory]
    [InlineData(200001, "Primary key already exists")]
    [InlineData(0, "Duplicate key value violates unique constraint \"SYS_INDEX_MYTABLE_UNIQUE_EMAIL\"")]
    public void HybridTableKeyViolation_IsUniqueConstraintViolation(int code, string message)
    {
        var raw = new NumberedDbException(code, message);

        Assert.IsType<UniqueConstraintViolationException>(
            new SnowflakeExceptionTranslator().Translate(Dialect(), raw, DbOperationKind.Insert));
    }

    [Theory]
    [InlineData(200009, "Foreign key constraint \"SYS_INDEX_PLAYER_FOREIGN_KEY_TEAM_ID_TEAM_TEAM_ID\" was violated.")]
    [InlineData(0, "Foreign keys that reference key values still exist.")]
    public void HybridTableForeignKeyViolation_IsForeignKeyViolation(int code, string message)
    {
        var raw = new NumberedDbException(code, message);

        Assert.IsType<ForeignKeyViolationException>(
            new SnowflakeExceptionTranslator().Translate(Dialect(), raw, DbOperationKind.Insert));
    }

    // A Postgres-style SQLSTATE alone is not a Snowflake unique-violation signal.
    [Fact]
    public void SqlState23505_WithoutASnowflakeShape_IsNotAUniqueViolation()
    {
        var raw = new SqlStateDbException("23505", "some other failure");

        Assert.IsNotType<UniqueConstraintViolationException>(
            new SnowflakeExceptionTranslator().Translate(Dialect(), raw, DbOperationKind.Insert));
    }
}
