// =============================================================================
// FILE: HanaDialect.cs
// PURPOSE: SAP HANA (column-store, in-memory RDBMS) dialect implementation.
//
// AI SUMMARY:
// - Backported from pengdows.crud 3.0 (same live-verification trail applies) to this
//   non-breaking 2.0.6 patch line. Isolation-level data (GetSupportedIsolationLevels/
//   GetIsolationProfileMapping on 3.0) lives in IsolationResolver.cs's central switch instead,
//   and advisory exception-category classification lives in SqlDialect.cs's own private
//   TryClassifyProviderException switch, since 2.0.6 predates 3.0's dialect-owned refactors for
//   both.
// - LIVE-VERIFIED end-to-end against a real saplabs/hanaexpress 2.00.088.00.1760424921
//   container (HXE) run by hand via Docker, using both the bundled hdbsql client and the real
//   Sap.Data.Hana.Net.v8.0 2.29.27 ADO.NET driver: identifier case folding, LIMIT/OFFSET paging,
//   MERGE INTO, IDENTITY + CURRENT_IDENTITY_VALUE(), stored procedure CALL syntax, savepoints,
//   isolation level acceptance, and live constraint-violation exception shapes all confirmed.
// - Driver: Sap.Data.Hana.Net.v8.0 (SAP-published, public NuGet.org, SAP Developer License
//   Agreement 3.2). Factory: Sap.Data.Hana.HanaFactory.Instance. GetSchema("DataSourceInformation")
//   reports DataSourceProductName = "HANA", ParameterMarkerFormat = "?".
// - Docker/CI: NOT wired into the always-on testbed matrix. saplabs/hanaexpress documents
//   16-32GB RAM for a working container — opt-in for resource reasons (INCLUDE_SAPHANA=true).
// - Parameters: positional "?" only, no named-parameter support — matches Informix's shape.
// - Identifier quoting: ANSI double quotes. CONFIRMED live: an unquoted identifier folds to
//   uppercase; a quoted one preserves exact case. Same behavior as Oracle.
// - Pagination: LIMIT n OFFSET m, CONFIRMED live. The SQL:2008 "OFFSET n ROWS FETCH NEXT m ROWS
//   ONLY" form is CONFIRMED REJECTED — do not assume Db2/SQL-Server-style paging.
// - MERGE: MERGE INTO ... WHEN MATCHED/WHEN NOT MATCHED is supported, CONFIRMED live for both
//   branches, but the base RenderMergeSource's "USING (VALUES (...)) AS s (...)" row-constructor
//   shape is CONFIRMED REJECTED. Overridden here with an Oracle/DUAL-shaped
//   "USING (SELECT ... FROM DUMMY) s" source, which IS accepted.
// - Batch insert: CONFIRMED live that the ANSI multi-row VALUES clause is rejected — same
//   limitation as Firebird/InterBase/Informix/Access/Sybase ASE. Falls back to one INSERT per entity.
// - Generated keys: IDENTITY column plus "SELECT CURRENT_IDENTITY_VALUE() FROM DUMMY"
//   immediately after INSERT on the same connection, CONFIRMED live. Deliberately NOT wired up
//   as GeneratedKeyPlan.SessionScopedFunction (a session-scoped last-insert-id query is not
//   guaranteed to land on the same pooled physical connection that ran the INSERT — the exact
//   two-lease hazard CompoundStatement/ReaderInsertedId/Returning exist to avoid).
//   CompoundStatement was also CONFIRMED REJECTED live (HANA does not support multiple
//   semicolon-separated statements in a single command). With SupportsInsertReturning left at
//   its default false and HasSessionScopedLastIdFunction left at its default false, the base
//   GetGeneratedKeyPlan() already resolves to the safe universal fallback,
//   GeneratedKeyPlan.CorrelationToken — no override needed here.
// - Procedures: CALL proc(args) via SQLSCRIPT, CONFIRMED live including an OUT parameter.
// - Savepoints: SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT all CONFIRMED live inside
//   a real transaction (unlike Oracle, which has no RELEASE SAVEPOINT at all).
// - DROP TABLE IF EXISTS: CONFIRMED REJECTED, same limitation as Oracle.
// - Isolation levels: HanaConnection.BeginTransaction accepted ReadUncommitted, ReadCommitted,
//   RepeatableRead, and Serializable without error (Snapshot was correctly rejected by the
//   driver itself) — CONFIRMED live. Whether ReadUncommitted delivers genuine dirty reads on the
//   server, versus a silent upgrade to ReadCommitted, was NOT verified.
// - Exception classification: CONFIRMED live via a real HanaException that
//   HanaException.ErrorCode is *always* the generic COM HRESULT -2147467259 regardless of
//   violation kind, and HanaException.SqlState is an *empty string* for every violation kind
//   except unique (which the driver leaves at "23000") — neither is usable for classification
//   via the .NET driver. The only reliable discriminator is HanaException.NativeError, which the
//   framework's shared error-code extraction already finds via reflection (probes
//   "Number" -> "SqliteErrorCode" -> "NativeError" in that order). Real codes captured live: 301
//   (unique), 461 (FK on insert/update, parent missing), 462 (FK on delete/update, dependent
//   child still exists), 287 (NOT NULL), 677 (CHECK). Deadlock (133) and lock-wait-timeout (131)
//   are sourced from SAP's own HANA Lock Analysis FAQ (KBA 1999998), not live-reproduced
//   (requires two contending sessions).
// - GUIDs: HanaDbType has no native UUID-style member — confirmed by enumerating the real enum.
//   Stored as a client-generated hyphenated string.
// - Pool discriminator: no ApplicationName-equivalent keyword exists on the real
//   Sap.Data.Hana.HanaConnectionStringBuilder (72 properties inspected via reflection).
//   "ConnectionTimeout=15" was chosen and CONFIRMED LIVE — 15 is this property's own compiled-in
//   default, so setting it explicitly is guaranteed behaviorally inert by construction.
// - Read-only transactions: "SET TRANSACTION READ ONLY" genuinely works — CONFIRMED LIVE that a
//   write attempted afterward fails with NativeError 129, while a read inside the same
//   read-only transaction succeeds normally.
//   CRITICAL companion finding: the read-only flag is STICKY at the session level — it persists
//   past COMMIT and affects the NEXT transaction on the same physical connection. CONFIRMED live
//   by direct reproduction. Fixed via GetBaseSessionSettings() returning
//   "SET TRANSACTION READ WRITE" as a per-checkout reset — CONFIRMED LIVE both that this
//   statement is safe to run as a bare preamble with no active transaction and that running it
//   after a stuck read-only commit correctly restores write access for the next transaction.
// =============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.exceptions.translators;
using pengdows.crud.infrastructure;

namespace pengdows.crud.dialects;

/// <summary>
/// SAP HANA dialect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>UPSERT:</strong> Uses MERGE INTO with a "USING (SELECT ... FROM DUMMY) s" source
/// (HANA rejects the VALUES-row-constructor source shape outright).
/// </para>
/// <para>
/// <strong>Parameters:</strong> Positional "?" only, no named-parameter support.
/// </para>
/// </remarks>
internal sealed class HanaDialect : SqlDialect
{
    internal HanaDialect(DbProviderFactory factory, ILogger logger)
        : base(factory, logger)
    {
    }

    public override SupportedDatabase DatabaseType => SupportedDatabase.SapHana;

    // Positional-only parameter markers; confirmed live via DataSourceInformation
    // (ParameterMarkerFormat == "?") and a real positional INSERT.
    public override string ParameterMarker => "?";
    public override bool SupportsNamedParameters => false;

    // Confirmed via DataSourceInformation.ParameterNameMaxLength.
    public override int ParameterNameMaxLength => 128;

    // Schema-qualified object names are real and documented (SYSTEM.TABLE, etc.); unquoted
    // schema/table names fold to uppercase, quoted ones are case-sensitive (confirmed live —
    // see file-level AI SUMMARY).
    public override bool SupportsNamespaces => true;

    // CONFIRMED live: HANA rejects the SQL:2008 "OFFSET n ROWS FETCH NEXT m ROWS ONLY" form
    // outright, but accepts MySQL/PostgreSQL-style "LIMIT m OFFSET n".
    public override bool SupportsOffsetFetch => false;
    public override bool SupportsLimitOffset => true;

    // CONFIRMED live: MERGE INTO ... WHEN MATCHED/WHEN NOT MATCHED works for both branches, but
    // only with a SELECT-based (not VALUES-based) USING source — see RenderMergeSource below.
    public override bool SupportsMerge => true;

    // CONFIRMED live: the ANSI multi-row VALUES clause is rejected. Falls back to one
    // BuildCreate per entity, the same safe path Firebird/InterBase/Informix/Access/Sybase ASE use.
    public override bool SupportsBatchInsert => false;

    // CONFIRMED live: CALL proc(?, ?) with an OUT parameter executes correctly.
    public override ProcWrappingStyle ProcWrappingStyle => ProcWrappingStyle.Call;

    // CONFIRMED live: GENERATED BY DEFAULT AS IDENTITY on a column-store table.
    public override bool SupportsIdentityColumns => true;

    // CONFIRMED live: "DROP TABLE IF EXISTS" is rejected outright, same limitation as Oracle.
    public override bool SupportsDropTableIfExists => false;

    // CONFIRMED live: SAVEPOINT / ROLLBACK TO SAVEPOINT / RELEASE SAVEPOINT all succeed inside a
    // real transaction — full capability set, unlike Oracle (no RELEASE SAVEPOINT at all).
    public override bool SupportsSavepoints => true;

    // GUIDs stored as CHAR(36)/VARCHAR(36) strings (client-generated) — HanaDbType has no
    // native UUID-shaped member.
    protected override GuidStorageFormat GuidFormat => GuidStorageFormat.String;

    /// <summary>
    /// HANA rejects the base "USING (VALUES (...)) AS s (col1, col2, ...)" row-constructor MERGE
    /// source outright (CONFIRMED live), but accepts an Oracle/DUAL-shaped
    /// "USING (SELECT ... FROM DUMMY) s" derived table.
    /// </summary>
    public override string RenderMergeSource(IReadOnlyList<IColumnInfo> columns,
        IReadOnlyList<string> parameterNames)
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

        var select = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                select.Append(", ");
            }

            var placeholder = MakeParameterName(parameterNames[i]);
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            select.Append(placeholder);
            select.Append(" AS ");
            select.Append(WrapObjectName(columns[i].Name));
        }

        return string.Concat("USING (SELECT ", select.ToString(), " FROM DUMMY) s");
    }

    public override void AppendPaging(ISqlQueryBuilder query, int offset, int limit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Must be >= 0.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Must be > 0.");
        }

        // CONFIRMED live: LIMIT/OFFSET only — see SupportsOffsetFetch above.
        query.Append(" LIMIT ").Append(limit);
        if (offset > 0)
        {
            query.Append(" OFFSET ").Append(offset);
        }
    }

    public override string GetVersionQuery()
    {
        return "SELECT VERSION FROM SYS.M_DATABASE";
    }

    // CONFIRMED live (real container): no ApplicationName-equivalent keyword exists on the real
    // Sap.Data.Hana.HanaConnectionStringBuilder (72 properties inspected via reflection).
    // "ConnectionTimeout" was chosen and CONFIRMED LIVE via the actual production mechanism
    // (ConnectionPoolingConfiguration.ApplyPoolDiscriminator's generic DbConnectionStringBuilder,
    // NOT the HANA-specific typed builder — the typed builder canonicalizes away any property
    // explicitly set to its own default value, which would silently produce an identical
    // connection string and defeat a discriminator built that way): the resulting string
    // genuinely contains "ConnectionTimeout=15" as literal text, and HanaConnection connects
    // successfully with it. 15 is this property's own compiled-in default, so setting it
    // explicitly is guaranteed behaviorally inert by construction.
    internal override string? ReadOnlyPoolDiscriminatorSettingName => "ConnectionTimeout";
    internal override string? ReadOnlyPoolDiscriminatorSettingValue => "15";

    // CONFIRMED LIVE (real container) — a genuine pooled-connection-hygiene hazard: HANA's
    // "SET TRANSACTION READ ONLY" is STICKY at the session level. It persists past COMMIT and
    // affects the NEXT transaction on the same physical connection — reproduced directly: mark a
    // transaction read-only, commit it with no write at all, then start a brand-new transaction
    // with NO explicit SET statement at all; a write inside that second transaction still fails
    // with the exact same NativeError 129 ("...please use \"SET TRANSACTION READ WRITE\"
    // statement first"). Without this reset, a pooled physical connection that
    // TryEnterReadOnlyTransaction below marked read-only could reject an unrelated caller's later
    // WRITE operation, unpredictably, depending on which pooled connection happens to be handed
    // out. Also CONFIRMED LIVE that "SET TRANSACTION READ WRITE" is safe to run as a bare
    // preamble with no active transaction (a no-op on an already-read-write session), and that
    // running it after a stuck read-only commit correctly restores write access for the next
    // transaction.
    public override string GetBaseSessionSettings()
    {
        return "SET TRANSACTION READ WRITE";
    }

    // CONFIRMED LIVE (real container): "SET TRANSACTION READ ONLY" inside an active transaction
    // is accepted (parses fine) and genuinely enforces read-only — a subsequent write fails with
    // NativeError 129 (see SqlDialect.cs's TryClassifyProviderException switch), while a read
    // inside the same read-only transaction succeeds normally. Uses the same TryExecuteReadOnlySql
    // shared helper Oracle/Informix use for their own identical mechanism. Do NOT assume the
    // ANSI SET TRANSACTION READ ONLY pattern works uniformly across dialects without checking —
    // it was a flat syntax error on both Db2 and Sybase ASE this same session.
    private const string SetTransactionReadOnlySql = "SET TRANSACTION READ ONLY";

    public override void TryEnterReadOnlyTransaction(ITransactionContext transaction)
    {
        TryExecuteReadOnlySql(transaction, SetTransactionReadOnlySql, "SAP HANA");
    }

    public override ValueTask TryEnterReadOnlyTransactionAsync(ITransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        return TryExecuteReadOnlySqlAsync(transaction, SetTransactionReadOnlySql, "SAP HANA", cancellationToken);
    }

    // Isolation-level data (GetSupportedIsolationLevels/GetIsolationProfileMapping on 3.0) lives
    // in IsolationResolver.cs's SupportedDatabase.SapHana cases on this branch instead.

    // All four codes below were captured live from a real Sap.Data.Hana.HanaException thrown
    // against a real saplabs/hanaexpress container (see file-level AI SUMMARY).
    // DbExceptionTranslationSupport.TryGetErrorCode already finds HanaException.NativeError via
    // reflection — SqlState/ErrorCode are both unusable for classification via this driver.
    public override bool IsUniqueViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 301;

    // 461: insert/update references a parent row that does not exist.
    // 462: delete/update blocked because a dependent child row still exists.
    public override bool IsForeignKeyViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) is 461 or 462;

    public override bool IsNotNullViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 287;

    public override bool IsCheckConstraintViolation(DbException ex) =>
        DbExceptionTranslationSupport.TryGetErrorCode(ex) == 677;

    // Advisory-level category classification (ISqlDialect.AnalyzeException/ClassifyException,
    // codes 133/131/129) lives in SqlDialect.cs's private TryClassifyProviderException switch on
    // this branch - 2.0.6 predates 3.0's dialect-owned classification refactor, so there is no
    // virtual member here to override. The exception TYPE actually thrown is determined by
    // HanaExceptionTranslator, not this advisory path.
}
