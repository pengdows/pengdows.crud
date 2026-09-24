using Xunit;

namespace pengdows.crud.Tests.Collections;

/// <summary>
/// Uuid7Optimized keeps process-wide static state (configuration, global epoch, per-thread
/// counters) that these tests reconfigure or reset. Running them in parallel lets one test's
/// Configure/reset leak into another's assertions.
/// </summary>
[CollectionDefinition("Uuid7StaticStateSerial", DisableParallelization = true)]
public class Uuid7StaticStateSerialCollection
{
    // Intentionally empty; used only for the collection definition.
}
