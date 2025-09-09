using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace CrudBenchmarks;

internal static class PgStats
{
    public static async Task ResetAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync("SELECT pg_stat_statements_reset();");
    }

    public static async Task DumpSummaryAsync(string connStr, string? label = null)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        Console.WriteLine(label == null
            ? "==== PostgreSQL Statistics Summary ===="
            : $"==== PostgreSQL Statistics Summary: {label} ====");

        // Top queries by total_exec_time (renamed from total_time in PostgreSQL 13+)
        var top = (await conn.QueryAsync(
            @"SELECT query,
                     calls,
                     rows,
                     total_exec_time as total_time,
                     mean_exec_time as mean_time,
                     min_exec_time as min_time,
                     max_exec_time as max_time,
                     shared_blks_hit,
                     shared_blks_read,
                     blk_read_time,
                     blk_write_time
                FROM pg_stat_statements s
                JOIN pg_database d ON d.oid = s.dbid
               WHERE d.datname = current_database()
            ORDER BY total_exec_time DESC
               LIMIT 10"))
            .ToList();

        Console.WriteLine("-- Top 10 queries by total_time --");
        foreach (var q in top)
        {
            Console.WriteLine(
                $"calls={q.calls,6} mean={q.mean_time,8:0.000}ms total={q.total_time,9:0.000}ms rows={q.rows,6} max={q.max_time,8:0.000}ms | {TrimQuery((string)q.query)}");
        }

        // Table-centric summaries (film, film_actor)
        await DumpTableSummaryAsync(conn, "film");
        await DumpTableSummaryAsync(conn, "film_actor");

        // Connection activity and pool usage
        Console.WriteLine("-- Connection activity (pg_stat_activity) --");
        var act = (await conn.QueryAsync(
            @"SELECT state,
                     count(*) as cnt,
                     avg(EXTRACT(EPOCH FROM now() - backend_start))*1000 as avg_backend_ms,
                     max(EXTRACT(EPOCH FROM now() - backend_start))*1000 as max_backend_ms,
                     avg(EXTRACT(EPOCH FROM now() - COALESCE(xact_start, now())))*1000 as avg_xact_ms,
                     max(EXTRACT(EPOCH FROM now() - COALESCE(xact_start, now())))*1000 as max_xact_ms
                FROM pg_stat_activity
               WHERE datname = current_database()
            GROUP BY state
            ORDER BY cnt DESC"))
            .ToList();
        foreach (var r in act)
        {
            Console.WriteLine(
                $"state={r.state,-12} count={r.cnt,3} avg_backend={r.avg_backend_ms,8:0.0}ms max_backend={r.max_backend_ms,8:0.0}ms avg_xact={r.avg_xact_ms,8:0.0}ms max_xact={r.max_xact_ms,8:0.0}ms");
        }

        // Connection pool details
        Console.WriteLine("-- Connection pool details --");
        var poolDetails = (await conn.QueryAsync(
            @"SELECT application_name,
                     client_addr,
                     client_port,
                     state,
                     backend_start,
                     state_change,
                     query_start,
                     xact_start,
                     EXTRACT(EPOCH FROM now() - backend_start)*1000 as backend_age_ms,
                     EXTRACT(EPOCH FROM now() - state_change)*1000 as state_age_ms
                FROM pg_stat_activity
               WHERE datname = current_database()
                 AND pid != pg_backend_pid()
            ORDER BY backend_start"))
            .ToList();
        
        foreach (var p in poolDetails)
        {
            Console.WriteLine(
                $"app={p.application_name ?? "null",-15} addr={p.client_addr ?? "local",-12} port={p.client_port,5} state={p.state,-8} backend_age={p.backend_age_ms,8:0.0}ms state_age={p.state_age_ms,8:0.0}ms");
        }

        // Database-level statistics
        Console.WriteLine("-- Database connection summary --");
        var dbStats = (await conn.QuerySingleAsync(
            @"SELECT d.datname,
                     d.numbackends as active_connections,
                     d.xact_commit,
                     d.xact_rollback,
                     d.blks_read,
                     d.blks_hit,
                     CASE WHEN (d.blks_read + d.blks_hit) > 0 
                          THEN round((d.blks_hit::numeric / (d.blks_read + d.blks_hit)) * 100, 2)
                          ELSE 0 
                     END as cache_hit_ratio
                FROM pg_stat_database d
               WHERE d.datname = current_database()"));
        
        Console.WriteLine(
            $"db={dbStats.datname} active_conn={dbStats.active_connections} commits={dbStats.xact_commit} rollbacks={dbStats.xact_rollback} cache_hit_ratio={dbStats.cache_hit_ratio}%");

        Console.WriteLine(label == null
            ? "==== End PostgreSQL Statistics Summary ===="
            : $"==== End PostgreSQL Statistics Summary: {label} ====");
    }

    private static async Task DumpTableSummaryAsync(NpgsqlConnection conn, string table)
    {
        Console.WriteLine($"-- Summary for table: {table} --");
        var sql = @"SELECT SUM(calls) AS calls,
                           SUM(rows) AS rows,
                           SUM(total_exec_time) AS total_time_ms,
                           SUM(blk_read_time) AS read_ms,
                           SUM(blk_write_time) AS write_ms
                      FROM pg_stat_statements s
                      JOIN pg_database d ON d.oid = s.dbid
                     WHERE d.datname = current_database()
                       AND query ILIKE @pattern";
        var result = await conn.QuerySingleOrDefaultAsync(sql, new { pattern = $"%from%{table}%" });
        if (result == null)
        {
            Console.WriteLine("(no matching queries)");
            return;
        }

        Console.WriteLine(
            $"calls={result.calls ?? 0,6} rows={result.rows ?? 0,6} total={result.total_time_ms ?? 0.0,10:0.000}ms read_io={result.read_ms ?? 0.0,10:0.000}ms write_io={result.write_ms ?? 0.0,10:0.000}ms");
    }

    private static string TrimQuery(string q)
    {
        q = q.Replace('\n', ' ').Replace('\r', ' ');
        return q.Length <= 120 ? q : q.Substring(0, 117) + "...";
    }
}
