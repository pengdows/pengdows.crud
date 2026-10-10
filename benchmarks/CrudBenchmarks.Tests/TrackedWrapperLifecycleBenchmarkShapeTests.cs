using CrudBenchmarks;

namespace CrudBenchmarks.Tests;

// The lifecycle benchmarks time what every query pays around its work: a tracked reader built, read and disposed,
// and a tracked connection opened and closed. A benchmark that did none of that would be timing nothing.
public class TrackedWrapperLifecycleBenchmarkShapeTests
{
    [Fact]
    public void TheReaderLifecycle_ReadsTheRowAndDisposesTheReader()
    {
        var benchmarks = new TrackedWrapperLifecycleBenchmarks();
        benchmarks.Setup();

        Assert.Equal(1, benchmarks.ReaderLifecycle());
        Assert.Equal(1, benchmarks.ReaderLifecycle()); // repeatable: nothing is used up
    }

    [Fact]
    public void TheConnectionLifecycle_OpensAndClosesTheConnection()
    {
        var benchmarks = new TrackedWrapperLifecycleBenchmarks();
        benchmarks.Setup();

        Assert.True(benchmarks.ConnectionLifecycle());
        Assert.True(benchmarks.ConnectionLifecycle());
    }
}
