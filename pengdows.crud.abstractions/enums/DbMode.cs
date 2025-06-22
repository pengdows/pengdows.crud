namespace pengdows.crud.enums;

/// <summary>
/// Specifies how connections should be managed within the DatabaseContext.
/// Only `Standard` is recommended for production. Other modes are for dev, test, or special use cases.
/// </summary>
public enum DbMode
{
    /// <summary>
    /// The only production-supported mode.
    /// Uses connection pooling. A new connection is opened for each statement unless a transaction is in use.
    /// Fully supports high concurrency and parallelism.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// Keeps a connection open to prevent unload of user-mode databases like SQL Server Express LocalDB.
    /// Behaves otherwise like <see cref="Standard" />.
    /// Use only in development or small applications with embedded databases.
    /// Not suitable for high-traffic environments.
    /// </summary>
    KeepAlive = 1,

    /// <summary>
    /// For file-based databases like Access or Firebird Embedded that allow only one writer.
    /// Keeps one write connection open at all times. Allows multiple concurrent read connections.
    /// Not intended for production use.
    /// </summary>
    SingleWriter = 2,

    /// <summary>
    /// For embedded or legacy databases that can only handle a single connection.
    /// Not suitable for production systems or multithreaded apps.
    /// </summary>
    SingleConnection = 4
}