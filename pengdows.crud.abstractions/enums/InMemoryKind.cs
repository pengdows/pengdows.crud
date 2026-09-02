namespace pengdows.crud.enums;

/// <summary>
/// Classifies whether a connection string targets an in-memory database instance, and if so,
/// whether that instance is private to one connection or shared across connections in the process.
/// </summary>
public enum InMemoryKind
{
    /// <summary>Not an in-memory database (a real file or a client-server connection).</summary>
    None,

    /// <summary>
    /// An in-memory database private to a single connection (e.g. SQLite/DuckDB <c>:memory:</c>
    /// with no shared-cache option). A new connection to the same string creates a separate,
    /// empty database, so only <see cref="DbMode.SingleConnection"/> can see a consistent view.
    /// </summary>
    Isolated,

    /// <summary>
    /// An in-memory database shared across connections in the same process (e.g. SQLite's
    /// <c>mode=memory;cache=shared</c>), so it behaves like a single-writer file database rather
    /// than requiring one pinned connection.
    /// </summary>
    Shared
}
