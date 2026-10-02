using System.Text;
using System.Text.RegularExpressions;

namespace CrudBenchmarks;

/// <summary>One statement pg_stat_statements recorded during a benchmark case, and how often.</summary>
internal sealed record SqlProofStatement(string Query, long Calls);

/// <summary>
/// What reached PostgreSQL during one benchmark case (one job, one benchmark method): the
/// statements and the number of operations the case performed. <see cref="EqualFooting"/> marks
/// the cells that claim to send the same SQL as each other.
/// </summary>
internal sealed record SqlProofCase(string Workload, string Framework, string Job, bool EqualFooting, long Operations,
    IReadOnlyList<SqlProofStatement> Statements);

/// <summary>
/// Turns "the frameworks run on equal footing" into a checked artifact: each case must send one
/// statement per operation, and the equal-footing cells of a workload must send the same statement
/// (after normalizing identifier quoting, case, whitespace and table qualifiers) and get the same
/// number of connection resets per operation.
/// </summary>
internal static partial class SqlProof
{
    // Npgsql's reset of a pooled connection returned with prepared statements (the parts of
    // DISCARD ALL that keep them), or DISCARD ALL itself. The driver sends it, the same for every
    // framework that closes its connection, so it is counted per operation, not as extra statements.
    private static readonly HashSet<string> ConnectionResetStatements = new(StringComparer.Ordinal)
    {
        "close all", "unlisten *", "select pg_advisory_unlock_all()", "discard sequences", "discard temp",
        "reset all", "set session authorization default", "discard all"
    };

    public static bool IsConnectionReset(string query) =>
        ConnectionResetStatements.Contains(Normalize(query).TrimEnd(';'));

    private static IEnumerable<SqlProofStatement> Work(SqlProofCase c) =>
        c.Statements.Where(s => !IsConnectionReset(s.Query));

    // One reset sends each of its statements once, so the most-called reset statement counts resets.
    private static long Resets(SqlProofCase c) =>
        c.Statements.Where(s => IsConnectionReset(s.Query)).Select(s => s.Calls).DefaultIfEmpty(0).Max();

    private static string ResetsPerOperation(SqlProofCase c) =>
        (c.Operations == 0 ? 0.0 : (double)Resets(c) / c.Operations).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*([(),=<>])\s*")]
    private static partial Regex Punctuation();

    // "b.id" -> "id"
    [GeneratedRegex(@"\b[a-z_][a-z0-9_]*\.(?=[a-z_*])")]
    private static partial Regex Qualifier();

    // "from benchmark b" / "from benchmark as b" -> "from benchmark" (not "from benchmark where")
    [GeneratedRegex(@"\b(from|join|into|update) ([a-z_][a-z0-9_]*)(?: as)? (?!where\b|join\b|on\b|limit\b|order\b|group\b|set\b|values\b|inner\b|left\b|right\b|offset\b)[a-z_][a-z0-9_]*\b")]
    private static partial Regex TableAlias();

    public static string Normalize(string query)
    {
        var text = query.Replace("\"", string.Empty).ToLowerInvariant();
        text = Whitespace().Replace(text, " ").Trim();
        text = TableAlias().Replace(text, "$1 $2");
        text = Qualifier().Replace(text, string.Empty);
        text = Punctuation().Replace(text, "$1");
        return text;
    }

    public static IReadOnlyList<string> Analyze(IEnumerable<SqlProofCase> cases)
    {
        var all = cases.ToList();
        var issues = new List<string>();

        foreach (var c in all)
        {
            var total = Work(c).Sum(s => s.Calls);
            if (total != c.Operations)
            {
                issues.Add($"{c.Job} {c.Workload} {c.Framework}: {total} statements for {c.Operations} operations " +
                           $"({string.Join("; ", Work(c).Select(s => $"{s.Calls}x {OneLine(s.Query)}"))})");
            }
        }

        foreach (var group in all.Where(c => c.EqualFooting).GroupBy(c => (c.Job, c.Workload)))
        {
            var reference = group.First();
            var expected = Signature(reference);
            foreach (var other in group.Skip(1))
            {
                if (Signature(other) != expected)
                {
                    issues.Add($"{group.Key.Job} {group.Key.Workload}: {other.Framework} sent " +
                               $"`{string.Join("; ", Work(other).Select(s => OneLine(s.Query)))}` but " +
                               $"{reference.Framework} sent `{string.Join("; ", Work(reference).Select(s => OneLine(s.Query)))}`");
                }

                if (ResetsPerOperation(other) != ResetsPerOperation(reference))
                {
                    issues.Add($"{group.Key.Job} {group.Key.Workload}: {other.Framework} had {ResetsPerOperation(other)} " +
                               $"connection resets per operation but {reference.Framework} had {ResetsPerOperation(reference)}");
                }
            }
        }

        return issues;
    }

    public static string Report(IEnumerable<SqlProofCase> cases)
    {
        var all = cases.OrderBy(c => c.Job).ThenBy(c => c.Workload).ThenBy(c => c.Framework).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# SQL proof: what each benchmark case sent to PostgreSQL");
        sb.AppendLine();
        sb.AppendLine("Recorded with pg_stat_statements, reset after warm-up, per case. Equal-footing cells must send the");
        sb.AppendLine("same statement (identifier quoting, case, whitespace and table qualifiers ignored) and exactly one");
        sb.AppendLine("statement per operation. Npgsql's reset of a pooled connection (CLOSE ALL ... RESET ALL, sent by the");
        sb.AppendLine("driver when a connection returns to the pool) is not counted as a statement; it is shown per");
        sb.AppendLine("operation and must match between equal-footing cells.");
        sb.AppendLine();
        sb.AppendLine("| Job | Workload | Framework | Equal footing | Operations | Statements | Connection resets / op |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in all)
        {
            sb.AppendLine($"| {c.Job} | {c.Workload} | {c.Framework} | {(c.EqualFooting ? "yes" : "no")} | {c.Operations} | {Work(c).Sum(s => s.Calls)} | {ResetsPerOperation(c)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Statements");
        sb.AppendLine();
        sb.AppendLine("| Job | Workload | Framework | Calls | Statement |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in all)
        {
            foreach (var s in Work(c).OrderByDescending(s => s.Calls))
            {
                sb.AppendLine($"| {c.Job} | {c.Workload} | {c.Framework} | {s.Calls} | `{OneLine(s.Query).Replace("|", "\\|")}` |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Issues");
        sb.AppendLine();
        var issues = Analyze(all);
        if (issues.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var issue in issues)
            {
                sb.AppendLine($"- {issue}");
            }
        }

        return sb.ToString();
    }

    private static string Signature(SqlProofCase c) =>
        string.Join("\n", Work(c).Select(s => Normalize(s.Query)).Distinct().OrderBy(q => q, StringComparer.Ordinal));

    private static string OneLine(string query) => Whitespace().Replace(query, " ").Trim();
}
