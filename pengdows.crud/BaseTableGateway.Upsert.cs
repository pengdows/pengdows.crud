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

using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: insert/upsert SQL shared by both gateways.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
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
            sc.Query.Append(" ON CONFLICT (");
            for (var i = 0; i < conflictColumns.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(conflictColumns[i].Name));
            }

            sc.Query.Append(") DO UPDATE SET ").Append(updateFragment);

            if (versionWhere != null)
            {
                sc.Query.Append(' ').Append(versionWhere);
            }

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
}
