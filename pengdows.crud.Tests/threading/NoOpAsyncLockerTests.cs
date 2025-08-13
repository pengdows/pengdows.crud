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
            var result = await NoOpAsyncLocker.Instance.TryLockAsync(TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
            Assert.True(result);
        }

        [Fact]
        public async Task TryLockAsync_CancelledToken_StillReturnsTrue()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await NoOpAsyncLocker.Instance.TryLockAsync(TimeSpan.Zero, cts.Token).ConfigureAwait(false);
            Assert.True(result);
        }

        [Fact]
        public async Task LockAsync_CancelledToken_CompletesSuccessfully()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await NoOpAsyncLocker.Instance.LockAsync(cts.Token).ConfigureAwait(false);
        }

        [Fact]
        public async Task DisposeAsync_IsNoOp()
        {
            await NoOpAsyncLocker.Instance.DisposeAsync().ConfigureAwait(false);
            await NoOpAsyncLocker.Instance.LockAsync().ConfigureAwait(false);
        }
    }
}
