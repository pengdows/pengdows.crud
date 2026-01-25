namespace pengdows.crud.enums;

/// <summary>
/// Specifies how DatabaseContext manages connection lifecycle and concurrency.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> All modes are fully thread-safe thanks to internal locking mechanisms.
/// DatabaseContext can be safely used as a singleton or from multiple threads concurrently.
/// </para>
/// <para>
/// <b>Choosing a Mode:</b> Use <see cref="Best"/> to automatically select the optimal mode based on
/// database type and connection string. Explicit modes are available when you need specific behavior
/// or want to override auto-detection.
/// </para>
/// <para>
/// <b>Mode/Database Matching:</b> Using a mode that doesn't match your database characteristics
/// is safe for correctness but may cause performance issues. For example:
/// <list type="bullet">
///   <item><description>SQL Server with SingleConnection: Safe but serializes all operations (poor throughput)</description></item>
///   <item><description>SQLite file with Standard: Safe but may cause SQLITE_BUSY errors under write contention</description></item>
/// </list>
/// pengdows.crud will log warnings when detecting mode/database mismatches.
/// </para>
/// </remarks>
public enum DbMode
{
    /// <summary>
    /// Opens a new connection for each operation, closes immediately after.
    /// Relies on provider-managed connection pooling.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item><description>Each database operation gets a fresh connection from the pool</description></item>
    ///   <item><description>Connection closes as soon as the operation completes</description></item>
    ///   <item><description>Inside transactions, connection stays open for transaction lifetime</description></item>
    ///   <item><description>Fully concurrent: multiple threads can execute operations in parallel</description></item>
    /// </list>
    /// <para><b>Best For:</b></para>
    /// <list type="bullet">
    ///   <item><description>Client-server databases (SQL Server, PostgreSQL, MySQL, Oracle, CockroachDB)</description></item>
    ///   <item><description>Production workloads requiring high throughput and concurrency</description></item>
    ///   <item><description>Cloud deployments with connection pooling</description></item>
    /// </list>
    /// <para><b>Operational Characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description>Excellent scalability: supports hundreds of concurrent operations</description></item>
    ///   <item><description>Minimal connection lifetime reduces resource usage and cloud costs</description></item>
    ///   <item><description>Requires database provider to support connection pooling</description></item>
    /// </list>
    /// <para><b>⚠️ Warnings:</b></para>
    /// <list type="bullet">
    ///   <item><description>SQLite file databases without WAL: May experience SQLITE_BUSY errors under write contention. Consider <see cref="SingleWriter"/> mode or enable WAL (PRAGMA journal_mode=WAL).</description></item>
    /// </list>
    /// </remarks>
    Standard = 0,

    /// <summary>
    /// Keeps one sentinel connection open permanently to prevent database unload.
    /// Otherwise behaves like <see cref="Standard"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item><description>One persistent "sentinel" connection stays open for the lifetime of the DatabaseContext</description></item>
    ///   <item><description>Sentinel connection is NEVER used for actual work</description></item>
    ///   <item><description>All operations open/close connections like Standard mode</description></item>
    ///   <item><description>Prevents database unload in embedded/user-mode databases</description></item>
    /// </list>
    /// <para><b>Best For:</b></para>
    /// <list type="bullet">
    ///   <item><description>SQL Server Express LocalDB (prevents automatic database unload)</description></item>
    ///   <item><description>Development environments with embedded databases</description></item>
    ///   <item><description>Any database where keeping it "loaded" improves startup latency</description></item>
    /// </list>
    /// <para><b>Operational Characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description>Same concurrency as Standard mode</description></item>
    ///   <item><description>Slightly higher baseline resource usage (one extra connection)</description></item>
    ///   <item><description>Eliminates cold-start latency for subsequent operations</description></item>
    /// </list>
    /// </remarks>
    KeepAlive = 1,

    /// <summary>
    /// Maintains one persistent write connection, acquires ephemeral read connections as needed.
    /// Designed for databases with single-writer constraints.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item><description>One persistent connection handles ALL write operations</description></item>
    ///   <item><description>Read operations open ephemeral connections (opened/closed per operation)</description></item>
    ///   <item><description>Serializes writes through the single writer connection</description></item>
    ///   <item><description>Reads can execute concurrently on separate connections</description></item>
    ///   <item><description>Inside transactions, all operations (read and write) use the transaction's connection</description></item>
    /// </list>
    /// <para><b>Best For:</b></para>
    /// <list type="bullet">
    ///   <item><description>File-based SQLite databases (including those with WAL mode enabled)</description></item>
    ///   <item><description>DuckDB file databases</description></item>
    ///   <item><description>Named in-memory databases with shared cache (e.g., SQLite "mode=memory;cache=shared")</description></item>
    /// </list>
    /// <para><b>Operational Characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description>Avoids SQLITE_BUSY errors by coordinating writes through one connection</description></item>
    ///   <item><description>Write throughput is serialized (one write at a time)</description></item>
    ///   <item><description>Read throughput is concurrent (multiple simultaneous reads)</description></item>
    ///   <item><description>WAL mode note: SQLite with WAL allows many readers + one writer, NOT multiple concurrent writers</description></item>
    /// </list>
    /// <para><b>⚠️ Warnings:</b></para>
    /// <list type="bullet">
    ///   <item><description>Client-server databases: Using this mode with SQL Server, PostgreSQL, etc. is safe but unnecessarily restricts write concurrency. Consider <see cref="Standard"/> mode.</description></item>
    /// </list>
    /// </remarks>
    SingleWriter = 2,

    /// <summary>
    /// All operations share a single persistent connection.
    /// Designed for isolated in-memory databases where each connection has its own private database instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Behavior:</b></para>
    /// <list type="bullet">
    ///   <item><description>One connection opened at DatabaseContext creation, stays open until disposal</description></item>
    ///   <item><description>ALL operations (reads, writes, transactions) use this single connection</description></item>
    ///   <item><description>Operations are serialized through locking (one at a time)</description></item>
    ///   <item><description>No connection pooling involved</description></item>
    /// </list>
    /// <para><b>Best For:</b></para>
    /// <list type="bullet">
    ///   <item><description>SQLite :memory: databases (where each connection = separate database)</description></item>
    ///   <item><description>DuckDB :memory: databases</description></item>
    ///   <item><description>Unit testing with in-memory databases</description></item>
    ///   <item><description>Embedded databases that truly support only one connection</description></item>
    /// </list>
    /// <para><b>Operational Characteristics:</b></para>
    /// <list type="bullet">
    ///   <item><description>Operations are fully serialized (no parallel execution)</description></item>
    ///   <item><description>Minimal overhead: no connection open/close cycles</description></item>
    ///   <item><description>Guarantees consistency: all operations see same database state immediately</description></item>
    ///   <item><description>Lowest throughput due to serialization</description></item>
    /// </list>
    /// <para><b>⚠️ Warnings:</b></para>
    /// <list type="bullet">
    ///   <item><description>Client-server databases: Using this mode with SQL Server, PostgreSQL, etc. is safe but serializes ALL operations (extremely poor throughput). Use <see cref="Standard"/> mode instead.</description></item>
    ///   <item><description>High-concurrency workloads: This mode cannot execute operations in parallel. Not recommended for production APIs or high-traffic applications unless using an isolated in-memory database.</description></item>
    /// </list>
    /// </remarks>
    SingleConnection = 4,

    /// <summary>
    /// Automatically selects the optimal mode based on database type and connection string analysis.
    /// <b>Recommended:</b> Use this unless you have specific requirements for a particular mode.
    /// </summary>
    /// <remarks>
    /// <para><b>Selection Rules:</b></para>
    /// <list type="bullet">
    ///   <item><description>SQLite :memory: → <see cref="SingleConnection"/> (isolated database per connection)</description></item>
    ///   <item><description>SQLite file or "mode=memory;cache=shared" → <see cref="SingleWriter"/> (single-writer database)</description></item>
    ///   <item><description>DuckDB :memory: → <see cref="SingleConnection"/></description></item>
    ///   <item><description>DuckDB file → <see cref="SingleWriter"/></description></item>
    ///   <item><description>SQL Server LocalDB → <see cref="KeepAlive"/> (prevents unload)</description></item>
    ///   <item><description>All other databases → <see cref="Standard"/> (client-server databases)</description></item>
    /// </list>
    /// <para><b>Benefits:</b></para>
    /// <list type="bullet">
    ///   <item><description>Eliminates mode/database mismatch warnings</description></item>
    ///   <item><description>Optimal performance for each database type</description></item>
    ///   <item><description>Handles correctness constraints (e.g., :memory: requiring SingleConnection)</description></item>
    ///   <item><description>Adapts automatically if you change database providers</description></item>
    /// </list>
    /// </remarks>
    Best = 15
}