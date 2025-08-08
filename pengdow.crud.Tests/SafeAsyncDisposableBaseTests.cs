#region
using System;
using System.Threading.Tasks;
using pengdow.crud.infrastructure;
using Xunit;
#endregion

namespace pengdow.crud.Tests;

public class SafeAsyncDisposableBaseTests
{
    private class TestDisposable : SafeAsyncDisposableBase
    {
        public int ManagedCount { get; private set; }
        public int ManagedAsyncCount { get; private set; }
        public int UnmanagedCount { get; private set; }

        protected override void DisposeManaged()
        {
            ManagedCount++;
        }

        protected override ValueTask DisposeManagedAsync()
        {
            ManagedAsyncCount++;
            return ValueTask.CompletedTask;
        }

        protected override void DisposeUnmanaged()
        {
            UnmanagedCount++;
        }
    }

    private sealed class SyncThrowsDisposable : SafeAsyncDisposableBase
    {
        protected override void DisposeManaged()
        {
            throw new InvalidOperationException("sync");
        }

        protected override void DisposeUnmanaged()
        {
            throw new InvalidOperationException("sync");
        }
    }

    private sealed class AsyncThrowsDisposable : SafeAsyncDisposableBase
    {
        protected override async ValueTask DisposeManagedAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("async");
        }
    }

    private sealed class ThrowIfDisposedDisposable : SafeAsyncDisposableBase
    {
        public void Use()
        {
            ThrowIfDisposed();
        }
    }

    private sealed class SyncOnlyDisposable : SafeAsyncDisposableBase
    {
        public int ManagedCount { get; private set; }

        protected override void DisposeManaged()
        {
            ManagedCount++;
        }
    }

    [Fact]
    public void Dispose_OnlyOnce()
    {
        var d = new TestDisposable();

        d.Dispose();
        d.Dispose();

        Assert.True(d.IsDisposed);
        Assert.Equal(1, d.ManagedCount);
        Assert.Equal(0, d.ManagedAsyncCount);
        Assert.Equal(1, d.UnmanagedCount);
    }

    [Fact]
    public async Task DisposeAsync_OnlyOnce()
    {
        var d = new TestDisposable();

        await d.DisposeAsync();
        await d.DisposeAsync();

        Assert.True(d.IsDisposed);
        Assert.Equal(0, d.ManagedCount);
        Assert.Equal(1, d.ManagedAsyncCount);
        Assert.Equal(1, d.UnmanagedCount);
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_NoAdditionalCalls()
    {
        var d = new TestDisposable();

        d.Dispose();
        await d.DisposeAsync();

        Assert.True(d.IsDisposed);
        Assert.Equal(1, d.ManagedCount);
        Assert.Equal(0, d.ManagedAsyncCount);
        Assert.Equal(1, d.UnmanagedCount);
    }

    [Fact]
    public async Task Dispose_AfterDisposeAsync_NoAdditionalCalls()
    {
        var d = new TestDisposable();

        await d.DisposeAsync();
        d.Dispose();

        Assert.True(d.IsDisposed);
        Assert.Equal(0, d.ManagedCount);
        Assert.Equal(1, d.ManagedAsyncCount);
        Assert.Equal(1, d.UnmanagedCount);
    }

    [Fact]
    public void Dispose_SwallowsExceptions()
    {
        var d = new SyncThrowsDisposable();

        d.Dispose();

        Assert.True(d.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_PropagatesExceptions()
    {
        var d = new AsyncThrowsDisposable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await d.DisposeAsync());
        Assert.True(d.IsDisposed);
    }

    [Fact]
    public void ThrowIfDisposed_ThrowsAfterDispose()
    {
        var d = new ThrowIfDisposedDisposable();

        d.Use();
        d.Dispose();

        Assert.Throws<ObjectDisposedException>(() => d.Use());
    }

    [Fact]
    public async Task DisposeAsync_BridgesToSync()
    {
        var d = new SyncOnlyDisposable();

        await d.DisposeAsync();

        Assert.Equal(1, d.ManagedCount);
    }
}

