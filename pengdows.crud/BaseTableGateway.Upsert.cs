// =============================================================================
// FILE: BaseTableGateway.Upsert.cs
// PURPOSE: The insert/upsert SQL both gateways share (DRY-016).
//
// AI SUMMARY:
// - PrepareForInsertOrUpsert(): audit fields and version before an insert or upsert.
// - BuildBatchInsertContainer(): one multi-row INSERT chunk, parameters bound, tracked.
// - BuildBatchUpsertOnConflict()/BuildBatchUpsertOnDuplicate(): chunked batch upserts; each
//   gateway passes its conflict key and its templates' fragments.
// =============================================================================

using System.Data;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// The single-row upsert statement shape a database can run. Internal: callers use BuildUpsert.
/// </summary>
internal enum SingleRowUpsertShape
{
    /// <summary>The database has no upsert statement.</summary>
    None,

    /// <summary>MERGE ... WHEN MATCHED / NOT MATCHED.</summary>
    Merge,

    /// <summary>INSERT ... ON CONFLICT ... DO UPDATE.</summary>
    OnConflict,

    /// <summary>INSERT ... ON DUPLICATE KEY UPDATE.</summary>
    OnDuplicate,
}

/// <summary>
/// The multi-row upsert statement shape for a batch. Internal: callers use BuildBatchUpsert.
/// </summary>
internal enum BatchUpsertShape
{
    /// <summary>INSERT ... ON CONFLICT, one statement for the batch.</summary>
    OnConflict,

    /// <summary>INSERT ... ON DUPLICATE KEY UPDATE, one statement for the batch.</summary>
    OnDuplicate,

    /// <summary>One single-row upsert per entity (MERGE dialects, and unreliable incoming values).</summary>
    PerEntity,
}

/// <summary>
/// BaseTableGateway partial: insert/upsert SQL shared by both gateways.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <summary>
    /// The one place that chooses the batch upsert shape for both gateways: ON CONFLICT where the dialect supports it,
    /// else ON DUPLICATE KEY when the incoming values are reliable, else one single-row upsert per entity.
    /// <paramref name="fragmentIsReliable"/> is false when the ON DUPLICATE KEY form would read an untrustworthy incoming
    /// value (DEC-012); the caller computes it because it owns the update fragment.
    /// </summary>
    private protected static BatchUpsertShape SelectBatchUpsertShape(IDatabaseContext ctx, bool fragmentIsReliable)
    {
        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BatchUpsertShape.OnConflict;
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey && fragmentIsReliable)
        {
            return BatchUpsertShape.OnDuplicate;
        }

        return BatchUpsertShape.PerEntity;
    }

    /// <summary>
    /// The one place that chooses the single-row upsert shape: MERGE where the dialect supports it and does not prefer
    /// ON CONFLICT, else ON CONFLICT, else ON DUPLICATE KEY. Each gateway's BuildUpsert branches on this result and calls
    /// its own private builder for the shape, so the choice is never repeated.
    /// </summary>
    private protected static SingleRowUpsertShape SelectSingleRowUpsertShape(IDatabaseContext ctx)
    {
        if (ctx.DataSourceInfo.SupportsMerge && !GetDialect(ctx).UpsertPrefersOnConflict())
        {
            return SingleRowUpsertShape.Merge;
        }

        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return SingleRowUpsertShape.OnConflict;
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return SingleRowUpsertShape.OnDuplicate;
        }

        return SingleRowUpsertShape.None;
    }

    /// <summary>
    /// Refuses an upsert whose conflict key has a NULL component. A NULL never matches in the key, whichever form the
    /// database uses (MERGE's ON t.k = s.k, ON CONFLICT, ON DUPLICATE KEY), so the row would be inserted again: a silent
    /// duplicate where the unique index allows NULLs (SQLite, PostgreSQL, MySQL) or a unique violation where it does not
    /// (SQL Server). Proven live, audit G-06.
    /// </summary>
    private protected void RejectNullUpsertKey(TEntity entity, IReadOnlyList<IColumnInfo> keyColumns)
    {
        for (var i = 0; i < keyColumns.Count; i++)
        {
            var column = keyColumns[i];
            if (column.MakeParameterValueFromField(entity) is null or DBNull)
            {
                throw new InvalidOperationException(
                    $"Cannot upsert {typeof(TEntity).Name}: key column '{column.Name}' is NULL. A NULL never matches in an " +
                    "upsert's conflict key, so the row would be inserted again instead of updated.");
            }
        }
    }

    /// <summary>
    /// Runs an upsert statement. Where the dialect's upsert is not atomic against a concurrent insert of the same key
    /// (<see cref="IInternalSqlDialect.UpsertRetriesOnUniqueViolation"/>), a unique violation is retried once: the row
    /// exists by then, so the statement matches. A second violation is a different unique index and is thrown.
    /// </summary>
    private protected async ValueTask<int> ExecuteUpsertAsync(ISqlContainer sc, IDatabaseContext ctx,
        CancellationToken cancellationToken)
    {
        try
        {
            return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
        }
        catch (UniqueConstraintViolationException) when (GetDialect(ctx).UpsertRetriesOnUniqueViolation())
        {
            return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sets an entity's audit fields and starts its version before an insert or upsert.</summary>
    private protected void PrepareForInsertOrUpsert(TEntity e)
    {
        // As CreateAsync: time-only fields are set without a resolver, user fields require one.
        if (_hasAuditColumns)
        {
            SetAuditFields(e, false);
        }

        InitializeVersion(e);
    }

    /// <summary>
    /// Batch variant: applies pre-resolved audit values instead of calling Resolve() per entity.
    /// </summary>
    private protected void PrepareForInsertOrUpsert(TEntity e, IAuditValues? cachedAuditValues)
    {
        if (_hasAuditColumns)
        {
            SetAuditFields(e, false, cachedAuditValues);
        }

        InitializeVersion(e);
    }

    /// <summary>
    /// A batch upsert as multi-row INSERT ... ON CONFLICT (<paramref name="conflictColumns"/>) DO UPDATE
    /// SET <paramref name="updateFragment"/> [<paramref name="versionWhere"/>], chunked to the
    /// dialect's parameter and row limits. Batches always use ON CONFLICT, even on a dialect that also
    /// supports MERGE (PostgreSQL 15+), so they take the ON CONFLICT fragment, never the MERGE one.
    /// </summary>
    private protected IReadOnlyList<ISqlContainer> BuildBatchUpsertOnConflict(IReadOnlyList<TEntity> entities,
        IDatabaseContext ctx, IReadOnlyList<IColumnInfo> conflictColumns, string? updateFragment,
        string? versionWhere, bool overridesSystemIdentity)
    {
        var dialect = GetDialect(ctx);
        var insertableColumns = GetCachedInsertableColumns();

        // Resolve audit values once for the whole batch (not once per entity)
        // Throws for user audit fields without a resolver, as CreateAsync does.
        var auditValues = _hasAuditColumns ? ResolveAuditValuesForBatch() : null;

        // Prepare all entities
        foreach (var entity in entities)
        {
            PrepareForInsertOrUpsert(entity, auditValues);
        }

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect, overridesSystemIdentity);

            // Append ON CONFLICT clause
            AppendOnConflictClause(sc.Query, dialect, conflictColumns, updateFragment, versionWhere);

            result.Add(sc);
        }

        return result;
    }

    /// <summary>
    /// A batch upsert as multi-row INSERT ... ON DUPLICATE KEY UPDATE <paramref name="updateFragment"/>
    /// (MySQL family), chunked to the dialect's parameter and row limits.
    /// </summary>
    private protected IReadOnlyList<ISqlContainer> BuildBatchUpsertOnDuplicate(IReadOnlyList<TEntity> entities,
        IDatabaseContext ctx, string? updateFragment)
    {
        var dialect = GetDialect(ctx);
        var insertableColumns = GetCachedInsertableColumns();

        // Resolve audit values once for the whole batch (not once per entity)
        // Throws for user audit fields without a resolver, as CreateAsync does.
        var auditValues = _hasAuditColumns ? ResolveAuditValuesForBatch() : null;

        // Prepare all entities
        foreach (var entity in entities)
        {
            PrepareForInsertOrUpsert(entity, auditValues);
        }

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect);

            // MySQL 8.0.20+: declare the row alias (AS `incoming`) between VALUES and ON DUPLICATE KEY UPDATE
            var incomingAlias = dialect.UpsertIncomingAlias;
            if (!string.IsNullOrEmpty(incomingAlias))
            {
                sc.Query.Append(" AS ").Append(dialect.WrapSimpleName(incomingAlias));
            }

            // Append ON DUPLICATE KEY UPDATE clause
            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(updateFragment);
            result.Add(sc);
        }

        return result;
    }

    /// <summary>
    /// A multi-row INSERT of <paramref name="chunk"/> in the dialect's batch shape (ANSI VALUES, Oracle
    /// INSERT ALL, ...), with OVERRIDING SYSTEM VALUE where a writable [Id] fills an identity column.
    /// </summary>
    private protected ISqlContainer BuildBatchInsertContainer(
        IReadOnlyList<TEntity> chunk,
        IReadOnlyList<IColumnInfo> insertableColumns,
        IDatabaseContext ctx,
        ISqlDialect dialect,
        bool overridesSystemIdentity = false)
    {
        var sc = ctx.CreateSqlContainer();
        var counters = new ClauseCounters();

        var wrappedTableName = BuildWrappedTableName(dialect);
        var wrappedColumnNames = new string[insertableColumns.Count];
        for (var i = 0; i < insertableColumns.Count; i++)
        {
            wrappedColumnNames[i] = dialect.WrapSimpleName(insertableColumns[i].Name);
        }

        var columnCount = insertableColumns.Count;
        var cells = ExtractBatchCells(chunk, insertableColumns);

        // Delegate structure to dialect (ANSI VALUES, Oracle INSERT ALL, etc.)
        dialect.BuildBatchInsertSql(wrappedTableName, wrappedColumnNames, chunk.Count, sc.Query,
            (row, col) => cells[row * columnCount + col], insertableColumns);

        if (overridesSystemIdentity)
        {
            // Only reached for PostgreSQL-family dialects, whose (non-overridden) ANSI
            // BuildBatchInsertSql emits ") VALUES " exactly once, right after the column list.
            sc.Query.Replace(") VALUES ", ") OVERRIDING SYSTEM VALUE VALUES ");
        }

        // Value binding for each entity
        for (var row = 0; row < chunk.Count; row++)
        {
            for (var c = 0; c < columnCount; c++)
            {
                var column = insertableColumns[c];
                var value = cells[row * columnCount + c];

                // Skip parameter creation if it was inlined as NULL literal
                if (value == null || value == DBNull.Value)
                {
                    continue;
                }

                var name = counters.NextBatch();
                var p = dialect.CreateDbParameter(name, column.DbType, value);
                dialect.MarkColumnParameter(p, column);

                sc.AddParameter(p);
            }
        }

        TrackBatchContainer(sc, chunk);
        return sc;
    }

    /// <summary>
    /// Appends <c> ON CONFLICT (keys) DO UPDATE SET fragment [WHERE version guard]</c>, the tail of every
    /// ON CONFLICT upsert, single-row and batch.
    /// </summary>
    private protected static void AppendOnConflictClause(ISqlQueryBuilder query, ISqlDialect dialect,
        IReadOnlyList<IColumnInfo> conflictColumns, string? updateFragment, string? versionWhere)
    {
        query.Append(" ON CONFLICT (");
        for (var i = 0; i < conflictColumns.Count; i++)
        {
            if (i > 0)
            {
                query.Append(", ");
            }

            query.Append(dialect.WrapSimpleName(conflictColumns[i].Name));
        }

        query.Append(") DO UPDATE SET ").Append(updateFragment);
        if (versionWhere != null)
        {
            query.Append(' ').Append(versionWhere);
        }
    }
}
