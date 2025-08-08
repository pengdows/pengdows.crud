using Moq;
using pengdows.crud.enums;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class ConnectionStrategyTests
{
    [Fact]
    public void StandardStrategy_ReturnsNewConnectionAndDisposes()
    {
        var disposeCount = 0;
        ITrackedConnection Factory()
        {
            var mock = new Mock<ITrackedConnection>();
            mock.Setup(c => c.Dispose()).Callback(() => disposeCount++);
            return mock.Object;
        }

        var strategy = new StandardConnectionStrategy(Factory);
        var c1 = strategy.GetConnection(ExecutionType.Read);
        var c2 = strategy.GetConnection(ExecutionType.Write);
        Assert.NotSame(c1, c2);
        strategy.CloseAndDisposeConnection(c1);
        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public void StandardStrategy_CloseNull_DoesNothing()
    {
        var strategy = new StandardConnectionStrategy(() => new Mock<ITrackedConnection>().Object);
        strategy.CloseAndDisposeConnection(null);
    }

    [Fact]
    public void SingleConnectionStrategy_ReturnsSameConnection()
    {
        var mock = new Mock<ITrackedConnection>();
        var strategy = new SingleConnectionStrategy(mock.Object);
        var c1 = strategy.GetConnection(ExecutionType.Read);
        var c2 = strategy.GetConnection(ExecutionType.Write);
        Assert.Same(c1, c2);
    }

    [Fact]
    public void SingleConnectionStrategy_CloseDoesNotDispose()
    {
        var mock = new Mock<ITrackedConnection>();
        var strategy = new SingleConnectionStrategy(mock.Object);
        strategy.CloseAndDisposeConnection(mock.Object);
        mock.Verify(c => c.Dispose(), Times.Never);
    }

    [Fact]
    public void SingleWriterStrategy_ReadGetsNewConnection()
    {
        var writer = new Mock<ITrackedConnection>();
        var reader = new Mock<ITrackedConnection>();
        var strategy = new SingleWriterConnectionStrategy(writer.Object, () => reader.Object);
        var conn = strategy.GetConnection(ExecutionType.Read);
        Assert.Same(reader.Object, conn);
    }

    [Fact]
    public void SingleWriterStrategy_WriteGetsWriterConnection()
    {
        var writer = new Mock<ITrackedConnection>();
        var strategy = new SingleWriterConnectionStrategy(writer.Object, () => new Mock<ITrackedConnection>().Object);
        var conn = strategy.GetConnection(ExecutionType.Write);
        Assert.Same(writer.Object, conn);
    }

    [Fact]
    public void SingleWriterStrategy_CloseWriterDoesNotDispose()
    {
        var writer = new Mock<ITrackedConnection>();
        var strategy = new SingleWriterConnectionStrategy(writer.Object, () => new Mock<ITrackedConnection>().Object);
        strategy.CloseAndDisposeConnection(writer.Object);
        writer.Verify(c => c.Dispose(), Times.Never);
    }

    [Fact]
    public void SingleWriterStrategy_CloseReadDisposes()
    {
        var writer = new Mock<ITrackedConnection>();
        var reader = new Mock<ITrackedConnection>();
        var strategy = new SingleWriterConnectionStrategy(writer.Object, () => reader.Object);
        strategy.CloseAndDisposeConnection(reader.Object);
        reader.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void KeepAliveStrategy_DisposesConnection()
    {
        var mock = new Mock<ITrackedConnection>();
        var strategy = new KeepAliveConnectionStrategy(() => mock.Object);
        var conn = strategy.GetConnection(ExecutionType.Read);
        strategy.CloseAndDisposeConnection(conn);
        mock.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void KeepAliveStrategy_CloseNull_DoesNothing()
    {
        var strategy = new KeepAliveConnectionStrategy(() => new Mock<ITrackedConnection>().Object);
        strategy.CloseAndDisposeConnection(null);
    }
}

