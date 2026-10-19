using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// A provider's reader can throw while closing (Npgsql drains the rest of the result set on close, so a connection
/// that drops mid-drain throws from Close/Dispose). fakeDb reproduces it so callers' cleanup paths can be tested.
/// </summary>
public class FakeDbReaderCloseFailureTests
{
    private static fakeDbDataReader Reader() =>
        new(new List<Dictionary<string, object>> { new() { ["id"] = 1 } });

    [Fact]
    public void Close_WithFailOnClose_ThrowsOnce_AndLeavesTheReaderClosed()
    {
        var reader = Reader();
        var failure = new InvalidOperationException("drain failed");
        reader.FailOnClose = failure;

        var thrown = Assert.Throws<InvalidOperationException>(() => reader.Close());

        Assert.Same(failure, thrown);
        Assert.True(reader.IsClosed);
        reader.Close();
    }

    [Fact]
    public void Dispose_WithFailOnClose_Throws()
    {
        var reader = Reader();
        reader.FailOnClose = new InvalidOperationException("drain failed");

        Assert.Throws<InvalidOperationException>(() => reader.Dispose());
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public async Task DisposeAsync_WithFailOnClose_Throws()
    {
        var reader = Reader();
        reader.FailOnClose = new InvalidOperationException("drain failed");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.DisposeAsync());
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void Close_WithoutFailOnClose_DoesNotThrow()
    {
        var reader = Reader();

        reader.Close();

        Assert.True(reader.IsClosed);
    }
}
