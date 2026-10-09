// =============================================================================
// FILE: PoolSlotAdaptiveCopy.cs
// PURPOSE: RAII struct representing an acquired pool slot.
//
// AI SUMMARY:
// - Readonly struct implementing IDisposable and IAsyncDisposable.
// - Returned by PoolGovernorAdaptiveCopy.Acquire/AcquireAsync.
// - Dispose(): Releases permit back to governor (once only).
// - PoolSlotAdaptiveCopyToken: Inner class ensuring single release via Interlocked.
// - Usage: using var slot = await governor.AcquireAsync();
// - Struct wrapper; each real acquisition allocates one PoolSlotAdaptiveCopyToken.
// - Null token (default struct) is valid no-op for disabled governors.
// =============================================================================

using System.Diagnostics;

namespace pengdows.crud.infrastructure;

internal readonly struct PoolSlotAdaptiveCopy : IDisposable, IAsyncDisposable
{
    private readonly PoolSlotAdaptiveCopyToken? _token;

    internal PoolSlotAdaptiveCopy(PoolSlotAdaptiveCopyToken token)
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

    internal sealed class PoolSlotAdaptiveCopyToken
    {
        private readonly PoolGovernorAdaptiveCopy _governor;
        private readonly long _waitStart;
        private readonly long _acquiredAt;
        private readonly bool _releaseWriterTurnstileInterest;
        private int _released;

        internal PoolSlotAdaptiveCopyToken(PoolGovernorAdaptiveCopy governor, long waitStart, bool releaseWriterTurnstileInterest)
        {
            _governor = governor;
            _waitStart = waitStart;
            _acquiredAt = Stopwatch.GetTimestamp();
            _releaseWriterTurnstileInterest = releaseWriterTurnstileInterest;
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                var releasedAt = Stopwatch.GetTimestamp();
                _governor.ReleaseToken(_waitStart, _acquiredAt, releasedAt, _releaseWriterTurnstileInterest);
            }
        }
    }
}

