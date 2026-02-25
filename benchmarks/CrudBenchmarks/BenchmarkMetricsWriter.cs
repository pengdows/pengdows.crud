using System.Diagnostics;
using System.Text;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.metrics;

namespace CrudBenchmarks;

/// <summary>
/// Writes a sidecar markdown file alongside BenchmarkDotNet artifacts containing
/// pengdows.crud internal metrics: connection hold times, command latencies,
/// P95/P99 percentiles, and pool governor stats (avgWait, avgHold, peak usage).
///
/// Output path: BenchmarkDotNet.Artifacts/results/{benchmarkClass}-pengdows-metrics.md
/// Each call appends a new section so multiple [Params] combinations accumulate in one file.
/// </summary>
internal static class BenchmarkMetricsWriter
{
    private static readonly string ArtifactsDir =
        Path.Combine("BenchmarkDotNet.Artifacts", "results");

    /// <summary>
    /// Writes pengdows.crud metrics to a sidecar file and the console.
    /// </summary>
    /// <param name="benchmarkClassName">Used to derive the output file name.</param>
    /// <param name="context">The DatabaseContext whose metrics to capture.</param>
    /// <param name="label">Optional label for this measurement (e.g. param values).</param>
    public static void Write(string benchmarkClassName, DatabaseContext context, string? label = null)
    {
        var sb = new StringBuilder();
        var header = label == null
            ? $"## pengdows.crud Metrics — {benchmarkClassName}"
            : $"## pengdows.crud Metrics — {benchmarkClassName} | {label}";

        sb.AppendLine(header);
        sb.AppendLine();
        AppendContextMetrics(sb, context);
        AppendRoleMetrics(sb, context);
        AppendGovernorMetrics(sb, context, PoolLabel.Writer, "Writer");
        AppendGovernorMetrics(sb, context, PoolLabel.Reader, "Reader");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var text = sb.ToString();

        // Always echo to Console so it appears in BDN job logs
        Console.Write(text);

        // Append to sidecar file (multiple [Params] combos accumulate in one file)
        try
        {
            Directory.CreateDirectory(ArtifactsDir);
            var path = Path.Combine(ArtifactsDir, $"{benchmarkClassName}-pengdows-metrics.md");
            File.AppendAllText(path, text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkMetricsWriter] Failed to write sidecar file: {ex.Message}");
        }
    }

    private static void AppendContextMetrics(StringBuilder sb, DatabaseContext context)
    {
        DatabaseMetrics m;
        try
        {
            m = context.Metrics;
        }
        catch
        {
            sb.AppendLine("_Context metrics unavailable._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("### Combined Metrics (Read + Write)");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Connections Current | {m.ConnectionsCurrent} |");
        sb.AppendLine($"| Peak Open Connections | {m.PeakOpenConnections} |");
        sb.AppendLine($"| Connections Opened | {m.ConnectionsOpened} |");
        sb.AppendLine($"| Connections Closed | {m.ConnectionsClosed} |");
        sb.AppendLine($"| Avg Connection Hold | {m.AvgConnectionHoldMs:0.000} ms |");
        sb.AppendLine($"| Avg Connection Open | {m.AvgConnectionOpenMs:0.000} ms |");
        sb.AppendLine($"| Avg Connection Close | {m.AvgConnectionCloseMs:0.000} ms |");
        sb.AppendLine($"| Long-Lived Connections | {m.LongLivedConnections} |");
        sb.AppendLine($"| Commands Executed | {m.CommandsExecuted} |");
        sb.AppendLine($"| Commands Failed | {m.CommandsFailed} |");
        sb.AppendLine($"| Commands Timed Out | {m.CommandsTimedOut} |");
        sb.AppendLine($"| Commands Cancelled | {m.CommandsCancelled} |");
        sb.AppendLine($"| Avg Command | {m.AvgCommandMs:0.000} ms |");
        sb.AppendLine($"| P95 Command | {m.P95CommandMs:0.000} ms |");
        sb.AppendLine($"| P99 Command | {m.P99CommandMs:0.000} ms |");
        sb.AppendLine($"| Rows Read Total | {m.RowsReadTotal} |");
        sb.AppendLine($"| Rows Affected Total | {m.RowsAffectedTotal} |");
        sb.AppendLine($"| Prepared Statements | {m.PreparedStatements} |");
        sb.AppendLine($"| Statements Cached | {m.StatementsCached} |");
        sb.AppendLine($"| Statements Evicted | {m.StatementsEvicted} |");
        sb.AppendLine($"| Avg Transaction | {m.AvgTransactionMs:0.000} ms |");
        sb.AppendLine($"| Peak Concurrent Transactions | {m.TransactionsMax} |");
        sb.AppendLine();
    }

    private static void AppendRoleMetrics(StringBuilder sb, DatabaseContext context)
    {
        DatabaseMetrics m;
        try
        {
            m = context.Metrics;
        }
        catch
        {
            return;
        }

        AppendRoleTable(sb, "Read", m.Read);
        AppendRoleTable(sb, "Write", m.Write);
    }

    private static void AppendRoleTable(StringBuilder sb, string role, DatabaseRoleMetrics r)
    {
        if (r.ConnectionsOpened == 0 && r.CommandsExecuted == 0)
        {
            return;
        }

        sb.AppendLine($"### Role Metrics — {role}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Connections Opened | {r.ConnectionsOpened} |");
        sb.AppendLine($"| Peak Open | {r.PeakOpenConnections} |");
        sb.AppendLine($"| Avg Connection Hold | {r.AvgConnectionHoldMs:0.000} ms |");
        sb.AppendLine($"| Avg Connection Open | {r.AvgConnectionOpenMs:0.000} ms |");
        sb.AppendLine($"| Commands Executed | {r.CommandsExecuted} |");
        sb.AppendLine($"| Avg Command | {r.AvgCommandMs:0.000} ms |");
        sb.AppendLine($"| P95 Command | {r.P95CommandMs:0.000} ms |");
        sb.AppendLine($"| P99 Command | {r.P99CommandMs:0.000} ms |");
        sb.AppendLine($"| Rows Read | {r.RowsReadTotal} |");
        sb.AppendLine($"| Rows Affected | {r.RowsAffectedTotal} |");
        sb.AppendLine($"| Prepared Statements | {r.PreparedStatements} |");
        sb.AppendLine();
    }

    private static void AppendGovernorMetrics(StringBuilder sb, DatabaseContext context,
        PoolLabel label, string displayLabel)
    {
        PoolStatisticsSnapshot s;
        try
        {
            s = context.GetPoolStatisticsSnapshot(label);
        }
        catch
        {
            return;
        }

        if (s.Disabled || s.TotalAcquired == 0)
        {
            return;
        }

        var avgWaitMs = s.TotalWaitTicks * 1000.0 / Stopwatch.Frequency / s.TotalAcquired;
        var avgHoldMs = s.TotalHoldTicks * 1000.0 / Stopwatch.Frequency / s.TotalAcquired;

        sb.AppendLine($"### Pool Governor — {displayLabel}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Max Slots | {s.MaxSlots} |");
        sb.AppendLine($"| Peak In-Use | {s.PeakInUse} |");
        sb.AppendLine($"| Peak Queued | {s.PeakQueued} |");
        sb.AppendLine($"| Peak Turnstile Queued | {s.PeakTurnstileQueued} |");
        sb.AppendLine($"| Total Acquired | {s.TotalAcquired} |");
        sb.AppendLine($"| Avg Wait | {avgWaitMs:0.000} ms |");
        sb.AppendLine($"| Avg Hold | {avgHoldMs:0.000} ms |");
        sb.AppendLine($"| Slot Timeouts | {s.TotalSlotTimeouts} |");
        sb.AppendLine($"| Turnstile Timeouts | {s.TotalTurnstileTimeouts} |");
        sb.AppendLine($"| Canceled Waits | {s.TotalCanceledWaits} |");
        sb.AppendLine();
    }
}
