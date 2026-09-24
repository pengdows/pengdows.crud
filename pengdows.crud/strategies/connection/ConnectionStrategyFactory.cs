// =============================================================================
// FILE: ConnectionStrategyFactory.cs
// PURPOSE: Factory for creating appropriate connection strategies based on DbMode.
//
// AI SUMMARY:
// - Creates IConnectionStrategy implementations based on DbMode:
//   * Standard - Ephemeral connections with provider pooling (default)
//   * PreventDatabaseUnload - Standard + sentinel connection to prevent unload
//   * SingleWriter - Standard lifecycle with governor-enforced single writer (file SQLite/DuckDB/Access)
//   * SingleConnection - All work on one connection (isolated :memory: SQLite/DuckDB)
// - The mode itself is resolved beforehand by ISqlDialect.CoerceConnectionMode (DbMode.Best):
//   * Client-server databases -> Standard
//   * SQL Server LocalDB -> PreventDatabaseUnload
//   * SQLite/DuckDB/Access file (and shared in-memory) -> SingleWriter
//   * Isolated SQLite/DuckDB :memory: -> SingleConnection
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
///    └─ Databases with single-writer file-based limitations (SQLite, DuckDB, Access)
///
/// 3. PreventDatabaseUnload - Standard + one unused sentinel connection
///    └─ SQL Server LocalDB, which otherwise unloads when its last connection closes
///
/// 4. Standard - Pure ephemeral connections with provider pooling
///    └─ Production databases with proper connection pooling
///
/// DATABASE-TO-STRATEGY MAPPING:
/// - SQLite/DuckDB isolated :memory: → SingleConnection (each connection owns its own database)
/// - SQLite/DuckDB shared in-memory (Mode=Memory;Cache=Shared) → SingleWriter (governed writer)
/// - File SQLite/DuckDB/Access → SingleWriter (write-serialized via governor)
/// - LocalDB → PreventDatabaseUnload (prevent shutdown between operations)
/// - Client-server databases → Standard (connection pooling)
/// (This factory only maps an already-resolved DbMode; the mapping above is applied by
/// ISqlDialect.CoerceConnectionMode.)
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