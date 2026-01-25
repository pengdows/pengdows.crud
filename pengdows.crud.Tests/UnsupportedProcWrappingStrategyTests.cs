#region

using System;
using pengdows.crud.enums;
using pengdows.crud.strategies.proc;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class UnsupportedProcWrappingStrategyTests
{
    [Fact]
    public void Wrap_ThrowsNotSupportedException()
    {
        // Arrange
        var strategy = new UnsupportedProcWrappingStrategy();
        var procName = "TestProcedure";
        var args = "arg1, arg2";

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            strategy.Wrap(procName, ExecutionType.Read, args));

        Assert.Equal("Stored procedures are not supported by this database.", exception.Message);
    }

    [Fact]
    public void Wrap_WithWriteExecutionType_ThrowsNotSupportedException()
    {
        // Arrange
        var strategy = new UnsupportedProcWrappingStrategy();
        var procName = "TestProcedure";
        var args = "arg1, arg2";

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            strategy.Wrap(procName, ExecutionType.Write, args));

        Assert.Equal("Stored procedures are not supported by this database.", exception.Message);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("SimpleProcName", "")]
    [InlineData("ComplexProcName", "param1 int, param2 varchar(50)")]
    [InlineData("schema.ProcName", "multiple, params, here")]
    public void Wrap_WithVariousInputs_AlwaysThrowsNotSupportedException(string procName, string args)
    {
        // Arrange
        var strategy = new UnsupportedProcWrappingStrategy();

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            strategy.Wrap(procName, ExecutionType.Read, args));

        Assert.Equal("Stored procedures are not supported by this database.", exception.Message);
    }

    [Fact]
    public void Wrap_WithNullProcName_ThrowsNotSupportedException()
    {
        // Arrange
        var strategy = new UnsupportedProcWrappingStrategy();

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            strategy.Wrap(null!, ExecutionType.Read, "args"));

        Assert.Equal("Stored procedures are not supported by this database.", exception.Message);
    }

    [Fact]
    public void Wrap_WithNullArgs_ThrowsNotSupportedException()
    {
        // Arrange
        var strategy = new UnsupportedProcWrappingStrategy();

        // Act & Assert
        var exception = Assert.Throws<NotSupportedException>(() =>
            strategy.Wrap("ProcName", ExecutionType.Read, null!));

        Assert.Equal("Stored procedures are not supported by this database.", exception.Message);
    }
}