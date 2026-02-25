// =============================================================================
// FILE: PoolSlot.cs
// PURPOSE: RAII struct representing an acquired pool slot.
//
// AI SUMMARY:
// - Readonly struct implementing IDisposable and IAsyncDisposable.
// - Returned by PoolGovernor.Acquire/AcquireAsync.
// - Dispose(): Releases permit back to governor (once only).
// - PoolSlotToken: Inner class ensuring single release via Interlocked.
// - Usage: using var slot = await governor.AcquireAsync();
// - Struct design: efficient, stack-allocated, no heap pressure.
// - Null token (default struct) is valid no-op for disabled governors.
// =============================================================================

using System.Diagnostics;

namespace pengdows.crud.infrastructure;

internal readonly struct PoolSlot : IDisposable, IAsyncDisposable
{
    private readonly PoolSlotToken? _token;

    internal PoolSlot(PoolSlotToken token)
    {
        _token = token;
    }

    public void Dispose()
    {
        _token?.Release();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal sealed class PoolSlotToken
    {
        private readonly PoolGovernor _governor;
        private readonly long _waitStart;
        private readonly long _acquiredAt;
        private int _released;

        internal PoolSlotToken(PoolGovernor governor, long waitStart)
        {
            _governor = governor;
            _waitStart = waitStart;
            _acquiredAt = Stopwatch.GetTimestamp();
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                var releasedAt = Stopwatch.GetTimestamp();
                _governor.ReleaseToken(_waitStart, _acquiredAt, releasedAt);
            }
        }
    }
}
