using System;
using pengdows.crud.exceptions;
using Xunit;

namespace pengdows.crud.Tests.exceptions;

public class CompleteExceptionTests
{
    [Fact]
    public void InvalidValueException_WithMessage_CreatesExceptionWithMessage()
    {
        const string message = "Invalid value provided";

        var exception = new InvalidValueException(message);

        Assert.Equal(message, exception.Message);
        Assert.IsType<InvalidValueException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void InvalidValueException_CanBeThrown()
    {
        const string message = "Test invalid value";

        var exception =
            Assert.Throws<InvalidValueException>(new Action(() => throw new InvalidValueException(message)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void NoColumnsFoundException_WithMessage_CreatesExceptionWithMessage()
    {
        const string message = "No columns found in entity";

        var exception = new NoColumnsFoundException(message);

        Assert.Equal(message, exception.Message);
        Assert.IsType<NoColumnsFoundException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void NoColumnsFoundException_CanBeThrown()
    {
        const string message = "Test no columns";

        var exception =
            Assert.Throws<NoColumnsFoundException>(new Action(() => throw new NoColumnsFoundException(message)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void TooManyColumns_WithMessage_CreatesExceptionWithMessage()
    {
        const string message = "Too many columns in result set";

        var exception = new TooManyColumns(message);

        Assert.Equal(message, exception.Message);
        Assert.IsType<TooManyColumns>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void TooManyColumns_CanBeThrown()
    {
        const string message = "Test too many columns";

        var exception = Assert.Throws<TooManyColumns>(new Action(() => throw new TooManyColumns(message)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void ConnectionFailedException_WithMessage_CreatesExceptionWithMessage()
    {
        const string message = "Connection to database failed";

        var exception = new ConnectionFailedException(message);

        Assert.Equal(message, exception.Message);
        Assert.IsType<ConnectionFailedException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ConnectionFailedException_CanBeThrown()
    {
        const string message = "Test connection failed";

        var exception =
            Assert.Throws<ConnectionFailedException>(new Action(() => throw new ConnectionFailedException(message)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void TransactionModeNotSupportedException_WithMessage_CreatesExceptionWithMessage()
    {
        const string message = "Transaction mode not supported";

        var exception = new TransactionModeNotSupportedException(message);

        Assert.Equal(message, exception.Message);
        Assert.IsType<TransactionModeNotSupportedException>(exception);
        Assert.IsAssignableFrom<NotSupportedException>(exception);
    }

    [Fact]
    public void TransactionModeNotSupportedException_CanBeThrown()
    {
        const string message = "Test transaction mode not supported";

        var exception = Assert.Throws<TransactionModeNotSupportedException>(
            new Action(() => throw new TransactionModeNotSupportedException(message)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void AllExceptions_InheritFromException()
    {
        var invalidValueException = new InvalidValueException("test");
        var noColumnsException = new NoColumnsFoundException("test");
        var tooManyColumnsException = new TooManyColumns("test");
        var connectionFailedException = new ConnectionFailedException("test");
        var transactionModeNotSupportedException = new TransactionModeNotSupportedException("test");

        Assert.IsAssignableFrom<Exception>(invalidValueException);
        Assert.IsAssignableFrom<Exception>(noColumnsException);
        Assert.IsAssignableFrom<Exception>(tooManyColumnsException);
        Assert.IsAssignableFrom<Exception>(connectionFailedException);
        Assert.IsAssignableFrom<Exception>(transactionModeNotSupportedException);
    }

    [Fact]
    public void AllExceptions_WithEmptyMessage_HandleGracefully()
    {
        var invalidValueException = new InvalidValueException("");
        var noColumnsException = new NoColumnsFoundException("");
        var tooManyColumnsException = new TooManyColumns("");
        var connectionFailedException = new ConnectionFailedException("");
        var transactionModeNotSupportedException = new TransactionModeNotSupportedException("");

        Assert.Equal("", invalidValueException.Message);
        Assert.Equal("", noColumnsException.Message);
        Assert.Equal("", tooManyColumnsException.Message);
        Assert.Equal("", connectionFailedException.Message);
        Assert.Equal("", transactionModeNotSupportedException.Message);
    }
}