using System;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests.exceptions.translators;

public class DatabaseExceptionContractTests
{
    [Fact]
    public void UniqueConstraintViolationException_PreservesDiagnosticFields()
    {
        var inner = new InvalidOperationException("raw");

        var exception = new UniqueConstraintViolationException(
            "duplicate key",
            SupportedDatabase.SqlServer,
            inner,
            sqlState: "23505",
            errorCode: 2627,
            constraintName: "PK_jobs",
            isTransient: false);

        Assert.Equal(SupportedDatabase.SqlServer, exception.Database);
        Assert.Equal("23505", exception.SqlState);
        Assert.Equal(2627, exception.ErrorCode);
        Assert.Equal("PK_jobs", exception.ConstraintName);
        Assert.False(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void DeadlockException_IsTransientWriteConflictException()
    {
        var exception = new DeadlockException("deadlock", SupportedDatabase.PostgreSql);

        Assert.IsType<DeadlockException>(exception);
        Assert.IsAssignableFrom<TransientWriteConflictException>(exception);
        Assert.IsAssignableFrom<DatabaseOperationException>(exception);
        Assert.True(exception.IsTransient);
    }

    [Fact]
    public void ConcurrencyConflictException_IsAssignableFrom_DatabaseException()
    {
        var ex = new ConcurrencyConflictException("version mismatch", SupportedDatabase.SqlServer);
        Assert.IsAssignableFrom<DatabaseException>(ex);
        Assert.False(ex.IsTransient ?? false);
    }

    [Fact]
    public void ConcurrencyConflictException_DoesNotRequireConstraintName()
    {
        var exception = new ConcurrencyConflictException(
            "version mismatch",
            SupportedDatabase.SqlServer);

        Assert.Null(exception.ConstraintName);
        Assert.Equal(SupportedDatabase.SqlServer, exception.Database);
    }

    [Fact]
    public void ConnectionException_PreservesDiagnosticFields()
    {
        var inner = new InvalidOperationException("raw");

        var exception = new ConnectionException(
            "connection refused",
            SupportedDatabase.PostgreSql,
            inner,
            sqlState: "08006",
            errorCode: 111,
            isTransient: true);

        Assert.Equal(SupportedDatabase.PostgreSql, exception.Database);
        Assert.Equal("08006", exception.SqlState);
        Assert.Equal(111, exception.ErrorCode);
        Assert.True(exception.IsTransient);
        Assert.Same(inner, exception.InnerException);
        Assert.IsAssignableFrom<DatabaseOperationException>(exception);
    }

    [Fact]
    public void TransactionException_PreservesDiagnosticFields()
    {
        var inner = new InvalidOperationException("raw");

        var exception = new TransactionException(
            "transaction aborted",
            SupportedDatabase.SqlServer,
            inner,
            sqlState: "40000",
            errorCode: 3960);

        Assert.Equal(SupportedDatabase.SqlServer, exception.Database);
        Assert.Equal("40000", exception.SqlState);
        Assert.Equal(3960, exception.ErrorCode);
        Assert.Same(inner, exception.InnerException);
        Assert.IsAssignableFrom<DatabaseOperationException>(exception);
    }

    [Fact]
    public void DataMappingException_PreservesDiagnosticFields()
    {
        var inner = new InvalidCastException("cannot cast");

        var exception = new DataMappingException(
            "type mismatch",
            SupportedDatabase.MySql,
            inner);

        Assert.Equal(SupportedDatabase.MySql, exception.Database);
        Assert.Same(inner, exception.InnerException);
        Assert.IsAssignableFrom<DatabaseException>(exception);
    }

    [Fact]
    public void SqlGenerationException_PreservesDiagnosticFields()
    {
        var exception = new SqlGenerationException(
            "invalid upsert target",
            SupportedDatabase.Oracle);

        Assert.Equal(SupportedDatabase.Oracle, exception.Database);
        Assert.Null(exception.InnerException);
        Assert.IsAssignableFrom<DatabaseException>(exception);
    }
}
