using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class SnowflakeTranslatorTests
{
    [Fact]
    public void SnowflakeTranslator_NeverEmitsUniqueConstraintViolationException()
    {
        var translator = new PostgresExceptionTranslator();
        var raw = new SqlStateDbException("23505", "duplicate key value violates unique constraint");

        var result = translator.Translate(SupportedDatabase.Snowflake, raw, DbOperationKind.Insert);

        Assert.IsType<DatabaseOperationException>(result);
        Assert.IsNotType<UniqueConstraintViolationException>(result);
    }
}
