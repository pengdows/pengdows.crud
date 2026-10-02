using System;
using CrudBenchmarks;
using Xunit;

namespace CrudBenchmarks.Tests;

// Backs PostgreSqlMethodologyBenchmarks' "Pinned" jobs: the database container gets the
// highest-numbered cores (Docker cpuset) and the benchmark process the rest (affinity mask), so
// client and server never compete for a core.
public sealed class CpuSplitTests
{
    [Fact]
    public void For_EightCoresTwoForTheDatabase_GivesTheDatabaseTheTopTwo()
    {
        var split = CpuSplit.For(processorCount: 8, databaseCores: 2);

        Assert.Equal("6-7", split.DatabaseCpuset);
        Assert.Equal((IntPtr)0b0011_1111, split.BenchmarkAffinity);
        Assert.Equal(6, split.BenchmarkCores);
    }

    [Fact]
    public void For_OneDatabaseCore_UsesASingleCpuNotARange()
    {
        var split = CpuSplit.For(processorCount: 4, databaseCores: 1);

        Assert.Equal("3", split.DatabaseCpuset);
        Assert.Equal((IntPtr)0b0111, split.BenchmarkAffinity);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 8)]
    [InlineData(1, 1)]
    [InlineData(80, 2)]
    public void For_NoCoreLeftForOneSideOrBeyondTheAffinityMask_Throws(int processorCount, int databaseCores)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuSplit.For(processorCount, databaseCores));
    }

    [Fact]
    public void Describe_NamesBothSides()
    {
        Assert.Equal("database cpus 6-7, benchmark cpus 0-5", CpuSplit.For(8, 2).Describe());
    }
}
