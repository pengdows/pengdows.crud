// =============================================================================
// FILE: AttributionStats.cs
// PURPOSE: Tracks read/write request attribution and governor wait statistics.
//
// AI SUMMARY:
// - Internal metrics collector for request attribution.
// - Thread-safe: all counters use Interlocked operations.
// - Tracked metrics:
//   * ReadRequests, WriteRequests: Total operation counts
//   * ReadGovernorWaits, WriteGovernorWaits: Pool wait events
//   * ReadGovernorTimeouts, WriteGovernorTimeouts: Pool timeout counts
//   * ReadModeWaits, WriteModeWaits: Mode lock contention events
// - GetSnapshot(): Returns immutable AttributionSnapshot.
// - AttributionSnapshot: Readonly record struct with all counters.
// - Used for debugging pool pressure and read/write distribution.
// =============================================================================

namespace pengdows.crud.metrics;

internal sealed class AttributionStats
{
    private long _readRequests;
    private long _writeRequests;
    private long _readGovernorWaits;
    private long _writeGovernorWaits;
    private long _readGovernorTimeouts;
    private long _writeGovernorTimeouts;
    private long _readModeWaits;
    private long _writeModeWaits;

    public void RecordReadRequest()
    {
        Interlocked.Increment(ref _readRequests);
    }

    public void RecordWriteRequest()
    {
        Interlocked.Increment(ref _writeRequests);
    }

    public void RecordReadGovernorWait()
    {
        Interlocked.Increment(ref _readGovernorWaits);
    }

    public void RecordWriteGovernorWait()
    {
        Interlocked.Increment(ref _writeGovernorWaits);
    }

    public void RecordReadGovernorTimeout()
    {
        Interlocked.Increment(ref _readGovernorTimeouts);
    }

    public void RecordWriteGovernorTimeout()
    {
        Interlocked.Increment(ref _writeGovernorTimeouts);
    }

    public void RecordReadModeWait()
    {
        Interlocked.Increment(ref _readModeWaits);
    }

    public void RecordWriteModeWait()
    {
        Interlocked.Increment(ref _writeModeWaits);
    }

    public AttributionSnapshot GetSnapshot()
    {
        return new AttributionSnapshot(
            Interlocked.Read(ref _readRequests),
            Interlocked.Read(ref _writeRequests),
            Interlocked.Read(ref _readGovernorWaits),
            Interlocked.Read(ref _writeGovernorWaits),
            Interlocked.Read(ref _readGovernorTimeouts),
            Interlocked.Read(ref _writeGovernorTimeouts),
            Interlocked.Read(ref _readModeWaits),
            Interlocked.Read(ref _writeModeWaits));
    }
}

internal readonly record struct AttributionSnapshot(
    long ReadRequests,
    long WriteRequests,
    long ReadGovernorWaits,
    long WriteGovernorWaits,
    long ReadGovernorTimeouts,
    long WriteGovernorTimeouts,
    long ReadModeWaits,
    long WriteModeWaits);