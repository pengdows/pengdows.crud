using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.dialects;

internal interface IInternalSqlDialect : ISqlDialect
{
    /// <summary>
    /// Reapplies provider-specific metadata after a cached command receives a set-valued value.
    /// </summary>
    void ConfigureSetValuedParameter(DbParameter parameter, Array value);

    /// <summary>
    /// Renders provider-specific JSON casts for parameter placeholders.
    /// </summary>
    string RenderJsonArgument(string parameterMarker, IColumnInfo column);

    /// <summary>
    /// Stamps provider-specific metadata on JSON parameters.
    /// </summary>
    void TryMarkJsonParameter(DbParameter parameter, IColumnInfo column);

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
            if (columns[i].IsJsonType)
            {
                placeholder = RenderJsonArgument(placeholder, columns[i]);
            }

            values[i] = placeholder;
            names[i] = WrapSimpleName(columns[i].Name);
        }

        return $"USING (VALUES ({string.Join(", ", values)})) AS s ({string.Join(", ", names)})";
    }


    /// <summary>
    /// Splits an insert-returning clause that must render before VALUES into its
    /// prefix/output/returning pieces. <paramref name="clause"/> is the dialect's own
    /// <see cref="ISqlDialect.RenderInsertReturningClause"/> output (e.g. SQL Server's
    /// <c>OUTPUT INSERTED.col</c>). Only called when <see cref="ISqlDialect.InsertReturningClauseBeforeValues"/>
    /// is true. Default: no prefix, the whole clause becomes the pre-VALUES piece, no
    /// post-VALUES piece — the ordinary "OUTPUT/RETURNING before VALUES" shape. Override when a
    /// dialect's real protocol needs something split across all three positions (SQL Server's
    /// OUTPUT INTO a table variable, required so triggers on the target table don't break a
    /// plain <c>OUTPUT INSERTED.col</c> result set).
    /// </summary>
    (string Prefix, string Output, string Returning) RenderOutputInsertClauses(string idWrapped, string clause)
    {
        return (string.Empty, clause, string.Empty);
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
    /// docs/planning/future-work.md for why parameter-baking caches aren't safely fingerprintable yet).
    /// </summary>
    string CacheFingerprint { get; }
}
