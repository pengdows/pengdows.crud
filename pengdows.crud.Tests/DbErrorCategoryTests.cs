using System;
using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for DbErrorCategory enum existence, values, and dialect classification.
/// </summary>
public class DbErrorCategoryTests
{
    [Fact]
    public void DbErrorCategory_None_IsZero()
    {
        Assert.Equal(0, (int)DbErrorCategory.None);
    }

    [Fact]
    public void DbErrorCategory_HasExpectedValues()
    {
        // Verify all expected categories exist
        _ = DbErrorCategory.None;
        _ = DbErrorCategory.Deadlock;
        _ = DbErrorCategory.SerializationFailure;
        _ = DbErrorCategory.ConstraintViolation;
        _ = DbErrorCategory.Timeout;
        _ = DbErrorCategory.Unknown;
    }

    [Fact]
    public void SqlDialect_ClassifyException_UnknownException_ReturnsUnknown()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = new InvalidOperationException("Something went wrong");
        var category = dialect.ClassifyException(ex);

        Assert.Equal(DbErrorCategory.Unknown, category);
    }

    [Fact]
    public void SqlDialect_ClassifyException_OperationCanceledException_ReturnsNone()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = new OperationCanceledException();
        var category = dialect.ClassifyException(ex);

        // Cancellations are tracked separately via CommandCancelled — not classified here
        Assert.Equal(DbErrorCategory.None, category);
    }

    [Theory]
    [InlineData("deadlock detected")]
    [InlineData("DEADLOCK found")]
    [InlineData("Deadlock victim")]
    public void SqlDialect_ClassifyException_DeadlockMessage_ReturnsDeadlock(string message)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = CreateDbExceptionWithMessage(message);
        var category = dialect.ClassifyException(ex);

        Assert.Equal(DbErrorCategory.Deadlock, category);
    }

    [Theory]
    [InlineData("serialization failure")]
    [InlineData("could not serialize access")]
    [InlineData("Serialization failure")]
    public void SqlDialect_ClassifyException_SerializationMessage_ReturnsSerializationFailure(string message)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = CreateDbExceptionWithMessage(message);
        var category = dialect.ClassifyException(ex);

        Assert.Equal(DbErrorCategory.SerializationFailure, category);
    }

    [Theory]
    [InlineData("unique constraint")]
    [InlineData("UNIQUE constraint failed")]
    [InlineData("foreign key constraint")]
    [InlineData("violates not-null constraint")]
    [InlineData("constraint violation")]
    public void SqlDialect_ClassifyException_ConstraintMessage_ReturnsConstraintViolation(string message)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = CreateDbExceptionWithMessage(message);
        var category = dialect.ClassifyException(ex);

        Assert.Equal(DbErrorCategory.ConstraintViolation, category);
    }

    [Theory]
    [InlineData("timeout expired")]
    [InlineData("command timeout")]
    [InlineData("operation timed out")]
    public void SqlDialect_ClassifyException_TimeoutMessage_ReturnsTimeout(string message)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var ctx = ConnectionFailureHelper.CreateFailOnCommandContext();
        var dialect = ctx.GetDialect();

        var ex = CreateDbExceptionWithMessage(message);
        var category = dialect.ClassifyException(ex);

        Assert.Equal(DbErrorCategory.Timeout, category);
    }

    private static DbException CreateDbExceptionWithMessage(string message)
    {
        return ConnectionFailureHelper.CommonExceptions.CreateDbException(message);
    }

    [Fact]
    public void SqlServer_IsUniqueViolation_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer));
        var dialect = ctx.GetDialect();

        Assert.True(dialect.IsUniqueViolation(new NumberedDbException(2627, "Violation of PRIMARY KEY constraint")));
        Assert.True(dialect.IsUniqueViolation(new NumberedDbException(2601, "Cannot insert duplicate key row")));
        Assert.False(dialect.IsUniqueViolation(new NumberedDbException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint")));
    }

    [Fact]
    public void PostgreSql_IsUniqueViolation_UsesSqlState()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        var dialect = ctx.GetDialect();

        Assert.True(dialect.IsUniqueViolation(new SqlStateDbException("23505", "duplicate key value violates unique constraint")));
        Assert.False(dialect.IsUniqueViolation(new SqlStateDbException("23503", "insert or update on table violates foreign key constraint")));
    }

    [Fact]
    public void MySql_IsUniqueViolation_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=MySql",
            new fakeDbFactory(SupportedDatabase.MySql));
        var dialect = ctx.GetDialect();

        Assert.True(dialect.IsUniqueViolation(new NumberedDbException(1062, "Duplicate entry for key")));
        Assert.False(dialect.IsUniqueViolation(new NumberedDbException(1452, "Cannot add or update a child row: a foreign key constraint fails")));
    }

    [Fact]
    public void Oracle_IsUniqueViolation_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));
        var dialect = ctx.GetDialect();

        Assert.True(dialect.IsUniqueViolation(new NumberedDbException(1, "ORA-00001: unique constraint violated")));
        Assert.False(dialect.IsUniqueViolation(new NumberedDbException(2291, "ORA-02291: integrity constraint violated - parent key not found")));
    }

    [Fact]
    public void SqlServer_ClassifyException_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer",
            new fakeDbFactory(SupportedDatabase.SqlServer));
        var dialect = ctx.GetDialect();

        Assert.Equal(DbErrorCategory.ConstraintViolation,
            dialect.ClassifyException(new NumberedDbException(2627, "Violation of PRIMARY KEY constraint")));
        Assert.Equal(DbErrorCategory.Deadlock,
            dialect.ClassifyException(new NumberedDbException(1205, "Transaction was deadlocked")));
        Assert.Equal(DbErrorCategory.Timeout,
            dialect.ClassifyException(new NumberedDbException(-2, "Execution timeout expired")));
    }

    [Fact]
    public void PostgreSql_ClassifyException_UsesSqlState()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql",
            new fakeDbFactory(SupportedDatabase.PostgreSql));
        var dialect = ctx.GetDialect();

        Assert.Equal(DbErrorCategory.ConstraintViolation,
            dialect.ClassifyException(new SqlStateDbException("23505", "duplicate key value violates unique constraint")));
        Assert.Equal(DbErrorCategory.SerializationFailure,
            dialect.ClassifyException(new SqlStateDbException("40001", "could not serialize access")));
        Assert.Equal(DbErrorCategory.Deadlock,
            dialect.ClassifyException(new SqlStateDbException("40P01", "deadlock detected")));
    }

    [Fact]
    public void MySql_ClassifyException_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=MySql",
            new fakeDbFactory(SupportedDatabase.MySql));
        var dialect = ctx.GetDialect();

        Assert.Equal(DbErrorCategory.ConstraintViolation,
            dialect.ClassifyException(new NumberedDbException(1062, "Duplicate entry for key")));
        Assert.Equal(DbErrorCategory.Deadlock,
            dialect.ClassifyException(new NumberedDbException(1213, "Deadlock found when trying to get lock")));
        Assert.Equal(DbErrorCategory.Timeout,
            dialect.ClassifyException(new NumberedDbException(1205, "Lock wait timeout exceeded")));
    }

    [Fact]
    public void Oracle_ClassifyException_UsesProviderErrorNumber()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle",
            new fakeDbFactory(SupportedDatabase.Oracle));
        var dialect = ctx.GetDialect();

        Assert.Equal(DbErrorCategory.ConstraintViolation,
            dialect.ClassifyException(new NumberedDbException(1, "ORA-00001: unique constraint violated")));
        Assert.Equal(DbErrorCategory.SerializationFailure,
            dialect.ClassifyException(new NumberedDbException(8177, "ORA-08177: can't serialize access for this transaction")));
        Assert.Equal(DbErrorCategory.Deadlock,
            dialect.ClassifyException(new NumberedDbException(60, "ORA-00060: deadlock detected while waiting for resource")));
    }

    [Fact]
    public void Sqlite_IsUniqueViolation_AcceptsExtendedConstraintCodes()
    {
        var ctx = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        var dialect = ctx.GetDialect();

        Assert.True(dialect.IsUniqueViolation(new SqliteExtendedDbException(1555, "UNIQUE constraint failed")));
        Assert.True(dialect.IsUniqueViolation(new SqliteExtendedDbException(2067, "PRIMARY KEY constraint failed")));
        Assert.Equal(DbErrorCategory.ConstraintViolation,
            dialect.ClassifyException(new SqliteExtendedDbException(2067, "PRIMARY KEY constraint failed")));
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
