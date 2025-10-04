using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

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

    // Deterministic async throw; avoids Task.Yield() runner quirks
    private sealed class AsyncThrowsDisposable : SafeAsyncDisposableBase
    {
        protected override ValueTask DisposeManagedAsync()
            => ValueTask.FromException(new InvalidOperationException("async"));
        // For older TFMs:
        // => new ValueTask(Task.FromException(new InvalidOperationException("async")));
    }

    private sealed class BothAsyncThrowDisposable : SafeAsyncDisposableBase
    {
        public List<(Exception ex, string phase)> Logged { get; } = new();

        protected override ValueTask DisposeManagedAsync()
            => ValueTask.FromException(new InvalidOperationException("managed"));

        protected override ValueTask DisposeUnmanagedAsync()
            => ValueTask.FromException(new InvalidOperationException("unmanaged"));

        protected override void OnDisposeException(Exception ex, string phase)
            => Logged.Add((ex, phase));
    }

    private sealed class ExplicitSwallowDisposable : SafeAsyncDisposableBase
    {
        protected override ValueTask DisposeManagedAsync()
        {
            try
            {
                throw new InvalidOperationException("expected");
            }
            catch
            {
                // eat error quietly
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowIfDisposedDisposable : SafeAsyncDisposableBase
    {
        public void Use() => ThrowIfDisposed();
    }

    private sealed class SyncOnlyDisposable : SafeAsyncDisposableBase
    {
        public int ManagedCount { get; private set; }
        protected override void DisposeManaged() => ManagedCount++;
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => d.DisposeAsync().AsTask());
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
        Assert.True(d.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_FirstFailureWins_SecondIsLogged()
    {
        var d = new BothAsyncThrowDisposable();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => d.DisposeAsync().AsTask());
        Assert.Equal("managed", ex.Message);                     // first failure wins
        Assert.Single(d.Logged);                                 // second is logged, not thrown
        Assert.Equal("DisposeUnmanagedAsync", d.Logged[0].phase);
        Assert.Equal("unmanaged", d.Logged[0].ex.Message);
        Assert.True(d.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_OnlyRunsOnce()
    {
        var d = new TestDisposable();

        await Task.WhenAll(
            d.DisposeAsync().AsTask(),
            d.DisposeAsync().AsTask(),
            d.DisposeAsync().AsTask());

        Assert.True(d.IsDisposed);
        Assert.Equal(0, d.ManagedCount);
        Assert.Equal(1, d.ManagedAsyncCount);
        Assert.Equal(1, d.UnmanagedCount);
    }

    [Fact]
    public async Task DisposeAsync_ExplicitSwallow_DoesNotThrow()
    {
        var d = new ExplicitSwallowDisposable();

        await d.DisposeAsync(); // intentionally swallowed in override

        Assert.True(d.IsDisposed);
    }
}
