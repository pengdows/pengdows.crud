using System;
using pengdows.crud.exceptions.translators;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

// TryGetConstraintName falls back to the message text when no ConstraintName property exists.
// Only a quoted identifier (or Oracle's parenthesised form) names a constraint; an unquoted
// English phrase after the word "constraint" does not.
public class ConstraintNameExtractionTests
{
    [Theory]
    [InlineData("duplicate key value violates unique constraint \"users_email_key\"", "users_email_key")]
    [InlineData("Violation of UNIQUE KEY constraint 'UQ__Orders__1'. Cannot insert duplicate key", "UQ__Orders__1")]
    [InlineData("violation of PRIMARY or UNIQUE KEY constraint \"PK_T\" on table \"T\"", "PK_T")]
    [InlineData("ORA-02290: check constraint (SCOTT.CHK_X) violated", "CHK_X")]
    [InlineData("ORA-00001: unique constraint (SCOTT.PK_JOBS) violated", "PK_JOBS")]
    public void TryGetConstraintName_QuotedOrOracleForm_ReturnsTheName(string message, string expected)
    {
        Assert.Equal(expected, DbExceptionTranslationSupport.TryGetConstraintName(new Exception(message)));
    }

    [Theory]
    [InlineData("Cannot add or update a child row: a foreign key constraint fails (`db`.`t`)")]
    [InlineData("Violation of PRIMARY KEY constraint PK_Orders. Cannot insert duplicate key")]
    [InlineData("the constraint is violated")]
    public void TryGetConstraintName_UnquotedPhrase_ReturnsNull(string message)
    {
        Assert.Null(DbExceptionTranslationSupport.TryGetConstraintName(new Exception(message)));
    }
}
