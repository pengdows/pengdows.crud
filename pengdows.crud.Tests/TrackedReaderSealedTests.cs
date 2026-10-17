using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TrackedReader is tested for by type on every typed read (TypedFieldReader.Read, once per column per row). A sealed
/// class makes that a method-table compare; an unsealed one walks the hierarchy.
/// </summary>
public class TrackedReaderSealedTests
{
    [Fact]
    public void TrackedReader_IsSealed() => Assert.True(typeof(TrackedReader).IsSealed);
}
