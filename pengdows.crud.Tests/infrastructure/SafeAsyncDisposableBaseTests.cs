using System;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests.infrastructure;

public class SafeAsyncDisposableBaseTests
{
    [Fact]
    public void IsDisposed_WhenNew_ReturnsFalse()
    {
        var disposable = new TestAsyncDisposable();

        Assert.False(disposable.IsDisposed);
    }

    [Fact]
    public void Dispose_WhenCalled_SetsIsDisposedTrue()
    {
        var disposable = new TestAsyncDisposable();

        disposable.Dispose();

        Assert.True(disposable.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_WhenCalled_SetsIsDisposedTrue()
    {
        var disposable = new TestAsyncDisposable();

        await disposable.DisposeAsync();

        Assert.True(disposable.IsDisposed);
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_OnlyCallsDisposeManagedOnce()
    {
        var disposable = new TestAsyncDisposable();

        disposable.Dispose();
        disposable.Dispose();
        disposable.Dispose();

        Assert.Equal(1, disposable.DisposeManagedCallCount);
    }

    [Fact]
    public async Task DisposeAsync_WhenCalledMultipleTimes_OnlyCallsDisposeManagedAsyncOnce()
    {
        var disposable = new TestAsyncDisposable();

        await disposable.DisposeAsync();
        await disposable.DisposeAsync();
        await disposable.DisposeAsync();

        Assert.Equal(1, disposable.DisposeManagedAsyncCallCount);
    }

    [Fact]
    public void ThrowIfDisposed_WhenNotDisposed_DoesNotThrow()
    {
        var disposable = new TestAsyncDisposable();

        disposable.CallThrowIfDisposed();
    }

    [Fact]
    public void ThrowIfDisposed_WhenDisposed_ThrowsObjectDisposedException()
    {
        var disposable = new TestAsyncDisposable();
        disposable.Dispose();

        var exception = Assert.Throws<ObjectDisposedException>(() => disposable.CallThrowIfDisposed());
        Assert.Contains(nameof(TestAsyncDisposable), exception.ObjectName);
    }

    [Fact]
    public async Task ThrowIfDisposed_WhenDisposedAsync_ThrowsObjectDisposedException()
    {
        var disposable = new TestAsyncDisposable();
        await disposable.DisposeAsync();

        var exception = Assert.Throws<ObjectDisposedException>(() => disposable.CallThrowIfDisposed());
        Assert.Contains(nameof(TestAsyncDisposable), exception.ObjectName);
    }

    [Fact]
    public void Dispose_CallsDisposeUnmanaged()
    {
        var disposable = new TestAsyncDisposable();

        disposable.Dispose();

        Assert.Equal(1, disposable.DisposeUnmanagedCallCount);
    }

    [Fact]
    public async Task DisposeAsync_CallsDisposeUnmanaged()
    {
        var disposable = new TestAsyncDisposable();

        await disposable.DisposeAsync();

        Assert.Equal(1, disposable.DisposeUnmanagedCallCount);
    }

    [Fact]
    public void Dispose_WithExceptionInDisposeManaged_DoesNotThrow()
    {
        var disposable = new TestAsyncDisposable { ThrowInDisposeManaged = true };

        disposable.Dispose();

        Assert.True(disposable.IsDisposed);
    }


    [Fact]
    public void MixedDispose_CalledOnSameInstance_WorksCorrectly()
    {
        var disposable = new TestAsyncDisposable();

        disposable.Dispose();

        Assert.True(disposable.IsDisposed);
        Assert.Equal(1, disposable.DisposeManagedCallCount);
        Assert.Equal(0, disposable.DisposeManagedAsyncCallCount);
        Assert.Equal(1, disposable.DisposeUnmanagedCallCount);
    }

    private class TestAsyncDisposable : SafeAsyncDisposableBase
    {
        public int DisposeManagedCallCount { get; private set; }
        public int DisposeManagedAsyncCallCount { get; private set; }
        public int DisposeUnmanagedCallCount { get; private set; }
        public bool ThrowInDisposeManaged { get; set; }
        public bool ThrowInDisposeManagedAsync { get; set; }

        protected override void DisposeManaged()
        {
            DisposeManagedCallCount++;
            if (ThrowInDisposeManaged)
                throw new InvalidOperationException("Test exception in DisposeManaged");
        }

        protected override ValueTask DisposeManagedAsync()
        {
            DisposeManagedAsyncCallCount++;
            if (ThrowInDisposeManagedAsync)
                throw new InvalidOperationException("Test exception in DisposeManagedAsync");
            return ValueTask.CompletedTask;
        }

        protected override void DisposeUnmanaged()
        {
            DisposeUnmanagedCallCount++;
        }

        public void CallThrowIfDisposed()
        {
            ThrowIfDisposed();
        }
    }
}
