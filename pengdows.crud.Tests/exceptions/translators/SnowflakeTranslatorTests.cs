using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SnowflakeTranslatorTests
{
    // Snowflake parses UNIQUE/PRIMARY KEY constraint DDL but never enforces it at runtime
    // (SnowflakeDialect.SupportsUniqueConstraints = false), so a Postgres-style "23505" sqlState
    // must never be misclassified as UniqueConstraintViolationException for Snowflake — it isn't
    // a real, reachable Snowflake error shape. DbExceptionTranslatorRegistry now routes Snowflake
    // to a dedicated SnowflakeExceptionTranslator (not PostgresExceptionTranslator) precisely to
    // keep this guarantee explicit rather than incidental.
    [Fact]
    public void SnowflakeTranslator_NeverEmitsUniqueConstraintViolationException()
    {
        var translator = new SnowflakeExceptionTranslator();
        var raw = new SqlStateDbException("23505", "duplicate key value violates unique constraint");

        var result = translator.Translate(SupportedDatabase.Snowflake, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }
}
