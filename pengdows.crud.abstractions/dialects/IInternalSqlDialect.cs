using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

internal interface IInternalSqlDialect : ISqlDialect
{
    /// <summary>
    /// True when the dialect's MERGE has no "WHEN MATCHED AND condition" form (Oracle), so a
    /// matched-row condition such as an optimistic-concurrency version check must be written as
    /// a WHERE on the UPDATE branch instead.
    /// </summary>
    bool MergeMatchedConditionAsUpdateWhere => false;

    /// <summary>
    /// False when the dialect's MERGE has no conditional matched clause in any form (neither
    /// "WHEN MATCHED AND condition" nor a WHERE on the UPDATE branch; e.g. Informix), so an
    /// optimistic-concurrency version check cannot be expressed in a MERGE upsert at all.
    /// </summary>
    bool SupportsMergeMatchedCondition => true;

    /// <summary>
    /// False when the dialect's MERGE support is not ANSI <c>MERGE ... WHEN MATCHED</c> but
    /// Firebird's <c>UPDATE OR INSERT ... MATCHING</c>, which the gateways render separately.
    /// </summary>
    bool EmitsAnsiMergeSyntax => true;

    /// <summary>
    /// True when the dialect's upsert statement needs no SET clause, so an entity whose only
    /// columns are its key columns can be upserted (Firebird's <c>UPDATE OR INSERT</c>). Every
    /// other upsert form needs at least one non-key column to update.
    /// </summary>
    bool SupportsPureKeyUpsert => false;

    /// <summary>
    /// True when the generated id comes back through an ADO.NET output parameter
    /// (Oracle's <c>RETURNING ... INTO</c>) rather than a result set.
    /// </summary>
    bool RequiresOutputParameterForReturning => false;

    /// <summary>
    /// Splits an insert-returning clause that renders before VALUES
    /// (<see cref="ISqlDialect.InsertReturningClauseBeforeValues"/>) into prefix/output/returning
    /// pieces. Default: the whole clause before VALUES. SQL Server overrides it to capture the id
    /// INTO a table variable, since a plain <c>OUTPUT INSERTED.col</c> breaks on a table with an
    /// enabled trigger.
    /// </summary>
    (string Prefix, string Output, string Returning) RenderOutputInsertClauses(string idWrapped, string clause)
        => (string.Empty, clause, string.Empty);

    /// <summary>
    /// True when a MERGE upsert whose version-guarded matched branch skips a stale row reports that
    /// row as 0 rows affected, so the gateways can raise ConcurrencyConflictException. False where
    /// rows affected can't reveal the skip: Firebird's UPDATE OR INSERT carries no guard, and Sybase
    /// ASE's @@rowcount counts the matched row even when "WHEN MATCHED AND ..." is false.
    /// </summary>
    bool MergeUpsertReportsSkippedVersionRow => true;

    /// <summary>
    /// False when a zero-length binary value does not read back as zero-length: Sybase ASE stores
    /// it as the single byte 0x00 (as it stores '' as a single blank).
    /// </summary>
    bool PreservesEmptyBinary => true;

    /// <summary>
    /// False when text outside the Unicode Basic Multilingual Plane (a UTF-16 surrogate pair, e.g.
    /// an emoji) cannot be stored even in a Unicode database: Informix through Informix.Net.Core
    /// rejects it ("An illegal character has been found in the statement").
    /// </summary>
    bool SupportsSupplementaryCharacters => true;

    /// <summary>
    /// False when a quoted identifier may not contain a space (Spanner: "table name not valid").
    /// </summary>
    bool SupportsSpacesInIdentifiers => true;

    /// <summary>
    /// False when INSERT ... ON CONFLICT can only target the primary key, not a secondary unique
    /// index (Spanner: "UNIMPLEMENTED"), so an upsert keyed on a [PrimaryKey] business key that is
    /// not the table's primary key fails there.
    /// </summary>
    bool SupportsOnConflictOnSecondaryUniqueKey => true;

    /// <summary>
    /// True when an unqualified column reference inside an expression (a SELECT list, a WHERE
    /// predicate, the right-hand side of SET) can resolve to something other than the column - on
    /// Informix a quoted "user", "today", "current", ... resolves to the special register - so the
    /// gateways must table-qualify every such reference. Column lists and SET targets are unaffected.
    /// </summary>
    bool QualifiesColumnReferences => false;

    /// <summary>
    /// Renders provider-specific JSON casts for parameter placeholders.
    /// </summary>
    string RenderJsonArgument(string parameterMarker, IColumnInfo column);

    /// <summary>
    /// Stamps provider-specific metadata on JSON parameters.
    /// </summary>
    void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column);

    /// <summary>
    /// Stamps provider metadata a column's parameter needs on every gateway write path: a JSON
    /// column's (<see cref="TryMarkJsonParameter"/>), and whatever else the dialect requires (the
    /// PostgreSQL family sends a C# enum's parameter untyped, TYPE-002).
    /// </summary>
    void MarkColumnParameter(DbParameter parameter, IColumnInfo column)
    {
        if (column.IsJsonType)
        {
            TryMarkJsonParameter(parameter, column);
        }
    }

    /// <summary>
    /// True when a column's value is written as an expression around its parameter placeholder
    /// (<see cref="RenderColumnArgument"/>) rather than the bare placeholder: a JSON column's cast,
    /// or a value the database must build itself (SQL Server geometry/geography).
    /// </summary>
    bool RendersColumnArgument(IColumnInfo column) => column.IsJsonType;

    /// <summary>
    /// Renders the value expression a column is written with, given its parameter placeholder.
    /// Default: <see cref="RenderJsonArgument"/> for a JSON column, otherwise the placeholder.
    /// </summary>
    string RenderColumnArgument(string parameterMarker, IColumnInfo column) =>
        column.IsJsonType ? RenderJsonArgument(parameterMarker, column) : parameterMarker;

    /// <summary>
    /// Builds the MERGE source clause (USING ...) for MERGE-based upserts.
    /// </summary>
    string RenderMergeSource(IReadOnlyList<IColumnInfo> columns, IReadOnlyList<string> parameterNames)
    {
        if (columns == null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        if (parameterNames == null)
        {
            throw new ArgumentNullException(nameof(parameterNames));
        }

        if (columns.Count != parameterNames.Count)
        {
            throw new ArgumentException("Column and parameter counts must match.");
        }

        var values = new string[columns.Count];
        var names = new string[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            var placeholder = MakeParameterName(parameterNames[i]);
            if (RendersColumnArgument(columns[i]))
            {
                placeholder = RenderColumnArgument(placeholder, columns[i]);
            }

            values[i] = placeholder;
            names[i] = WrapSimpleName(columns[i].Name);
        }

        return $"USING (VALUES ({string.Join(", ", values)})) AS s ({string.Join(", ", names)})";
    }


    void ApplyConnectionSettings(IDbConnection connection, IDatabaseContext context, bool readOnly);

    bool ShouldDisablePrepareOn(Exception ex);

    void TryEnterReadOnlyTransaction(ITransactionContext transaction);

    ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default);

    void InitializeUnknownProductInfo();

    Version? ParseVersion(string versionString);

    int? GetMajorVersion(string versionString);

    string GetDatabaseVersion(ITrackedConnection connection);

    DataTable GetDataSourceInformationSchema(ITrackedConnection connection);

    bool IsReadCommittedSnapshotOn(ITrackedConnection connection);

    bool IsSnapshotIsolationOn(ITrackedConnection connection);

    Task<IDatabaseProductInfo> DetectDatabaseInfoAsync(ITrackedConnection connection);

    IDatabaseProductInfo DetectDatabaseInfo(ITrackedConnection connection);

    /// <summary>
    /// Identifies dialect instances that would generate identical cached SQL/metadata for the
    /// gateway caches keyed by this value (see <c>TableGateway._templatesByDialect</c> and
    /// siblings). Two dialect instances with equal fingerprints may safely share one cache entry;
    /// unequal fingerprints must never share one. Implementations must include every property
    /// they read during cached-template construction that can vary between instances of the same
    /// <see cref="ISqlDialect.DatabaseType"/> — at minimum the detected server version, and any
    /// other per-instance construction flag that affects generated SQL text (not raw
    /// <see cref="DbParameter"/> construction — see the fingerprint audit in
    /// docs/FUTURE_WORK.md for why parameter-baking caches aren't safely fingerprintable yet).
    /// </summary>
    string CacheFingerprint { get; }

    /// <summary>
    /// True for a real client-server RDBMS with full write concurrency (the default for every
    /// engine); false only for embedded, single-writer engines (SQLite, DuckDB, Access), the
    /// FlatFile provider, and the unrecognized-database fallback. This is the single source of truth for that distinction —
    /// callers that need it (mode-mismatch diagnostics, mode coercion) should consult this property
    /// instead of maintaining their own SupportedDatabase switch.
    /// </summary>
    bool IsClientServerDatabase => true;

    /// <summary>
    /// True only for embedded, single-writer engines (SQLite, DuckDB, Access). Deliberately narrower than
    /// <c>!IsClientServerDatabase</c>, which is also false for the unrecognized-database fallback —
    /// diagnostics that are specifically about embedded single-writer behavior (e.g. file-based
    /// lock-contention warnings) should consult this instead, so they don't fire for an unknown
    /// database that merely isn't confirmed client-server either.
    /// </summary>
    bool IsEmbeddedSingleWriterEngine => false;

    /// <summary>
    /// Classifies whether <paramref name="connectionString"/> targets an in-memory database
    /// instance for this engine, and if so, whether it is isolated (private to one connection) or
    /// shared across connections. Every engine except SQLite/DuckDB has no in-memory concept and
    /// always returns <see cref="InMemoryKind.None"/>. Single source of truth — callers should
    /// consult this instead of parsing the connection string themselves.
    /// </summary>
    InMemoryKind DetectInMemoryKind(string? connectionString) => InMemoryKind.None;

    /// <summary>
    /// Resolves the actual <see cref="DbMode"/> a connection should use, given what the caller
    /// requested (including <see cref="DbMode.Best"/>) and this engine's real mode constraints —
    /// this is the single source of truth for both "what does Best mean here" and "which explicit
    /// modes are unsafe here", so <c>DatabaseContext</c> owns no per-database coercion rules of its
    /// own. The returned reason is a human-readable string for diagnostic logging; it is only
    /// examined by the caller when the resolved mode differs from what was requested.
    /// </summary>
    /// <param name="requested">The mode the caller asked for, or <see cref="DbMode.Best"/>.</param>
    /// <param name="connectionString">The connection string, for engines whose decision depends on
    /// in-memory detection (see <see cref="DetectInMemoryKind"/>).</param>
    /// <param name="isLocalDb">True when topology detection identified this as SQL Server LocalDB.</param>
    (DbMode Mode, string Reason) CoerceConnectionMode(DbMode requested, string? connectionString, bool isLocalDb)
    {
        if (requested == DbMode.Best)
        {
            var reason = IsClientServerDatabase
                ? "Full server: Best selects Standard"
                : "Unknown provider: Best defaults to Standard";
            return (DbMode.Standard, reason);
        }

        return (requested, string.Empty);
    }

    /// <summary>
    /// Whether the dialect requires, allows, or forbids parenthesizing a nested join
    /// expression in a FROM clause (e.g. <c>A JOIN (B JOIN C ON ...) ON ...</c>).
    /// Most ANSI SQL databases treat this as optional; some (e.g. MS Access/Jet)
    /// require it once three or more tables are joined; a small few reject it outright.
    /// </summary>
    JoinParenthesization JoinParenthesization => JoinParenthesization.Optional;

    /// <summary>
    /// Gets the SQL statement to explicitly release a savepoint with the given name. Only called
    /// when <see cref="SavepointCapabilities"/> includes <see cref="pengdows.crud.enums.SavepointCapabilities.Release"/> —
    /// dialects without release support (SQL Server, Sybase, Oracle) never need to implement this.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    /// <returns>The SQL statement (e.g., "RELEASE SAVEPOINT name").</returns>
    string GetReleaseSavepointSql(string name) => $"RELEASE SAVEPOINT {WrapObjectName(name)}";

    /// <summary>
    /// Whether the provider accepts ';'-separated multiple statements sent as a single batch
    /// (e.g. building a return-value capture as "DECLARE @x INT; EXEC @x = proc; SELECT @x;").
    /// Default true. Sybase ASE: false — rejects a ';' between statements in a batch entirely
    /// ("Incorrect syntax near ';'"), so multi-statement batches must be newline-separated with
    /// no terminator at all instead.
    /// </summary>
    bool SupportsSemicolonStatementSeparator => true;

    /// <summary>
    /// True when <see cref="AppendPaging"/> can page a query on this dialect, by whatever syntax it
    /// uses: <see cref="SupportsOffsetFetch"/>, <see cref="SupportsLimitOffset"/>, or a native form
    /// such as Informix's <c>SELECT SKIP n FIRST m</c>. False when the engine has no way to skip
    /// rows (e.g. Sybase ASE, where <see cref="AppendPaging"/> throws).
    /// </summary>
    bool SupportsPaging => SupportsOffsetFetch || SupportsLimitOffset;
}
