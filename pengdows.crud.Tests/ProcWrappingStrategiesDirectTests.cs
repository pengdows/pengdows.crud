#region

using System;
using pengdows.crud.enums;
using pengdows.crud.strategies.proc;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ProcWrappingStrategiesDirectTests
{
    private static string Wrapper(string name) => $"[{name}]";

    [Fact]
    public void ExecProcWrappingStrategy_UsesWrapper_WithAndWithoutArgs()
    {
        var s = new ExecProcWrappingStrategy();

        var withArgs = s.Wrap("proc", ExecutionType.Write, "@a, @b", Wrapper);
        Assert.Equal("EXEC [proc] @a, @b", withArgs);

        var noArgs = s.Wrap("proc", ExecutionType.Read, string.Empty, Wrapper);
        Assert.Equal("EXEC [proc]", noArgs);
    }

    [Fact]
    public void ExecProcWrappingStrategy_EmptyName_Throws()
    {
        var s = new ExecProcWrappingStrategy();
        Assert.Throws<ArgumentException>(() => s.Wrap("  ", ExecutionType.Write, "@a"));
    }

    [Fact]
    public void CallProcWrappingStrategy_UsesWrapper_WithAndWithoutArgs()
    {
        var s = new CallProcWrappingStrategy();

        var withArgs = s.Wrap("proc", ExecutionType.Write, "@a, @b", Wrapper);
        Assert.Equal("CALL [proc](@a, @b)", withArgs);

        var noArgs = s.Wrap("proc", ExecutionType.Read, string.Empty, Wrapper);
        Assert.Equal("CALL [proc]()", noArgs);
    }

    [Fact]
    public void CallProcWrappingStrategy_EmptyName_Throws()
    {
        var s = new CallProcWrappingStrategy();
        Assert.Throws<ArgumentException>(() => s.Wrap("", ExecutionType.Read, "@a"));
    }

    [Fact]
    public void OracleProcWrappingStrategy_UsesWrapper_WithAndWithoutArgs()
    {
        var s = new OracleProcWrappingStrategy();

        var withArgs = s.Wrap("proc", ExecutionType.Write, "@a, @b", Wrapper);
        Assert.Equal("BEGIN\n\t[proc](@a, @b);\nEND;", withArgs);

        var noArgs = s.Wrap("proc", ExecutionType.Read, string.Empty, Wrapper);
        Assert.Equal("BEGIN\n\t[proc];\nEND;", noArgs);
    }

    [Fact]
    public void OracleProcWrappingStrategy_EmptyName_Throws()
    {
        var s = new OracleProcWrappingStrategy();
        Assert.Throws<ArgumentException>(() => s.Wrap(null!, ExecutionType.Write, "@a"));
    }
}

