using System;
using System.Threading.Tasks;
using pengdows.crud.connection;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.connection;

public class InternalConnectionStrategiesAsyncDisposeTests
{
    [Fact]
    public async Task KeepAlive_DisposeAsync_DisposesSentinel()
    {
        var sentinelDisposed = false;
        var sentinel = new TrackedConnection(new fakeDbConnection(), onDispose: _ => sentinelDisposed = true);
        var strategy = new KeepAliveConnectionStrategy(() => sentinel);

        await strategy.DisposeAsync();

        Assert.True(sentinelDisposed);
    }

    [Fact]
    public async Task KeepAlive_ReleaseConnectionAsync_DisposesNonSentinel()
    {
        var sentinel = new TrackedConnection(new fakeDbConnection());
        var strategy = new KeepAliveConnectionStrategy(() => sentinel);

        var releasedDisposed = false;
        var released = new TrackedConnection(new fakeDbConnection(), onDispose: _ => releasedDisposed = true);

        await strategy.ReleaseConnectionAsync(released);
        Assert.True(releasedDisposed);

        // Sentinel is not disposed by release
        var sentinelDisposed = false;
        var sentinel2 = new TrackedConnection(new fakeDbConnection(), onDispose: _ => sentinelDisposed = true);
        var strategy2 = new KeepAliveConnectionStrategy(() => sentinel2);
        await strategy2.ReleaseConnectionAsync(sentinel2);
        Assert.False(sentinelDisposed);
    }

    [Fact]
    public async Task SingleConnection_DisposeAsync_DisposesUnderlying()
    {
        var disposed = false;
        var tracked = new TrackedConnection(new fakeDbConnection(), onDispose: _ => disposed = true);
        var strategy = new SingleConnectionStrategy(tracked);

        await strategy.DisposeAsync();
        Assert.True(disposed);
    }

    [Fact]
    public async Task SingleWriter_DisposeAsync_DisposesWriter()
    {
        var writerDisposed = false;
        var writer = new TrackedConnection(new fakeDbConnection(), onDispose: _ => writerDisposed = true);
        var strategy = new SingleWriterConnectionStrategy(writer, () => new TrackedConnection(new fakeDbConnection()));

        await strategy.DisposeAsync();
        Assert.True(writerDisposed);
    }

    [Fact]
    public async Task SingleWriter_ReleaseConnectionAsync_DisposesReaderConnections()
    {
        var writer = new TrackedConnection(new fakeDbConnection());
        var strategy = new SingleWriterConnectionStrategy(writer, () => new TrackedConnection(new fakeDbConnection()));

        var readDisposed = false;
        var read = new TrackedConnection(new fakeDbConnection(), onDispose: _ => readDisposed = true);

        await strategy.ReleaseConnectionAsync(read);
        Assert.True(readDisposed);

        // Writer is not disposed by release
        var writerDisposed = false;
        var writer2 = new TrackedConnection(new fakeDbConnection(), onDispose: _ => writerDisposed = true);
        var strategy2 = new SingleWriterConnectionStrategy(writer2, () => new TrackedConnection(new fakeDbConnection()));
        await strategy2.ReleaseConnectionAsync(writer2);
        Assert.False(writerDisposed);
    }
}

