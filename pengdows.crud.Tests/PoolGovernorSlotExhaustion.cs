using System;
using System.Threading;
using pengdows.crud.infrastructure;

namespace pengdows.crud.Tests;

/// <summary>
/// Fills a governor's slots so the next acquire must wait for one, while leaving the test-owned turnstile free:
/// the state "turnstile passes instantly, slot is the bottleneck" that the failure-path tests exercise. The slot is
/// taken through the governor, which for a writer also takes the turnstile, so the turnstile is handed back at once
/// and taken again before the slot is released.
/// </summary>
internal static class PoolGovernorSlotExhaustion
{
    internal static IDisposable Hold(PoolGovernor governor, SemaphoreSlim turnstile)
    {
        var slot = governor.Acquire();
        turnstile.Release();
        return new Held(slot, turnstile);
    }

    private sealed class Held : IDisposable
    {
        private readonly PoolSlot _slot;
        private readonly SemaphoreSlim _turnstile;

        internal Held(PoolSlot slot, SemaphoreSlim turnstile)
        {
            _slot = slot;
            _turnstile = turnstile;
        }

        public void Dispose()
        {
            _turnstile.Wait(0);
            _slot.Dispose();
        }
    }
}
