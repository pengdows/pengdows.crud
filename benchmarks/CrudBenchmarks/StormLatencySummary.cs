using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CrudBenchmarks;

/// <summary>
/// The per-arm latency table a storm benchmark writes into its sidecar: successes and failures apart, each with its
/// count and nearest-rank P50/P95/P99 and maximum, in milliseconds from <see cref="Stopwatch"/> ticks.
/// </summary>
internal static class StormLatencySummary
{
    internal static string Format(IEnumerable<long> successTicks, IEnumerable<long> failureTicks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Calls | Count | P50 | P95 | P99 | Max |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendRow(sb, "Successes", successTicks);
        AppendRow(sb, "Failures", failureTicks);
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, IEnumerable<long> ticks)
    {
        var sorted = ticks.ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 0)
        {
            sb.AppendLine($"| {label} | 0 | - | - | - | - |");
            return;
        }

        sb.AppendLine($"| {label} | {sorted.Length} | {Ms(PercentileMath.NearestRank(sorted, 50))} | " +
                      $"{Ms(PercentileMath.NearestRank(sorted, 95))} | {Ms(PercentileMath.NearestRank(sorted, 99))} | " +
                      $"{Ms(sorted[^1])} |");
    }

    private static string Ms(double ticks) =>
        (ticks / Stopwatch.Frequency * 1000.0).ToString("F3", CultureInfo.InvariantCulture) + " ms";
}
