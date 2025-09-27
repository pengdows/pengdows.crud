namespace pengdows.crud.enums;

/// <summary>
/// Defines the strategy for retrieving generated primary key values after INSERT operations.
/// Order reflects preference hierarchy: inline return is best, session-scoped functions are safe,
/// correlation tokens are universal fallback, and natural key lookup requires unique constraints.
/// </summary>
public enum GeneratedKeyPlan
{
    /// <summary>
    /// No key retrieval strategy available. Database doesn't support auto-generated keys
    /// or the strategy hasn't been configured.
    /// </summary>
    None = 0,

    /// <summary>
    /// Use inline RETURNING clause (PostgreSQL, Firebird, DuckDB, SQLite 3.35+).
    /// Best option: atomic, single round-trip, race-free.
    /// Example: INSERT ... RETURNING id
    /// </summary>
    Returning = 1,

    /// <summary>
    /// Use OUTPUT INSERTED clause (SQL Server).
    /// Best option for SQL Server: atomic, single round-trip, race-free.
    /// Example: INSERT ... OUTPUT INSERTED.id
    /// </summary>
    OutputInserted = 2,

    /// <summary>
    /// Use session-scoped last insert ID functions (MySQL, MariaDB, SQLite &lt;3.35, SQL Server fallback).
    /// Safe when used on the same connection immediately after INSERT.
    /// Examples: LAST_INSERT_ID(), last_insert_rowid(), SCOPE_IDENTITY()
    /// </summary>
    SessionScopedFunction = 3,

    /// <summary>
    /// Pre-fetch the ID from a sequence before INSERT (Oracle preferred approach).
    /// Excellent option: you know the ID before inserting, no lookup needed.
    /// Example: SELECT seq.NEXTVAL → INSERT with known ID
    /// </summary>
    PrefetchSequence = 4,

    /// <summary>
    /// Use correlation token: add a unique token to the INSERT, then SELECT by token.
    /// Universal fallback that works on any database with proper uniqueness.
    /// Safe, robust, but requires two round-trips.
    /// </summary>
    CorrelationToken = 5,

    /// <summary>
    /// Look up by natural key values within a transaction (last resort).
    /// Only safe with unique constraints on the lookup columns.
    /// Requires explicit opt-in due to potential race conditions.
    /// </summary>
    NaturalKeyLookup = 6
}