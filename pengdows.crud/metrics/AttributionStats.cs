// =============================================================================
// FILE: AttributionStats.cs
// PURPOSE: Tracks read/write request attribution and governor wait statistics.
//
// AI SUMMARY:
// - Internal metrics collector for request attribution.
// - Thread-safe: all counters use Interlocked operations.
// - Tracked metrics:
//   * ReadRequests, WriteRequests: Total operation counts
// - GetSnapshot(): Returns immutable request-count snapshot.
// - Used for debugging pool pressure and read/write distribution.
// =============================================================================

namespace pengdows.crud.metrics;

internal sealed class AttributionStats
{
    private long _readRequests;
    private long _writeRequests;
    public void RecordReadRequest()
    {
        Interlocked.Increment(ref _readRequests);
    }

    public void RecordWriteRequest()
    {
        Interlocked.Increment(ref _writeRequests);
    }

    public AttributionSnapshot GetSnapshot()
    {
        return new AttributionSnapshot(
            Interlocked.Read(ref _readRequests),
            Interlocked.Read(ref _writeRequests));
    }
}

internal readonly record struct AttributionSnapshot(
    long ReadRequests,
    long WriteRequests);
