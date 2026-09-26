// =============================================================================
// FILE: FlatFileDialect.cs
// PURPOSE: Dialect for pengdows.flatfile — a file-backed ADO.NET provider over
//          CSV/TSV/pipe/delimited/fixed-width/NDJSON files.
//
// STATUS: Partial. Only the properties below reflect a deliberate, verified decision
// against pengdows.flatfile's actual behavior (see citations on each). Everything not
// overridden here still falls through to SqlDialect's generic defaults and has NOT been
// verified against pengdows.flatfile — in particular isolation levels/profiles (its
// FlatFileTransaction is file-snapshot/undo-journal rollback, explicitly not real
// concurrent-connection isolation or MVCC per its own README), generated-key/identity
// plan (flatfile has no autoincrement/sequence/RETURNING concept at all), session
// settings, and CoerceConnectionMode/DbMode-Best selection. Decide those from real
// research against pengdows.flatfile's source, not by copying another embedded dialect's
// assumptions — see CLAUDE.md's "Adding a New Database" checklist and its SAP HANA
// callout for the same caution.
// =============================================================================

using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

/// <summary>
/// Dialect for <c>pengdows.flatfile</c>. See the file-level STATUS remark above — this is a
/// deliberately partial dialect, not a finished one.
/// </summary>
internal class FlatFileDialect : SqlDialect
{
    internal FlatFileDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.FlatFile;

    /// <summary>
    /// Confirmed via <c>pengdows.flatfile/FlatFileConnectionStringBuilder.cs</c>'s own
    /// <c>KeyApplicationName</c> constant and <c>ApplicationName</c> property — a real, recognized
    /// keyword, not a guess. Without this set, reader/writer connection strings would be
    /// identical and collapse into one shared pool (the same bug class Access's
    /// <c>ReadOnlyPoolDiscriminatorSettingName</c> fix addressed, but avoided here entirely since
    /// FlatFile's own driver already supports the standard mechanism).
    /// </summary>
    public override string? ApplicationNameSettingName => "applicationName";

    /// <summary>
    /// pengdows.flatfile is a custom, in-process, file-based provider with no network handshake
    /// and no real connection pool to configure — architecturally identical to why
    /// <see cref="DuckDbDialect"/>'s equivalent is <see langword="false"/>.
    /// </summary>
    public override bool SupportsExternalPooling => false;

    /// <summary>
    /// Confirmed via <c>pengdows.flatfile/FlatFileConnectionStringBuilder.cs</c>'s own
    /// <c>KeyReadOnly</c> constant and <c>ReadOnly</c> property
    /// (<c>SetOrRemove(KeyReadOnly, value ? "true" : null)</c>) — a real, hard-enforced keyword:
    /// any mutating statement (DML/DDL) against a connection carrying this is rejected
    /// immediately by the provider itself, not just by pengdows.crud's own pre-flight checks.
    /// </summary>
    public override string? GetReadOnlyConnectionParameter() => "readonly=true";

    /// <summary>
    /// pengdows.flatfile binds the ISO SQL <c>:name</c> host-parameter form: <c>pengdows.sql/SqlLexer.cs</c>
    /// emits a <c>NamedParameter</c> token for <c>:</c> followed by a letter or underscore, and
    /// <c>BoundPredicateEvaluator.ResolveParameter</c> matches the bare name against
    /// <c>DbParameter.ParameterName</c> case-insensitively (so a name may be repeated). Positional
    /// <c>?</c> also works, but one statement cannot mix the two (<c>SqlBinder.BindParameter</c>),
    /// and <c>@name</c>/<c>$1</c> are rejected as vendor syntax. The old README line "positional
    /// ? only" was stale. Being a named-parameter dialect also turns off the ODBC-style common
    /// conversions (bool to Int16, Guid to string, DateTimeOffset to UTC DateTime): the provider's
    /// <c>ClrTypeMap</c> handles bool/Guid/DateTimeOffset natively, and a BOOLEAN column rejects
    /// an Int16 <c>1</c>.
    /// </summary>
    public override bool SupportsNamedParameters => true;

    /// <summary>
    /// See <see cref="SupportsNamedParameters"/>: <c>:name</c>, the same marker as Oracle.
    /// </summary>
    public override string ParameterMarker => ":";

    /// <summary>
    /// <c>pengdows.sql/SqlParser.cs</c> parses the SQL-92 <c>SAVEPOINT name</c>,
    /// <c>RELEASE SAVEPOINT name</c> and <c>ROLLBACK TO SAVEPOINT name</c> forms (regular or
    /// delimited identifier), and <c>DefaultFlatFileQueryExecutor</c> routes them to
    /// <c>FlatFileTransaction.Save/Release/Rollback(name)</c>, which undo staged DML and DDL
    /// backups since the savepoint. The base ANSI SQL text and full
    /// <see cref="SqlDialect.SavepointCapabilities"/> (Create|Rollback|Release) apply unchanged.
    /// </summary>
    public override bool SupportsSavepoints => true;

    /// <summary>
    /// <c>pengdows.sql/SqlParser.cs</c> parses the SQL:2008 <c>OFFSET n ROWS FETCH {FIRST|NEXT} n ROWS
    /// ONLY</c> tail (<c>ParseOffset</c>/<c>ParseFetchFirst</c>) and has no <c>LIMIT</c> clause: the
    /// real provider rejects <c>SELECT * FROM t LIMIT 1</c> ("Expected token 'EndOfInput' ... found
    /// 'NumericLiteral'"). <see cref="SqlDialect.AppendPaging"/> already emits OFFSET/FETCH because
    /// <see cref="SqlDialect.SupportsOffsetFetch"/> is true.
    /// </summary>
    public override bool SupportsLimitOffset => false;

    /// <summary>
    /// The base query ends in the generic <c>LIMIT 1</c>, which pengdows.flatfile rejects (see
    /// <see cref="SupportsLimitOffset"/>); use the standard <c>FETCH FIRST 1 ROWS ONLY</c>.
    /// </summary>
    public override string GetNaturalKeyLookupQuery(string tableName, string idColumnName,
        IReadOnlyList<string> columnNames, IReadOnlyList<string> parameterNames)
    {
        var query = base.GetNaturalKeyLookupQuery(tableName, idColumnName, columnNames, parameterNames);
        return query.Replace(" LIMIT 1", " FETCH FIRST 1 ROWS ONLY", StringComparison.Ordinal);
    }

    private const string SetTransactionReadOnlySql = "SET TRANSACTION READ ONLY";

    /// <summary>
    /// <c>TransactionCharacteristicsExecutor</c> applies <c>SET TRANSACTION READ ONLY</c> to the
    /// current <c>FlatFileTransaction</c>, after which <c>DefaultFlatFileQueryExecutor</c> rejects
    /// every mutating statement. The setting belongs to that one transaction (each BEGIN creates a
    /// new <c>FlatFileTransaction</c>), so no reset SQL is needed. SingleWriter reads already use
    /// <c>readonly=true</c> connections; this also covers a read-only transaction on a writer
    /// connection (SingleConnection mode).
    /// </summary>
    public override bool SupportsReadOnlyTransactions => true;

    /// <inheritdoc cref="SupportsReadOnlyTransactions"/>
    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        TryExecuteReadOnlySql(transaction, SetTransactionReadOnlySql, "FlatFile");
    }

    /// <inheritdoc cref="SupportsReadOnlyTransactions"/>
    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, SetTransactionReadOnlySql, "FlatFile", cancellationToken);
    }

    /// <summary>
    /// pengdows.flatfile has no stored-procedure/trigger/control-flow support at all (confirmed:
    /// its README lists this under "Not supported"). <see cref="ProcWrappingStyle.None"/> is the
    /// base default already, but this override documents that the value was verified, not left
    /// unexamined.
    /// </summary>
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.None;

    /// <summary>
    /// pengdows.flatfile is an embedded, per-directory file engine with no server process — the
    /// same classification as SQLite/DuckDB, not a client-server RDBMS.
    /// </summary>
    public override bool IsClientServerDatabase => false;

    /// <summary>
    /// <c>FlatFileConnection.Open</c> acquires <c>ConnectionWriteLock</c> for every non-readonly
    /// connection: one writer per directory (or per file in single-file mode). A second in-process
    /// writer waits <c>connectionTimeout</c> (default 5 s) and then throws
    /// <see cref="TimeoutException"/>; a writer in another process is refused outright. Readonly
    /// connections never take the lock. That is the SQLite/DuckDB single-writer constraint.
    /// </summary>
    public override bool IsEmbeddedSingleWriterEngine => true;

    /// <summary>
    /// Same policy as SQLite/DuckDB (<see cref="SqlDialect.CoerceEmbeddedSingleWriterMode"/>): Best,
    /// Standard and PreventDatabaseUnload become SingleWriter, so writes are serialized by the
    /// governor and reads use <c>readonly=true</c> connections that skip the write lock. Standard is
    /// not honored because concurrent writers queue inside <c>Open()</c> and time out, and every
    /// Standard read would also take the write lock. A PreventDatabaseUnload sentinel would hold the
    /// write lock for the context's lifetime. pengdows.flatfile has no in-memory mode (every
    /// connection names a <c>path</c> or <c>file</c>), so <see cref="SqlDialect.DetectInMemoryKind"/>
    /// keeps its <see cref="InMemoryKind.None"/> default.
    /// </summary>
    public override (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString,
        bool isLocalDb) =>
        CoerceEmbeddedSingleWriterMode(requested, DetectInMemoryKind(connectionString));

    /// <summary>
    /// Verified: pengdows.flatfile's SQL parser (<c>pengdows.sql/SqlParser.cs</c>) genuinely
    /// parses an <c>IF EXISTS</c> clause on <c>DROP TABLE</c>/<c>DROP VIEW</c>/<c>DROP INDEX</c>
    /// (see its <c>SqlAst.cs</c> <c>IfExists</c> properties), so the base <c>true</c> default is
    /// confirmed correct here rather than inherited blind.
    /// </summary>
    public override bool SupportsDropTableIfExists => true;

    /// <summary>
    /// <c>SELECT version()</c> — the base <see cref="SqlDialect.GetDatabaseVersionAsync"/> fallback
    /// when a dialect doesn't override <see cref="GetVersionQuery"/> — is not a SQL-standard
    /// function; it happens to work for Postgres-family and DuckDB engines because they each
    /// implement it as a real builtin (see <see cref="DuckDbDialect.GetVersionQuery"/>), not because
    /// any standard guarantees it. pengdows.flatfile's SQL grammar has no <c>version()</c> function
    /// at all, so that guess would simply fail against a real flatfile connection. Its ADO.NET
    /// provider instead exposes version the idiomatic ADO.NET way — <c>FlatFileConnection.ServerVersion</c>
    /// (currently hardcoded to <c>"1.0"</c>) — so read that directly instead of executing SQL.
    /// </summary>
    public override Task<string> GetDatabaseVersionAsync(ITrackedConnection connection)
    {
        return Task.FromResult(connection.ServerVersion);
    }

    /// <summary>
    /// pengdows.flatfile's <c>FlatFileException.SqlState</c> deliberately uses the real ANSI SQL
    /// class-23 codes a server-based database would report (see pengdows.flatfile's own CLAUDE.md
    /// "Constraint Violations" section and <c>FlatFileException.cs</c>'s file-level remarks) —
    /// exactly so a consumer like this dialect can classify it via SqlState alone, the same pattern
    /// PostgreSqlDialect already uses for its own identical code set. The base
    /// SqlDialect.IsXxxViolation overrides only match message text ("foreign key", "not null",
    /// etc.), which FlatFileException's actual messages don't contain, so without these overrides
    /// every FlatFile constraint violation fell through to a generic, unclassified exception.
    /// </summary>
    public override bool IsUniqueViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23505", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsForeignKeyViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23503", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsNotNullViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23502", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc cref="IsUniqueViolation"/>
    public override bool IsCheckConstraintViolation(DbException ex) =>
        string.Equals(TryGetProviderSqlState(ex), "23514", StringComparison.OrdinalIgnoreCase);
}
