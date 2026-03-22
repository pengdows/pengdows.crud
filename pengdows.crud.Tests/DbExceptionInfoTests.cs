using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DbExceptionInfoTests
{
    [Fact]
    public void DbConstraintKind_HasExpectedValues()
    {
        _ = DbConstraintKind.None;
        _ = DbConstraintKind.Unique;
        _ = DbConstraintKind.ForeignKey;
        _ = DbConstraintKind.NotNull;
        _ = DbConstraintKind.Check;
        _ = DbConstraintKind.Unknown;
    }

    [Fact]
    public void SqlServer_AnalyzeException_ReturnsUniqueConstraintInfo()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);

        var info = dialect.AnalyzeException(new NumberedDbException(2627, "Violation of PRIMARY KEY constraint"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.Unique, info.ConstraintKind);
        Assert.False(info.IsTransient);
        Assert.False(info.IsRetryable);
        Assert.Equal(2627, info.ProviderErrorCode);
    }

    [Fact]
    public void SqlServer_AnalyzeException_ReturnsForeignKeyConstraintInfo()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);

        var info = dialect.AnalyzeException(new NumberedDbException(
            547, "The INSERT statement conflicted with the FOREIGN KEY constraint"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.ForeignKey, info.ConstraintKind);
    }

    [Fact]
    public void PostgreSql_AnalyzeException_ReturnsSpecificConstraintKindFromSqlState()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);

        var info = dialect.AnalyzeException(new SqlStateDbException(
            "23502", "null value in column violates not-null constraint"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.NotNull, info.ConstraintKind);
        Assert.Equal("23502", info.SqlState);
    }

    [Fact]
    public void PostgreSql_AnalyzeException_ReturnsRetryableInfoForSerializationFailure()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);

        var info = dialect.AnalyzeException(new SqlStateDbException(
            "40001", "could not serialize access due to read/write dependencies"));

        Assert.Equal(DbErrorCategory.SerializationFailure, info.Category);
        Assert.Equal(DbConstraintKind.None, info.ConstraintKind);
        Assert.True(info.IsTransient);
        Assert.True(info.IsRetryable);
        Assert.Equal("40001", info.SqlState);
    }

    [Fact]
    public void MySql_AnalyzeException_ReturnsCheckConstraintInfo()
    {
        var dialect = CreateDialect(SupportedDatabase.MySql);

        var info = dialect.AnalyzeException(new NumberedDbException(
            3819, "Check constraint 'chk_value_positive' is violated."));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.Check, info.ConstraintKind);
    }

    [Fact]
    public void Oracle_AnalyzeException_ReturnsNotNullConstraintInfo()
    {
        var dialect = CreateDialect(SupportedDatabase.Oracle);

        var info = dialect.AnalyzeException(new NumberedDbException(
            1400, "ORA-01400: cannot insert NULL into"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.NotNull, info.ConstraintKind);
    }

    [Fact]
    public void Sqlite_AnalyzeException_RecognizesForeignKeyAndCheckMessages()
    {
        var dialect = CreateDialect(SupportedDatabase.Sqlite);

        var foreignKey = dialect.AnalyzeException(new SqliteExtendedDbException(
            787, "FOREIGN KEY constraint failed"));
        var check = dialect.AnalyzeException(new SqliteExtendedDbException(
            275, "CHECK constraint failed: value >= 0"));

        Assert.Equal(DbConstraintKind.ForeignKey, foreignKey.ConstraintKind);
        Assert.Equal(DbConstraintKind.Check, check.ConstraintKind);
    }

    [Fact]
    public void AnalyzeException_UnknownConstraintKind_ReturnsUnknownKind()
    {
        var dialect = CreateDialect(SupportedDatabase.SqlServer);

        var info = dialect.AnalyzeException(new NumberedDbException(
            547, "The UPDATE statement conflicted with the SOME OTHER constraint"));

        Assert.Equal(DbErrorCategory.ConstraintViolation, info.Category);
        Assert.Equal(DbConstraintKind.Unknown, info.ConstraintKind);
    }

    private static pengdows.crud.dialects.ISqlDialect CreateDialect(SupportedDatabase db)
    {
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));
        return ctx.GetDialect();
    }

    private sealed class NumberedDbException : DbException
    {
        public NumberedDbException(int number, string message)
            : base(message)
        {
            Number = number;
        }

        public int Number { get; }
    }

    private sealed class SqlStateDbException : DbException
    {
        public SqlStateDbException(string sqlState, string message)
            : base(message)
        {
            SqlState = sqlState;
        }

        public override string? SqlState { get; }
    }

    private sealed class SqliteExtendedDbException : DbException
    {
        public SqliteExtendedDbException(int errorCode, string message)
            : base(message)
        {
            HResult = errorCode;
        }
    }
}
