namespace CrudBenchmarks;

/// <summary>
/// Run once per case in GlobalSetup: an arm that returns fewer rows, or leaves a column at its default (a Dapper arm
/// without underscore matching skips is_active and created_at), does less work than the others and invalidates the case.
/// </summary>
internal static class RowScalingValidation
{
    internal static void Check<T>(string arm, int expectedRows, IReadOnlyCollection<T> rows,
        Func<T, (string Name, string CreatedAt, bool MatchesFilter)> describe)
    {
        if (rows.Count != expectedRows)
        {
            throw new InvalidOperationException($"{arm} returned {rows.Count} rows, expected {expectedRows}.");
        }

        foreach (var row in rows)
        {
            var (name, createdAt, matchesFilter) = describe(row);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(createdAt) || !matchesFilter)
            {
                throw new InvalidOperationException(
                    $"{arm} returned a row that is not fully hydrated or does not match its filter " +
                    $"(name '{name}', created_at '{createdAt}', filter {matchesFilter}).");
            }
        }
    }
}
