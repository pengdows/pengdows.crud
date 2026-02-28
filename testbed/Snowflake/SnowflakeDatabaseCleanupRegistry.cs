using System.Collections.Concurrent;
using Snowflake.Data.Client;

namespace testbed.Snowflake;

/// <summary>
/// Process-level registry that guarantees Snowflake test databases and schemas are dropped
/// even when the test process is killed or crashes before <see cref="SnowflakeTestContainer"/>
/// can run its normal <c>DisposeAsyncCore</c>.
///
/// Hooks into <see cref="AppDomain.ProcessExit"/> and <see cref="Console.CancelKeyPress"/>
/// so that resources are cleaned up on SIGTERM, Ctrl+C, and graceful process exit.
/// (SIGKILL cannot be intercepted by any in-process mechanism.)
/// </summary>
internal static class SnowflakeDatabaseCleanupRegistry
{
    private static readonly ConcurrentDictionary<Guid, CleanupEntry> _registry = new();

    static SnowflakeDatabaseCleanupRegistry()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <summary>
    /// Registers a set of drop statements to execute on process exit.
    /// Returns an ID that must be passed to <see cref="Deregister"/> after a successful normal disposal.
    /// </summary>
    public static Guid Register(string adminConnectionString, IReadOnlyList<string> dropSqls, string label)
    {
        var id = Guid.NewGuid();
        _registry[id] = new CleanupEntry(adminConnectionString, dropSqls, label);
        return id;
    }

    /// <summary>
    /// Removes an entry from the registry. Call this after the resource has been
    /// successfully dropped through the normal disposal path.
    /// </summary>
    public static void Deregister(Guid id) => _registry.TryRemove(id, out _);

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Cancel the default Ctrl+C termination so our cleanup can finish first.
        e.Cancel = true;
        CleanupAll();
    }

    private static void OnProcessExit(object? sender, EventArgs e) => CleanupAll();

    private static void CleanupAll()
    {
        // Snapshot and clear atomically so a re-entrant call is a no-op.
        var keys = _registry.Keys.ToArray();
        var entries = new List<CleanupEntry>(keys.Length);
        foreach (var key in keys)
        {
            if (_registry.TryRemove(key, out var entry))
            {
                entries.Add(entry);
            }
        }

        foreach (var entry in entries)
        {
            ExecuteCleanupSync(entry);
        }
    }

    /// <summary>
    /// Synchronous cleanup used from process-exit handlers where async is not available.
    /// </summary>
    private static void ExecuteCleanupSync(CleanupEntry entry)
    {
        try
        {
            using var conn = SnowflakeDbFactory.Instance.CreateConnection();
            conn.ConnectionString = entry.AdminConnectionString;
            conn.Open();

            using var cmd = conn.CreateCommand();
            foreach (var sql in entry.DropSqls)
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            conn.Close();
            Console.WriteLine($"[Snowflake] Emergency cleanup completed: {entry.Label}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Snowflake] Emergency cleanup FAILED for {entry.Label}: {ex.Message}");
        }
    }

    private sealed record CleanupEntry(
        string AdminConnectionString,
        IReadOnlyList<string> DropSqls,
        string Label);
}