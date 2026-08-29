// =============================================================================
// FILE: ConnectionStrategyFactory.cs
// PURPOSE: Factory for creating appropriate connection strategies based on DbMode.
//
// AI SUMMARY:
// - Creates IConnectionStrategy implementations based on DbMode:
//   * Standard - Ephemeral connections with provider pooling (default)
//   * PreventDatabaseUnload - Standard + sentinel connection(s) to prevent unload
//   * SingleWriter - Standard lifecycle with governor-enforced single writer (file SQLite)
//   * SingleConnection - All work on one connection (:memory: SQLite)
// - Database-to-strategy mapping based on connection string/provider:
//   * SQL Server/PostgreSQL/MySQL -> Standard
//   * LocalDB -> PreventDatabaseUnload
//   * SQLite file -> SingleWriter
//   * SQLite :memory: -> SingleConnection
// =============================================================================

using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
/// 2. SingleWriter - StandardConnectionStrategy + governor (write slot = 1)
///    └─ Databases with single-writer file-based limitations (SQLite, DuckDB)
///
/// 3. PreventDatabaseUnload - Standard + one unused sentinel per materially separate pool
///    └─ Embedded databases that benefit from staying loaded
///
/// 4. Standard - Pure ephemeral connections with provider pooling
///    └─ Production databases with proper connection pooling
///
/// DATABASE-TO-STRATEGY MAPPING:
/// - SQLite/DuckDB isolated :memory: → SingleConnection (each connection owns its own database)
/// - SQLite/DuckDB shared in-memory (Mode=Memory;Cache=Shared) → SingleWriter (governed writer)
/// - File SQLite → SingleWriter (write-serialized via governor)
/// - LocalDB → PreventDatabaseUnload (prevent shutdown between operations)
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
            DbMode.PreventDatabaseUnload => new PreventDatabaseUnloadConnectionStrategy(context),
            DbMode.SingleConnection => new SingleConnectionStrategy(context),
            // SingleWriter now uses Standard lifecycle with governor policy (WriteSlots=1 + turnstile)
            // This provides: per-operation connections, connection recovery, writer starvation prevention
            DbMode.SingleWriter => new StandardConnectionStrategy(context),
            _ => throw new NotSupportedException($"Unsupported database mode: {mode}")
        };
    }
}
