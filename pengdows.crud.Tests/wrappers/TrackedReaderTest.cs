#region

using System;
using System.Collections.Generic;
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

    [Fact]
    public void Read_DoesNotClose_WhenShouldCloseConnectionFalse()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.Read()).Returns(false);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, false);

        var result = tracked.Read();

        Assert.False(result);
        connection.Verify(c => c.Close(), Times.Never);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task ReadAsync_DisposesAfterLastRow()
    {
        var reader = new Mock<FakeDbDataReader>();
        reader.SetupSequence(r => r.ReadAsync(CancellationToken.None))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        reader.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        var first = await tracked.ReadAsync();
        Assert.True(first);
        connection.Verify(c => c.Close(), Times.Never);
        locker.Verify(l => l.DisposeAsync(), Times.Never);

        var second = await tracked.ReadAsync();
        Assert.False(second);
        connection.Verify(c => c.Close(), Times.Once);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void NextResult_ReturnsFalseWithoutCallingReader()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.NextResult()).Throws<InvalidOperationException>();

        var tracked = new TrackedReader(reader.Object, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var result = tracked.NextResult();

        Assert.False(result);
        reader.Verify(r => r.NextResult(), Times.Never);    }

    [Fact]
    public async Task DisposeAsync_ClosesConnection_WhenShouldCloseTrue()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, true);

        await tracked.DisposeAsync();

        connection.Verify(c => c.Close(), Times.Once);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotCloseConnection_WhenShouldCloseFalse()
    {
        var reader = new Mock<DbDataReader>();
        reader.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, false);

        await tracked.DisposeAsync();

        connection.Verify(c => c.Close(), Times.Never);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void Dispose_DoesNotCloseConnection_WhenShouldCloseFalse()
    {
        var reader = new Mock<DbDataReader>();
        var connection = new Mock<ITrackedConnection>();
        var locker = new Mock<IAsyncDisposable>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var tracked = new TrackedReader(reader.Object, connection.Object, locker.Object, false);

        tracked.Dispose();

        connection.Verify(c => c.Close(), Times.Never);
        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void WrapperMethods_DelegateToUnderlyingReader()
    {
        var row = new Dictionary<string, object>
        {
            ["Bool"] = true,
            ["Byte"] = (byte)1,
            ["String"] = "text",
            ["Decimal"] = 1.2m,
            ["Double"] = 2.3,
            ["Float"] = 3.4f,
            ["Short"] = (short)5,
            ["Int"] = 6,
            ["Long"] = 7L,
            ["Guid"] = Guid.NewGuid(),
            ["Char"] = 'x',
            ["Date"] = new DateTime(2025, 1, 1)
        };

        using var reader = new FakeDbDataReader(new[] { row });
        reader.Read();

        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        Assert.True(tracked.GetBoolean(0));
        Assert.Equal((byte)1, tracked.GetByte(1));
        Assert.Equal("text", tracked.GetString(2));
        Assert.Equal(1.2m, tracked.GetDecimal(3));
        Assert.Equal(2.3, tracked.GetDouble(4));
        Assert.Equal(3.4f, tracked.GetFloat(5));
        Assert.Equal((short)5, tracked.GetInt16(6));
        Assert.Equal(6, tracked.GetInt32(7));
        Assert.Equal(7L, tracked.GetInt64(8));
        Assert.Equal(row["Guid"], tracked.GetGuid(9));
        Assert.Equal('x', tracked.GetChar(10));
        Assert.Equal(new DateTime(2025, 1, 1), tracked.GetDateTime(11));
        Assert.Equal("Bool", tracked.GetName(0));
        Assert.Equal(2, tracked.GetOrdinal("String"));
        Assert.False(tracked.IsDBNull(0));
        Assert.Equal(row["String"], tracked["String"]);
        Assert.Equal(row["Int"], tracked[7]);
        Assert.Null(tracked.GetSchemaTable());
        Assert.Equal(0, tracked.Depth);
        Assert.False(tracked.IsClosed);
        Assert.Equal(0, tracked.RecordsAffected);
    }
}
