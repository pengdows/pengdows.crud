using System;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.threading;
using Xunit;

namespace pengdows.crud.Tests.threading
{
    public class NoOpAsyncLockerTests
    {
        [Fact]
        public async Task TryLockAsync_AlwaysReturnsTrue()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await NoOpAsyncLocker.Instance.TryLockAsync(TimeSpan.FromMilliseconds(1), cts.Token);
            Assert.True(result);
        }

        [Fact]
        public async Task TryLockAsync_CancelledToken_StillReturnsTrue()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await NoOpAsyncLocker.Instance.TryLockAsync(TimeSpan.Zero, cts.Token);
            Assert.True(result);
        }

        [Fact]
        public async Task LockAsync_CancelledToken_CompletesSuccessfully()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await NoOpAsyncLocker.Instance.LockAsync(cts.Token);
        }

        [Fact]
        public async Task DisposeAsync_IsNoOp()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await NoOpAsyncLocker.Instance.DisposeAsync();
            await NoOpAsyncLocker.Instance.LockAsync(cts.Token);
        }

        [Fact]
        public async Task DisposeAsync_DoesNotMarkDisposed()
        {
            await NoOpAsyncLocker.Instance.DisposeAsync();
            Assert.False(NoOpAsyncLocker.Instance.IsDisposed);
        }
    }
}
