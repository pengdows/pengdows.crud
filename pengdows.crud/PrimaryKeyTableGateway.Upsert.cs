// =============================================================================
// FILE: PrimaryKeyTableGateway.Upsert.cs
// PURPOSE: UPSERT operations keyed on [PrimaryKey] columns.
//
// AI SUMMARY:
// - BuildUpsert() - Dispatches to dialect-specific builder (MERGE, then ON CONFLICT, then
//   ON DUPLICATE KEY); throws if there are no updateable non-key columns (pure junction
//   table), unless Firebird which supports pure-key upsert. (The constructor already
//   rejects entities with no [PrimaryKey].)
// - UpsertAsync() - Executes BuildUpsert, then post-execute concurrency check:
//   * 0 rows + [Version] + (non-Firebird MERGE dialect or ON CONFLICT ... WHERE dialect)
//     → ConcurrencyConflictException
//   * MySQL/MariaDB ON DUPLICATE KEY, ON CONFLICT dialects with SupportsOnConflictWhere=false,
//     and Firebird: conflict not detectable, no exception
// - Database-specific syntax (single-entity):
//   * MERGE-capable dialects (e.g. SQL Server, Oracle, Snowflake, Db2, PostgreSQL 15+,
//     DuckDB 1.4+): MERGE ... WHEN MATCHED [AND t.ver = s.ver] THEN UPDATE
//     (Oracle: WHEN MATCHED THEN UPDATE ... [WHERE t.ver = s.ver])
//   * ON CONFLICT dialects without MERGE (e.g. PostgreSQL < 15, CockroachDB, SQLite):
//     INSERT ... ON CONFLICT DO UPDATE [WHERE table.ver = EXCLUDED.ver when supported]
//   * MySQL/MariaDB: INSERT ... ON DUPLICATE KEY UPDATE (no version guard)
//   * Firebird: UPDATE OR INSERT ... MATCHING (...)
// - Batch variants (BuildBatchUpsert, BatchUpsertAsync):
//   * ON CONFLICT path (preferred over MERGE when both are supported): multi-row insert
//     with version WHERE predicate from PkTemplates.UpsertOnConflictVersionWhere
//   * ON DUPLICATE KEY: multi-row insert with alias quoting
//   * MERGE/Firebird: falls back to per-entity BuildUpsert loop
//   * BatchUpsertAsync throws ConcurrencyConflictException when a guarded container affects
//     fewer rows than it holds entities (BatchUpsertCanDetectVersionConflict); ON DUPLICATE KEY
//     and Firebird cannot detect a stale [Version] and never throw for it
// - Throws NotSupportedException for fallback/unknown dialects.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud;

/// <summary>
/// PrimaryKeyTableGateway partial: UPSERT and batch UPSERT operations.
/// </summary>
public partial class PrimaryKeyTableGateway<TEntity>
{
    /// <inheritdoc/>
    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;

        // A database with no upsert statement (InterBase) is refused before the pure-key check below,
        // which would otherwise blame the entity's columns (HARN-011).
        if (ctx.DataSourceInfo.IsUsingFallbackDialect ||
            !(ctx.DataSourceInfo.SupportsMerge || ctx.DataSourceInfo.SupportsInsertOnConflict ||
              ctx.DataSourceInfo.SupportsOnDuplicateKey))
        {
            throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
        }

        // Only a dialect whose upsert needs no SET clause (Firebird's UPDATE OR INSERT MATCHING) can
        // upsert an entity whose only columns are its primary key.
        if (!GetDialect(ctx).SupportsPureKeyUpsert())
        {
            var dialect = GetDialect(ctx);
            var template = GetPkTemplatesForDialect(dialect);
            if (template.UpsertUpdateFragment == null)
            {
                throw new NotSupportedException(
                    $"Upsert requires at least one non-primary-key updateable column. " +
                    $"'{typeof(TEntity).Name}' has only primary key columns.");
            }
        }

        if (ctx.DataSourceInfo.SupportsMerge)
        {
            return BuildPkUpsertMerge(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BuildPkUpsertOnConflict(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return BuildPkUpsertOnDuplicate(entity, ctx);
        }

        throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        // BuildUpsert mutates audit fields as a side effect of building the statement, before
        // anything executes. Restore them whenever the write doesn't actually succeed —
        // including the version-conflict/0-rows-affected case below.
        var auditSnapshot = SnapshotAuditFields(entity);
        try
        {
            var ctx = context ?? _context;
            await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
            var dialect = GetDialect(ctx);
            await using var sc = BuildUpsert(entity, ctx);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                // 0 rows affected without an exception is a failed write regardless of whether
                // this entity is versioned or the dialect can detect a conflict.
                RestoreAuditFields(entity, auditSnapshot);
                if (_versionColumn != null)
                {
                    var canDetect = UpsertCanDetectVersionConflict(dialect);
                    if (canDetect)
                    {
                        throw VersionConflict(ctx);
                    }
                }
            }

            return rowsAffected;
        }
        catch
        {
            RestoreAuditFields(entity, auditSnapshot);
            throw;
        }
    }

    // =========================================================================
    // BATCH UPSERT
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchUpsert(IReadOnlyList<TEntity> entities,
        IDatabaseContext? context = null)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count == 0)
        {
            return Array.Empty<ISqlContainer>();
        }

        var ctx = context ?? _context;

        if (ctx.DataSourceInfo.SupportsInsertOnConflict || ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            // Batch paths require an UPDATE SET fragment; pure-PK entities are not supported.
            var dialect = GetDialect(ctx);
            var template = GetPkTemplatesForDialect(dialect);
            if (template.UpsertUpdateFragment == null)
            {
                throw new NotSupportedException(
                    $"Upsert requires at least one non-primary-key updateable column. " +
                    $"'{typeof(TEntity).Name}' has only primary key columns.");
            }
        }

        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BuildPkBatchUpsertOnConflict(entities, ctx);
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey &&
            !UpsertFragmentHasUnreliableIncoming(GetDialect(ctx),
                GetPkTemplatesForDialect(GetDialect(ctx)).UpsertUpdateFragmentOnConflict, _tableInfo.Columns.Values))
        {
            return BuildPkBatchUpsertOnDuplicate(entities, ctx);
        }

        // Fallback: one-by-one (MERGE or unsupported — BuildUpsert handles its own guard)
        var result = new List<ISqlContainer>(entities.Count);
        foreach (var entity in entities)
        {
            var container = BuildUpsert(entity, ctx);
            TrackBatchContainer(container, [entity]);
            result.Add(container);
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (entities.Count == 0)
        {
            return 0;
        }

        var ctx = context ?? _context;
        await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
        if (entities.Count == 1)
        {
            return await UpsertAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
        }

        var auditSnapshots = _hasAuditColumns
            ? entities.Select(SnapshotAuditFields).ToArray()
            : Array.Empty<AuditFieldSnapshot>();
        var containers = BuildBatchUpsert(entities, ctx);
        var total = 0;
        var completedContainers = 0;
        var versionConflictDetectionApplies = BatchUpsertCanDetectVersionConflict(ctx);
        try
        {
            foreach (var sc in containers)
            {
                await using var owned = sc;
                cancellationToken.ThrowIfCancellationRequested();
                var affected = await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);

                // A guarded upsert that skips a stale row reports it as not affected.
                if (versionConflictDetectionApplies &&
                    _batchContainerEntities.TryGetValue(sc, out var chunkEntities) &&
                    affected < chunkEntities.Count)
                {
                    throw BatchVersionConflict(ctx, chunkEntities, affected);
                }

                total += affected;
                completedContainers++;
            }
        }
        catch
        {
            RestoreBatchAuditFields(containers, completedContainers, entities, auditSnapshots);
            throw;
        }

        return total;
    }

    // =========================================================================
    // Private: dialect-specific upsert builders
    // =========================================================================

    private void PrepareForPkUpsert(TEntity entity)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(entity, false);
        }

        InitializeVersion(entity);
    }

    private void PrepareForPkUpsert(TEntity entity, IAuditValues? cachedAuditValues)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(entity, false, cachedAuditValues);
        }

        InitializeVersion(entity);
    }

    private ISqlContainer BuildPkUpsertOnConflict(TEntity entity, IDatabaseContext context)
    {
        var dialect = GetDialect(context);
        PrepareForPkUpsert(entity);

        var insertableColumns = GetCachedInsertableColumns();
        var template = GetPkTemplatesForDialect(dialect);

        var sc = context.CreateSqlContainer();
        var parameters = AppendInsertIntoColumnsAndValues(sc, dialect, insertableColumns, entity);

        sc.Query.Append(" ON CONFLICT (");

        var pkCols = _tableInfo.PrimaryKeys;
        for (var i = 0; i < pkCols.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(pkCols[i].Name));
        }

        sc.Query.Append(") DO UPDATE SET ").Append(template.UpsertUpdateFragmentOnConflict);

        if (template.UpsertOnConflictVersionWhere != null)
        {
            sc.Query.Append(" ").Append(template.UpsertOnConflictVersionWhere);
        }

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildPkUpsertOnDuplicate(TEntity entity, IDatabaseContext context)
    {
        var dialect = GetDialect(context);
        PrepareForPkUpsert(entity);

        var insertableColumns = GetCachedInsertableColumns();
        var template = GetPkTemplatesForDialect(dialect);

        var sc = context.CreateSqlContainer();
        var parameters = AppendInsertIntoColumnsAndValues(sc, dialect, insertableColumns, entity);

        var incomingAlias = dialect.UpsertIncomingAlias;
        if (!string.IsNullOrEmpty(incomingAlias))
        {
            sc.Query.Append(" AS ").Append(dialect.WrapSimpleName(incomingAlias));
        }

        sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(ReuseParametersForUnreliableIncoming(dialect,
            template.UpsertUpdateFragmentOnConflict!, insertableColumns, parameters));

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildPkUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        if (!GetDialect(context).EmitsAnsiMergeSyntax())
        {
            return BuildPkFirebirdMergeUpsert(entity, context);
        }

        var dialect = GetDialect(context);
        ThrowIfVersionedMergeUpsertUnsupported(dialect);
        PrepareForPkUpsert(entity);

        var insertableColumns = GetCachedInsertableColumns();
        var template = GetPkTemplatesForDialect(dialect);
        var parameters = new List<DbParameter>(insertableColumns.Count);

        // Build INSERT VALUES for merge source
        var colNames = new List<string>(insertableColumns.Count);
        var paramNames = new List<string>(insertableColumns.Count);
        for (var i = 0; i < insertableColumns.Count; i++)
        {
            var pName = $"i{i}";
            paramNames.Add(pName);
            colNames.Add(dialect.WrapSimpleName(insertableColumns[i].Name));

            var col = insertableColumns[i];
            var value = col.MakeParameterValueFromField(entity);
            var param = dialect.CreateDbParameter(pName, col.DbType, value);
            dialect.MarkColumnParameter(param, col);

            parameters.Add(param);
        }

        // WRT-004: keys through the source, every other value bound directly where the dialect needs it.
        var direct = dialect.MergeBindsValuesDirectly()
            ? RenderDirectBoundMerge(dialect, insertableColumns, paramNames, _tableInfo.PrimaryKeys,
                template.UpsertMergeUpdateColumns ?? new List<IColumnInfo>())
            : default((string Source, string UpdateSet, string InsertValues)?);
        var mergeSource = direct?.Source
                          ?? dialect.RenderMergeSource(insertableColumns.ToList(), paramNames, BuildWrappedTableName(dialect));

        var insertColSb = SbLite.Create(stackalloc char[512]);
        var insertValSb = SbLite.Create(stackalloc char[512]);
        var joinSb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
            foreach (var col in insertableColumns)
            {
                if (insertColSb.Length > 0)
                {
                    insertColSb.Append(", ");
                    insertValSb.Append(", ");
                }

                var wrapped = dialect.WrapSimpleName(col.Name);
                insertColSb.Append(wrapped);
                insertValSb.Append("s.");
                insertValSb.Append(wrapped);
            }

            var pkCols = _tableInfo.PrimaryKeys;
            for (var i = 0; i < pkCols.Count; i++)
            {
                if (i > 0)
                {
                    joinSb.Append(SqlFragments.And);
                }

                joinSb.Append("t.");
                joinSb.Append(dialect.WrapSimpleName(pkCols[i].Name));
                joinSb.Append(SqlFragments.EqualsOp);
                joinSb.Append("s.");
                joinSb.Append(dialect.WrapSimpleName(pkCols[i].Name));
            }

            var onClause = dialect.RenderMergeOnClause(joinSb.ToString());

            var sc = context.CreateSqlContainer();
            sc.Query.Append("MERGE INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" t ")
                .Append(mergeSource)
                .Append(" ON ")
                .Append(onClause)
                // Version check in WHEN MATCHED arm (not in ON clause): a stale-version row still
                // matches ON, so the WHEN NOT MATCHED INSERT arm (which would produce a unique
                // constraint violation) never fires, but the UPDATE is skipped → 0 rows →
                // ConcurrencyConflictException.
                .Append(template.UpsertMergeVersionCondition != null
                    ? $" WHEN MATCHED {template.UpsertMergeVersionCondition} THEN UPDATE SET "
                    : " WHEN MATCHED THEN UPDATE SET ");

            sc.Query.Append(direct?.UpdateSet ?? template.UpsertUpdateFragment);
            if (template.UpsertMergeUpdateWhere != null)
            {
                sc.Query.Append(" ").Append(template.UpsertMergeUpdateWhere);
            }

            sc.Query.Append(" WHEN NOT MATCHED THEN INSERT (")
                .Append(insertColSb.AsSpan())
                .Append(") VALUES (");
            if (direct is { } bound)
            {
                sc.Query.Append(bound.InsertValues);
            }
            else
            {
                sc.Query.Append(insertValSb.AsSpan());
            }

            sc.Query.Append(")");

            if (dialect.RequiresMergeStatementTerminator)
            {
                sc.Query.Append(';');
            }

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            insertColSb.Dispose();
            insertValSb.Dispose();
            joinSb.Dispose();
        }
    }

    private ISqlContainer BuildPkFirebirdMergeUpsert(TEntity entity, IDatabaseContext context)
    {
        var dialect = GetDialect(context);
        PrepareForPkUpsert(entity);

        var insertableColumns = GetCachedInsertableColumns();
        var parameters = new List<DbParameter>(insertableColumns.Count);

        var insertColSb = SbLite.Create(stackalloc char[512]);
        var valSb = SbLite.Create(stackalloc char[512]);
        try
        {
            for (var i = 0; i < insertableColumns.Count; i++)
            {
                if (i > 0)
                {
                    insertColSb.Append(", ");
                    valSb.Append(", ");
                }

                insertColSb.Append(dialect.WrapSimpleName(insertableColumns[i].Name));

                var pName = $"i{i}";
                var col = insertableColumns[i];
                var value = col.MakeParameterValueFromField(entity);
                var param = dialect.CreateDbParameter(pName, col.DbType, value);
                dialect.MarkColumnParameter(param, col);

                if (dialect.RendersColumnArgument(col))
                {
                    valSb.Append(dialect.RenderColumnArgument(dialect.MakeParameterName(pName), col));
                }
                else
                {
                    valSb.Append(dialect.MakeParameterName(pName));
                }

                parameters.Add(param);
            }

            var pkCols = _tableInfo.PrimaryKeys;

            var sc = context.CreateSqlContainer();
            sc.Query.Append("UPDATE OR INSERT INTO ")
                .Append(BuildWrappedTableName(dialect))
                .Append(" (")
                .Append(insertColSb.AsSpan())
                .Append(") VALUES (")
                .Append(valSb.AsSpan())
                .Append(") MATCHING (");

            for (var i = 0; i < pkCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(pkCols[i].Name));
            }

            sc.Query.Append(");");

            sc.AddParameters(parameters);
            return sc;
        }
        finally
        {
            insertColSb.Dispose();
            valSb.Dispose();
        }
    }

    // =========================================================================
    // Batch upsert helpers
    // =========================================================================

    private IReadOnlyList<ISqlContainer> BuildPkBatchUpsertOnConflict(IReadOnlyList<TEntity> entities,
        IDatabaseContext context)
    {
        var dialect = GetDialect(context);
        var insertableColumns = GetCachedInsertableColumns();
        var template = GetPkTemplatesForDialect(dialect);

        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

        foreach (var entity in entities)
        {
            PrepareForPkUpsert(entity, auditValues);
        }

        var pkCols = _tableInfo.PrimaryKeys;
        var chunks = ChunkList(entities, insertableColumns.Count, context.MaxParameterLimit,
            dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildPkBatchInsertContainer(chunk, insertableColumns, context, dialect);

            sc.Query.Append(" ON CONFLICT (");
            for (var i = 0; i < pkCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(pkCols[i].Name));
            }

            // Batches always use ON CONFLICT, even on a dialect that also supports MERGE
            // (PostgreSQL 15+), so they take the ON CONFLICT fragment, never the MERGE one.
            var updateFragment = template.UpsertUpdateFragmentOnConflict;

            sc.Query.Append(") DO UPDATE SET ").Append(updateFragment);

            if (template.UpsertOnConflictVersionWhere != null)
            {
                sc.Query.Append(" ").Append(template.UpsertOnConflictVersionWhere);
            }

            result.Add(sc);
        }

        return result;
    }

    private IReadOnlyList<ISqlContainer> BuildPkBatchUpsertOnDuplicate(IReadOnlyList<TEntity> entities,
        IDatabaseContext context)
    {
        var dialect = GetDialect(context);
        var insertableColumns = GetCachedInsertableColumns();
        var template = GetPkTemplatesForDialect(dialect);

        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

        foreach (var entity in entities)
        {
            PrepareForPkUpsert(entity, auditValues);
        }

        var chunks = ChunkList(entities, insertableColumns.Count, context.MaxParameterLimit,
            dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildPkBatchInsertContainer(chunk, insertableColumns, context, dialect);

            var incomingAlias = dialect.UpsertIncomingAlias;
            if (!string.IsNullOrEmpty(incomingAlias))
            {
                sc.Query.Append(" AS ").Append(dialect.WrapSimpleName(incomingAlias));
            }

            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragmentOnConflict);
            result.Add(sc);
        }

        return result;
    }

    /// <summary>
    /// Builds a multi-row INSERT container shared by batch create and batch upsert paths.
    /// Mirrors the logic in TableGateway.Batch.cs BuildBatchInsertContainer.
    /// </summary>
    private ISqlContainer BuildPkBatchInsertContainer(
        IReadOnlyList<TEntity> chunk,
        IReadOnlyList<IColumnInfo> insertableColumns,
        IDatabaseContext ctx,
        ISqlDialect dialect)
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
        dialect.BuildBatchInsertSql(wrappedTableName, wrappedColumnNames, chunk.Count, sc.Query,
            (row, col) => cells[row * columnCount + col], insertableColumns);

        for (var row = 0; row < chunk.Count; row++)
        {
            for (var c = 0; c < columnCount; c++)
            {
                var column = insertableColumns[c];
                var value = cells[row * columnCount + c];

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

    private void TrackBatchContainer(ISqlContainer container, IReadOnlyList<TEntity> entities)
    {
        _batchContainerEntities.Remove(container);
        _batchContainerEntities.Add(container, entities);
    }

    private void RestoreBatchAuditFields(
        IReadOnlyList<ISqlContainer> containers,
        int firstUnexecutedContainer,
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<AuditFieldSnapshot> snapshots)
    {
        if (!_hasAuditColumns)
        {
            return;
        }

        // Each entity's snapshot by reference identity, built once (its first position, as the
        // linear search found): the search per entity made the restore O(N x chunk) (REV-065).
        var snapshotIndex = new Dictionary<TEntity, int>(entities.Count, ReferenceEqualityComparer.Instance);
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            snapshotIndex.TryAdd(entities[entityIndex], entityIndex);
        }

        for (var containerIndex = firstUnexecutedContainer; containerIndex < containers.Count; containerIndex++)
        {
            if (!_batchContainerEntities.TryGetValue(containers[containerIndex], out var chunk))
            {
                continue;
            }

            foreach (var entity in chunk)
            {
                if (snapshotIndex.TryGetValue(entity, out var entityIndex))
                {
                    RestoreAuditFields(entity, snapshots[entityIndex]);
                }
            }
        }
    }
}
