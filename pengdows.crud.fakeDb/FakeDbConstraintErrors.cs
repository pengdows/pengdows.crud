using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.fakeDb;

/// <summary>
/// The unique-key violation and syntax error each emulated product's provider raises: the SQLSTATE, vendor error number
/// and message the library's dialects classify on. Provider-shaped, so internal.
/// </summary>
internal static class FakeDbConstraintErrors
{
    internal static DbException UniqueViolation(SupportedDatabase product, string table, string constraint)
    {
        var (message, number, sqlState) = product switch
        {
            SupportedDatabase.SqlServer => ($"Violation of PRIMARY KEY constraint '{constraint}'. Cannot insert duplicate key in object '{table}'.", 2627, null),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb or SupportedDatabase.AuroraMySql
                or SupportedDatabase.SingleStore => ($"Duplicate entry for key '{constraint}'", 1062, "23000"),
            SupportedDatabase.Oracle => ($"ORA-00001: unique constraint ({constraint}) violated", 1, null),
            SupportedDatabase.Sqlite => ($"UNIQUE constraint failed: {table}", 2067, null),
            SupportedDatabase.DuckDB => ($"Constraint Error: Duplicate key violates primary key constraint '{constraint}'", 0, null),
            SupportedDatabase.Firebird => ($"violation of PRIMARY or UNIQUE KEY constraint \"{constraint}\" on table \"{table}\"", 335544665, null),
            SupportedDatabase.InterBase => ($"violation of PRIMARY or UNIQUE KEY constraint \"{constraint}\" on table \"{table}\"", 335544665, null),
            SupportedDatabase.Db2 => ($"SQL0803N One or more values in the INSERT statement violate a unique constraint: {constraint}", -803, "23505"),
            SupportedDatabase.Informix => ("Unique constraint violated.", -268, "23000"),
            SupportedDatabase.SapHana => ($"unique constraint violated: {constraint}", 301, null),
            SupportedDatabase.SybaseASE => ($"Attempt to insert duplicate key row in object '{table}' with unique index '{constraint}'", 2601, null),
            SupportedDatabase.Snowflake => ($"Duplicate key value violates unique constraint \"{constraint}\"", 0, null),
            SupportedDatabase.Access => ("The changes you requested to the table were not successful because they would create duplicate values in the index, primary key, or relationship.", 0, null),
            _ => ($"duplicate key value violates unique constraint \"{constraint}\"", 0, "23505")
        };

        return new ProviderShapedException(message, number, sqlState, product == SupportedDatabase.Sqlite ? 2067 : 0);
    }

    /// <summary>
    /// The read-only-refusal error the emulated product's provider raises for a write attempted on
    /// a connection fakeDb detected as read-only (fakeDbConnection.IsReadOnly,
    /// FakeDbReadOnlyEnforcement). Codes from docs/read-only-enforcement.md's
    /// ReadOnlyViolationException table (checked live 2026-10-03) -- the same codes pengdows.crud's
    /// own dialect translators classify into ReadOnlyViolationException, so this exercises that
    /// classification path end to end instead of a generic exception a translator would ignore.
    /// </summary>
    internal static DbException ReadOnlyViolation(SupportedDatabase product, string verb = "WRITE")
    {
        var (message, number, sqlState) = product switch
        {
            SupportedDatabase.Sqlite => ("attempt to write a readonly database", 8, null),
            SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb
                or SupportedDatabase.AuroraPostgreSql
                => ($"cannot execute {verb} in a read-only transaction", 0, "25006"),
            SupportedDatabase.DuckDB => ($"Cannot execute statement of type \"{verb}\" on database \"main\" which is attached in read-only mode!", 0, null),
            // MySqlDialect.TryClassifyProviderException checks error code 1792 (shared by
            // MariaDb/TiDb/AuroraMySql, which inherit MySQL's error-number space).
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
                or SupportedDatabase.AuroraMySql =>
                ("Cannot execute statement in a READ ONLY transaction.", 1792, null),
            // OracleDialect.TryClassifyProviderException checks error code 1456 (ORA-01456).
            SupportedDatabase.Oracle => ("ORA-01456: may not perform insert/delete/update operation inside a READ ONLY transaction", 1456, null),
            // InformixDialect.TryClassifyProviderException checks error code -878 exclusively.
            SupportedDatabase.Informix => ("Invalid operation for a READ-ONLY transaction.", -878, null),
            // HanaDialect.TryClassifyProviderException checks error code 129 AND that the message
            // contains "read-only" (129 alone is HANA's generic internal-error code, reused for
            // unrelated failures too).
            SupportedDatabase.SapHana =>
                ($"cannot change this transaction's access mode from read-only to update directly ({verb})", 129, null),
            // FirebirdDialect.TryClassifyProviderException CAN classify error code 335544361
            // (isc_read_only_trans) into ReadOnlyViolationException, but live verification
            // (docs/read-only-enforcement.md, 2026-10-03) found a read-only transaction's session
            // SQL does not actually make Firebird raise it -- FakeDbReadOnlyEnforcement.
            // ProductEnforcesSessionReadOnly returns false for Firebird, so this branch is never
            // reached through that mechanism. Kept for a test that wants to simulate the error
            // shape directly (e.g. a genuinely read-only-opened database file), not as a claim
            // that BeginTransaction(ExecutionType.Read) triggers it.
            SupportedDatabase.Firebird =>
                ("attempted update during read-only transaction", 335544361, null),
            // AccessDialect.TryClassifyProviderException checks this exact substring, not a
            // SqlState or error code -- the message is the only signal ACE gives.
            SupportedDatabase.Access => ("Operation must use an updateable query.", 0, null),
            // FlatFileDialect.TryClassifyProviderException checks SqlState "25006" exclusively
            // (the FlatFile provider's own read-only SQLSTATE), not message text.
            SupportedDatabase.FlatFile => ($"cannot execute {verb} on a read-only connection", 0, "25006"),
            _ => ("attempt to write on a read-only connection", 0, null)
        };

        return new ProviderShapedException(message, number, sqlState, product == SupportedDatabase.Sqlite ? 8 : 0);
    }

    /// <summary>
    /// The syntax error the emulated product's provider raises for text that is not a statement (FakeDataStore.StrictMode).
    /// </summary>
    internal static DbException SyntaxError(SupportedDatabase product, string token)
    {
        var (message, number, sqlState) = product switch
        {
            SupportedDatabase.Sqlite => ($"SQLite Error 1: 'near \"{token}\": syntax error'.", 1, null),
            SupportedDatabase.SqlServer => ($"Incorrect syntax near '{token}'.", 102, null),
            SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb or SupportedDatabase.AuroraMySql
                or SupportedDatabase.SingleStore => ($"You have an error in your SQL syntax; check the manual that corresponds to your server version for the right syntax to use near '{token}' at line 1", 1064, "42000"),
            SupportedDatabase.Oracle => ("ORA-00900: invalid SQL statement", 900, null),
            _ => ($"syntax error at or near \"{token}\"", 0, "42601")
        };

        return new ProviderShapedException(message, number, sqlState, product == SupportedDatabase.Sqlite ? 1 : 0);
    }

    /// <summary>A driver's own <see cref="DbException"/> with the message it raises (for a <see cref="fakeDbDriverQuirks"/> preset).</summary>
    internal static DbException ProviderError(string message)
    {
        return new ProviderShapedException(message, 0, null, 0);
    }

    private sealed class ProviderShapedException : DbException
    {
        private readonly string? _sqlState;

        internal ProviderShapedException(string message, int number, string? sqlState, int errorCode)
            : base(message, errorCode)
        {
            Number = number;
            _sqlState = sqlState;
        }

        public int Number { get; }

        public override string? SqlState => _sqlState;
    }
}
