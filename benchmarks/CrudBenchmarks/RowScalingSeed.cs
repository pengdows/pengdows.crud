namespace CrudBenchmarks;

/// <summary>
/// The seed shared by the row-scaling benchmarks: the equal-footing classes' row formula (age = 20 + i % 50, active when
/// i is even), at a size where both read queries can return 10,000 rows.
/// </summary>
internal static class RowScalingSeed
{
    internal const int Rows = 60_000;
    internal const int ReadListMinAgeExclusive = 30;
    internal const int FilteredMinAge = 25;
    internal const int FilteredMaxAge = 45;

    // SQL Server accepts at most 1,000 rows in one INSERT ... VALUES.
    private const int BatchSize = 1000;

    internal static int Age(int i) => 20 + (i % 50);

    internal static bool IsActive(int i) => i % 2 == 0;

    internal static int ReadListMatches(int rows) =>
        Enumerable.Range(1, rows).Count(i => Age(i) > ReadListMinAgeExclusive);

    internal static int FilteredQueryMatches(int rows) =>
        Enumerable.Range(1, rows).Count(i => IsActive(i) && Age(i) >= FilteredMinAge && Age(i) <= FilteredMaxAge);

    internal static IEnumerable<(int First, int Count)> Batches(int rows)
    {
        for (var first = 1; first <= rows; first += BatchSize)
        {
            yield return (first, Math.Min(BatchSize, rows - first + 1));
        }
    }

    /// <summary>One multi-row INSERT per batch; <paramref name="row"/> renders one VALUES tuple.</summary>
    internal static IEnumerable<string> InsertStatements(int rows, Func<int, string> row)
    {
        foreach (var (first, count) in Batches(rows))
        {
            yield return "INSERT INTO benchmark (name, age, salary, is_active, created_at) VALUES " +
                         string.Join(',', Enumerable.Range(first, count).Select(row));
        }
    }
}
