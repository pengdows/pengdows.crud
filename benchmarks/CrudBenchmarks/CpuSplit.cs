using System.Globalization;

namespace CrudBenchmarks;

/// <summary>
/// Splits the machine's cores between the database container (the highest-numbered cores, as a
/// Docker cpuset) and the benchmark process (the rest, as an affinity mask), so the client and
/// server never compete for a core while the benchmark measures differences of a few percent.
/// </summary>
internal sealed record CpuSplit(string DatabaseCpuset, IntPtr BenchmarkAffinity, int BenchmarkCores)
{
    // The affinity mask is a pointer-sized bit set.
    private static readonly int MaxCores = IntPtr.Size * 8 - 1;

    public static CpuSplit For(int processorCount, int databaseCores)
    {
        if (processorCount < 2 || processorCount > MaxCores)
        {
            throw new ArgumentOutOfRangeException(nameof(processorCount), processorCount,
                $"Pinning needs 2 to {MaxCores} cores.");
        }

        if (databaseCores < 1 || databaseCores >= processorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(databaseCores), databaseCores,
                $"The database needs 1 to {processorCount - 1} of the {processorCount} cores.");
        }

        var benchmarkCores = processorCount - databaseCores;
        var first = benchmarkCores.ToString(CultureInfo.InvariantCulture);
        var last = (processorCount - 1).ToString(CultureInfo.InvariantCulture);
        return new CpuSplit(
            databaseCores == 1 ? last : $"{first}-{last}",
            (IntPtr)((1L << benchmarkCores) - 1),
            benchmarkCores);
    }

    public string Describe() =>
        BenchmarkCores == 1
            ? $"database cpus {DatabaseCpuset}, benchmark cpus 0"
            : $"database cpus {DatabaseCpuset}, benchmark cpus 0-{BenchmarkCores - 1}";
}
