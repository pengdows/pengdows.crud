// =============================================================================
// FILE: PoolPermit.cs
// PURPOSE: RAII struct representing an acquired pool slot.
//
// AI SUMMARY:
// - Readonly struct implementing IDisposable and IAsyncDisposable.
// - Returned by PoolGovernor.Acquire/AcquireAsync.
// - Dispose(): Releases permit back to governor (once only).
// - PoolPermitToken: Inner class ensuring single release via Interlocked.
// - Usage: using var permit = await governor.AcquireAsync();
// - Struct design: efficient, stack-allocated, no heap pressure.
// - Null token (default struct) is valid no-op for disabled governors.
// =============================================================================

namespace pengdows.crud.infrastructure;

internal readonly struct PoolPermit : IDisposable, IAsyncDisposable
{
    private readonly PoolPermitToken? _token;

    internal PoolPermit(PoolPermitToken token)
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

    internal sealed class PoolPermitToken
    {
        private readonly PoolGovernor _governor;
        private int _released;

        internal PoolPermitToken(PoolGovernor governor)
        {
            _governor = governor;
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _governor.Release();
            }
        }
    }
}