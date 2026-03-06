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
}
