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
    /// Use inline RETURNING clause (e.g. PostgreSQL, Oracle, Firebird, DuckDB, SQLite 3.35+; Db2 uses
    /// the equivalent <c>SELECT ... FROM FINAL TABLE (INSERT ...)</c>).
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
    /// Use a session-scoped last insert ID function in a follow-up query. Selected by the default
    /// plan logic for a dialect with no inline RETURNING/OUTPUT support but a session-scoped
    /// function; the built-in MySQL, MariaDB, SQLite, and Sybase ASE dialects select
    /// <see cref="CompoundStatement"/> or <see cref="ReaderInsertedId"/> instead.
    /// Safe only when used on the same connection immediately after INSERT.
    /// Examples: LAST_INSERT_ID(), last_insert_rowid(), SCOPE_IDENTITY()
    /// </summary>
    SessionScopedFunction = 3,

    /// <summary>
    /// Pre-fetch the ID from a sequence/generator before INSERT (used by InterBase).
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
    NaturalKeyLookup = 6,

    /// <summary>
    /// Execute INSERT and session-scoped ID function as a single compound statement.
    /// Example: INSERT ... ; SELECT LAST_INSERT_ID()
    /// Requires multi-statement support enabled in the provider connection string.
    /// Avoids the two-lease correctness hazard of SessionScopedFunction, where the connection
    /// pool may assign a different physical connection for the follow-up scalar query.
    /// Used by MySQL (MySql.Data provider), SQLite before 3.35, and Sybase ASE.
    /// </summary>
    CompoundStatement = 7,

    /// <summary>
    /// Execute INSERT as a reader and read the generated key from the provider-specific
    /// DbDataReader property (e.g. MySqlDataReader.LastInsertedId) populated from the
    /// database OK packet. No second round-trip and no multi-statement support required.
    /// Used by MariaDB and by MySQL on MySqlConnector, which deliberately does not support
    /// AllowMultipleStatements.
    /// </summary>
    ReaderInsertedId = 8
}