#region

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests.wrappers;

public class TrackedReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsFalseAndDisposes_WhenDone()
    {
        var reader = new Mock<FakeDbDataReader>();
        reader.SetupSequence(r => r.ReadAsync(CancellationToken.None))
            .ReturnsAsync(false);

        reader.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        var result = await tracked.ReadAsync();

        Assert.False(result);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
        connection.Verify(c => c.Close(), Times.Once);
    }

    [Fact]
    public void Read_ReturnsFalseAndDisposes_WhenDone()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.Read()).Returns(false);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        var result = tracked.Read();

        Assert.False(result);
        connection.Verify(c => c.Close(), Times.Once);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_OnlyOnce()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        await tracked.DisposeAsync();
        await tracked.DisposeAsync();

        locker.Verify(l => l.DisposeAsync(), Times.Once);
        connection.Verify(c => c.Close(), Times.Once);
    }

    [Fact]
    public void Dispose_OnlyOnce()
    {
        var reader = new Mock<DbDataReader>();
        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        tracked.Dispose();
        tracked.Dispose();

        locker.Verify(l => l.DisposeAsync(), Times.Once);
        connection.Verify(c => c.Close(), Times.Once);
    }

    [Fact]
    public void Accessors_ForwardToReader()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.FieldCount).Returns(1);
        reader.Setup(r => r[0]).Returns("value");
        reader.Setup(r => r["col"]).Returns("value2");

        var tracked = new TrackedReader(reader.Object, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(),
            false);

        Assert.Equal(1, tracked.FieldCount);
        Assert.Equal("value", tracked[0]);
        Assert.Equal("value2", tracked["col"]);
    }
}