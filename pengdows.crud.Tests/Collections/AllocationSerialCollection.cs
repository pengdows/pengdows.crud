using Xunit;

namespace pengdows.crud.Tests.Collections;

/// <summary>
/// Tests that measure allocations run alone: GC.GetAllocatedBytesForCurrentThread also counts what
/// other tests' work does on the measuring thread, and shared caches other tests fill grow under
/// it, so under a parallel full-suite run an allocation comparison failed with no allocation
/// defect (CreateAsyncIdLeaseTests, net8 and net10).
/// </summary>
[CollectionDefinition("AllocationSerial", DisableParallelization = true)]
public class AllocationSerialCollection
{
    // Intentionally empty; used only for the collection definition.
}
