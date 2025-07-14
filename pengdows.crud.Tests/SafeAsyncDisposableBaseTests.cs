#region

using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SafeAsyncDisposableBaseTests
{
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
}