// =============================================================================
// FILE: IReaderLifetimeListener.cs
// PURPOSE: Internal callback for reader lifetime events.
// =============================================================================

namespace pengdows.crud.infrastructure;

internal interface IReaderLifetimeListener
{
    void OnReaderDisposed();
}
