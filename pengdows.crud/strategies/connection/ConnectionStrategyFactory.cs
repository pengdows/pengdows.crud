using pengdows.crud.enums;

namespace pengdows.crud.strategies.connection;

/// <summary>
/// CONNECTION STRATEGY FACTORY - ARCHITECTURAL OVERVIEW:
///
/// This factory creates the appropriate connection management strategy based on database characteristics
/// and user requirements. Each strategy encapsulates a complete connection lifecycle policy.
///
/// STRATEGY SELECTION HIERARCHY (from most restrictive to most scalable):
///
/// 1. SingleConnection - ONE connection for everything
///    └─ In-memory databases where connection loss = data loss
///
/// 2. SingleWriter - ONE persistent writer + ephemeral readers
///    └─ Databases with single-writer limitations (SQLCE, file-based)
///
/// 3. KeepAlive - Standard + one unused sentinel connection
///    └─ Embedded databases that benefit from staying loaded
///
/// 4. Standard - Pure ephemeral connections with provider pooling
///    └─ Production databases with proper connection pooling
///
/// DATABASE-TO-STRATEGY MAPPING:
/// - :memory: SQLite → SingleConnection (data loss if connection closes)
/// - File SQLite → SingleWriter (single writer performance)
/// - LocalDB → KeepAlive (prevent shutdown between operations)
/// - SQL Server/PostgreSQL/MySQL → Standard (connection pooling)
///
/// FUTURE ARCHITECTURAL DIRECTION:
/// Each strategy should eventually handle its own dialect detection and initialization,
/// removing this logic from DatabaseContext constructor.
///
/// DO NOT MODIFY: This factory determines connection behavior for all database operations
/// </summary>
internal static class ConnectionStrategyFactory
{
    public static IConnectionStrategy Create(DatabaseContext context, DbMode mode)
    {
        return mode switch
        {
            DbMode.Standard => new StandardConnectionStrategy(context),
            DbMode.KeepAlive => new KeepAliveConnectionStrategy(context),
            DbMode.SingleConnection => new SingleConnectionStrategy(context),
            DbMode.SingleWriter => new SingleWriterConnectionStrategy(context),
            _ => new StandardConnectionStrategy(context)
        };
    }
}
