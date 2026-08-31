// =============================================================================
// FILE: TableGateway.Batch.cs
// PURPOSE: Batch INSERT and UPSERT operations with multi-row VALUES syntax.
//
// AI SUMMARY:
// - BuildBatchCreate() - Generates multi-row INSERT INTO t (cols) VALUES (...), (...)
// - BatchCreateAsync() - Executes batch insert, returns total affected rows
// - BuildBatchUpsert() - Generates dialect-specific batch upsert:
//   * PostgreSQL/CockroachDB: multi-row INSERT ... ON CONFLICT DO UPDATE [WHERE ver = EXCLUDED.ver]
//   * MySQL/MariaDB: multi-row INSERT ... ON DUPLICATE KEY UPDATE
//   * SQL Server/Oracle/Firebird: falls back to individual BuildUpsert per entity
// - Optimistic concurrency: ON CONFLICT batch path appends CachedSqlTemplates.UpsertOnConflictVersionWhere
//   when entity has [Version] column and dialect.SupportsOnConflictWhere — prevents stale-version writes
// - Auto-chunks based on dialect's MaxParameterLimit (with 10% headroom)
// - Sequential parameter naming via ClauseCounters.NextBatch() (b0, b1, b2, ...)
// - NULL values are inlined as NULL literal (no parameter consumed)
// - No RETURNING support for batch (too complex across databases)
// =============================================================================

using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: Batch INSERT and UPSERT operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    private readonly ConditionalWeakTable<ISqlContainer, IReadOnlyList<TEntity>> _batchContainerEntities = new();
    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchCreate(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null)
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
        var dialect = GetDialect(ctx);
        var arrayBound = dialect is SqlDialect concreteDialectForArrayBinding &&
                          concreteDialectForArrayBinding.SupportsArrayBinding;

        // Fallback for dialects that cannot execute EXECUTE BLOCK / multi-row INSERT with ADO.NET parameters
        if (!arrayBound && !dialect.SupportsBatchInsert)
        {
            var fallback = new List<ISqlContainer>(entities.Count);
            foreach (var entity in entities)
            {
                var container = BuildCreate(entity, ctx);
                TrackBatchContainer(container, [entity]);
                fallback.Add(container);
            }

            return fallback;
        }

        var insertableColumns = GetCachedInsertableColumns();

        // Resolve audit values once for the whole batch (not once per entity)
        var auditValues = _hasAuditColumns ? ResolveAuditValuesForBatch() : null;

        // Prepare all entities (audit, version, writable ID)
        foreach (var entity in entities)
        {
            EnsureWritableIdHasValue(entity);
            if (_hasAuditColumns)
            {
                SetAuditFields(entity, false, auditValues);
            }

            PrepareVersionForCreate(entity);
        }

        // Array binding uses exactly one parameter per column regardless of row count (the array
        // IS the multi-row payload), so the per-cell parameter-count limit that constrains the
        // multi-row-VALUES/INSERT-ALL chunking below doesn't apply — chunk by MaxRowsPerBatch alone
        // (paramsPerRow=1 makes ChunkList's parameter-limit math a no-op).
        var chunks = arrayBound
            ? ChunkList(entities, 1, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch)
            : ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = arrayBound
                ? BuildArrayBoundInsertContainer(chunk, insertableColumns, ctx, dialect)
                : BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchCreateAsync(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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
        // Single entity fast path
        if (entities.Count == 1)
        {
            var success = await CreateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
            return success ? 1 : 0;
        }

        var auditSnapshots = _hasAuditColumns
            ? entities.Select(SnapshotAuditFields).ToArray()
            : Array.Empty<AuditFieldSnapshot>();
        var containers = BuildBatchCreate(entities, ctx);
        var totalAffected = 0;
        var completedContainers = 0;

        try
        {
            foreach (var sc in containers)
            {
                await using var owned = sc;
                cancellationToken.ThrowIfCancellationRequested();
                totalAffected += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);
                completedContainers++;
            }
        }
        catch
        {
            RestoreBatchAuditFields(containers, completedContainers, entities, auditSnapshots);
            throw;
        }

        return totalAffected;
    }

    /// <inheritdoc/>
    public ValueTask<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchCreateAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpdateAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpsertAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<int> BatchUpdateAsync(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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
        // Single entity fast path
        if (entities.Count == 1)
        {
            return await UpdateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
        }

        var auditSnapshots = _hasAuditColumns
            ? entities.Select(SnapshotAuditFields).ToArray()
            : Array.Empty<AuditFieldSnapshot>();
        var containers = BuildBatchUpdate(entities, ctx);
        var totalAffected = 0;
        var completedContainers = 0;

        try
        {
            foreach (var sc in containers)
            {
                await using var owned = sc;
                cancellationToken.ThrowIfCancellationRequested();
                var affected = await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);

                // Unlike single-entity UpdateAsync (TableGateway.Core.cs), this loop only ever
                // accumulated totalAffected — a stale-[Version] conflict on one entity in a chunk
                // (fewer rows affected than entities in that container) was invisible. An UPDATE's
                // WHERE clause deterministically matches or doesn't per row (no MySQL-style
                // no-op-update ambiguity), so this check is safe for every dialect/chunk shape.
                if (_versionColumn != null && _batchContainerEntities.TryGetValue(sc, out var chunkEntities))
                {
                    if (affected < chunkEntities.Count)
                    {
                        throw new ConcurrencyConflictException(
                            BuildBatchConflictMessage(chunkEntities, affected), ctx.Product);
                    }

                    // Matches single-entity UpdateAsync: a successful write (checked above) means
                    // the SET clause's "version = version + 1" took effect for every entity in this
                    // container — write the new value back into each entity so it doesn't keep
                    // showing the pre-update value. No-ops for opaque (byte[]/RowVersion) version
                    // columns via WriteBackIncrementedVersion's own guard.
                    foreach (var entity in chunkEntities)
                    {
                        WriteBackIncrementedVersion(entity);
                    }
                }

                totalAffected += affected;
                completedContainers++;
            }
        }
        catch
        {
            RestoreBatchAuditFields(containers, completedContainers, entities, auditSnapshots);
            throw;
        }

        return totalAffected;
    }

    /// <summary>
    /// Builds the message for a batch-update version conflict. When the chunk contains exactly
    /// one entity (always true for dialects without SupportsBatchUpdate, which fall back to one
    /// BuildUpdate container per entity — SQLite, MySQL, MariaDB, Firebird), the conflict
    /// is unambiguous and named directly. For a real multi-row chunk (Postgres/SqlServer/
    /// Snowflake/Oracle), no RETURNING/OUTPUT is used for batch operations (by design, for
    /// cross-dialect portability), so the specific conflicting entity/entities genuinely cannot be
    /// identified from the affected-row count alone — the message says so honestly instead of
    /// implying an attribution it can't make, and warns that no entity in the chunk was written back.
    /// </summary>
    private string BuildBatchConflictMessage(IReadOnlyList<TEntity> chunkEntities, int affected)
    {
        if (chunkEntities.Count == 1)
        {
            return $"Concurrency conflict on {typeof(TEntity).Name} " +
                   $"({DescribeEntityKeyForConflictMessage(chunkEntities[0])}): version mismatch or row deleted.";
        }

        return $"Concurrency conflict on {typeof(TEntity).Name}: expected {chunkEntities.Count} row(s) " +
               $"affected but {affected} succeeded. Which specific entity/entities conflicted cannot be " +
               "individually identified from this batch SQL shape — no RETURNING/OUTPUT is used for batch " +
               "operations, by design, for cross-dialect portability. No entity in this chunk's in-memory " +
               "Version was written back — not written back even for entities that may have succeeded " +
               "server-side. Re-read every entity in this batch from the database before retrying; do not " +
               "assume only some are stale.";
    }

    private string DescribeEntityKeyForConflictMessage(TEntity entity)
    {
        if (_idColumn != null)
        {
            return $"{_idColumn.Name}={_idColumn.MakeParameterValueFromField(entity)}";
        }

        if (_tableInfo.PrimaryKeys.Count > 0)
        {
            return string.Join(", ",
                _tableInfo.PrimaryKeys.Select(pk => $"{pk.Name}={pk.MakeParameterValueFromField(entity)}"));
        }

        return "key unknown";
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchUpdate(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null)
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
        var dialect = GetDialect(ctx);

        if (!dialect.SupportsBatchUpdate)
        {
            // Fallback: one-by-one BuildUpdate per entity
            var fallback = new List<ISqlContainer>(entities.Count);
            foreach (var entity in entities)
            {
                // We assume sequential strategy here for fallback, skip original load (no change tracking)
                var container = BuildUpdate(entity, ctx);
                TrackBatchContainer(container, [entity]);
                fallback.Add(container);
            }

            return fallback;
        }

        var updateableColumns = GetCachedUpdateableColumns();
        // Matches single-row UpdateAsync's contract exactly (TableGateway.Update.cs /
        // TableGateway.Sql.cs): WHERE always keys on [Id], never on [PrimaryKey] — a
        // [PrimaryKey]-only entity (no [Id]) is not a valid update target for this gateway.
        var keyColumns = _idColumn != null
            ? new List<IColumnInfo> { _idColumn }
            : throw new NotSupportedException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");

        // Resolve audit values once for the whole batch
        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

        // Prepare all entities
        foreach (var entity in entities)
        {
            if (_hasAuditColumns)
            {
                SetAuditFields(entity, true, auditValues);
            }

            // Version column increment is usually handled in the SET clause SQL
        }

        // A [Version] column isn't in updateableColumns (excluded above) — it needs its own
        // WHERE/ON predicate (comparing the row's pre-update value) plus, for non-opaque columns,
        // a server-side increment in SET. Both are handled inside BuildBatchUpdateSql via these
        // parameters; here we just need to (a) size chunking for the extra bound value per row
        // and (b) bind that value at the same column index BuildBatchUpdateSql's getValue expects.
        var wrappedVersionColumnName = _versionColumn != null ? dialect.WrapSimpleName(_versionColumn.Name) : null;
        var versionColumnIsOpaque = _versionColumn?.IsOpaqueVersionColumn ?? false;

        // Chunking calculation: keyCols + updateableCols (+1 for the version column's pre-update
        // value, bound purely for the WHERE/ON comparison — see above).
        var totalParamsPerRow = keyColumns.Count + updateableColumns.Count + (_versionColumn != null ? 1 : 0);
        var chunks = ChunkList(entities, totalParamsPerRow, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = ctx.CreateSqlContainer();
            var counters = new ClauseCounters();

            var wrappedTableName = BuildWrappedTableName(dialect);
            var wrappedColNames = updateableColumns.Select(c => dialect.WrapSimpleName(c.Name)).ToList();
            var wrappedKeyNames = keyColumns.Select(c => dialect.WrapSimpleName(c.Name)).ToList();

            // Delegate structure to dialect
            dialect.BuildBatchUpdateSql(wrappedTableName, wrappedColNames, wrappedKeyNames, chunk.Count, sc.Query,
                (row, col) =>
                {
                    var entity = chunk[row];
                    IColumnInfo colInfo;
                    if (col < keyColumns.Count)
                    {
                        colInfo = keyColumns[col];
                    }
                    else if (col < keyColumns.Count + updateableColumns.Count)
                    {
                        colInfo = updateableColumns[col - keyColumns.Count];
                    }
                    else
                    {
                        colInfo = _versionColumn!;
                    }

                    return colInfo.MakeParameterValueFromField(entity);
                },
                wrappedVersionColumnName, versionColumnIsOpaque);

            // Value binding
            for (var row = 0; row < chunk.Count; row++)
            {
                var entity = chunk[row];
                // Bind Keys, then Updateable columns, then the version column's pre-update value
                // (matching the getValue order above).
                foreach (var col in keyColumns)
                {
                    var val = col.MakeParameterValueFromField(entity);
                    if (val == null || val == DBNull.Value)
                    {
                        continue;
                    }
                    sc.AddParameter(dialect.CreateDbParameter(counters.NextBatch(), col.DbType, val));
                }

                foreach (var col in updateableColumns)
                {
                    var val = col.MakeParameterValueFromField(entity);
                    if (val == null || val == DBNull.Value)
                    {
                        continue;
                    }
                    sc.AddParameter(dialect.CreateDbParameter(counters.NextBatch(), col.DbType, val));
                }

                if (_versionColumn != null)
                {
                    var val = _versionColumn.MakeParameterValueFromField(entity);
                    if (val != null && val != DBNull.Value)
                    {
                        sc.AddParameter(dialect.CreateDbParameter(counters.NextBatch(), _versionColumn.DbType, val));
                    }
                }
            }

            TrackBatchContainer(sc, chunk);
            result.Add(sc);
        }

        return result;
    }

    private IReadOnlyList<IColumnInfo> GetCachedUpdateableColumns()
    {
        if (_columnListCache.TryGet("Updateable", out var cached))
        {
            return cached;
        }

        var updateable = new List<IColumnInfo>(_tableInfo.OrderedColumns.Count);
        foreach (var c in _tableInfo.OrderedColumns)
        {
            // [Version] is excluded here the same way TableGateway.Sql.cs's single-entity path
            // excludes it (line ~198) — it needs dialect-specific SET/WHERE handling (increment
            // server-side, compare pre-update value), not a generic "copy the client's value" SET,
            // so BuildBatchUpdate below threads it through BuildBatchUpdateSql's dedicated
            // versionColumnName/versionColumnIsOpaque parameters instead of this list.
            // Otherwise identical to the single-row template's filter — [PrimaryKey] columns ARE
            // updateable here (WHERE keys exclusively on [Id], never on [PrimaryKey]), and
            // [CreatedBy]/[CreatedOn] are excluded because they must never change after CREATE.
            if (!c.IsNonUpdateable && !c.IsId && !c.IsVersion && !c.IsCreatedBy && !c.IsCreatedOn)
            {
                updateable.Add(c);
            }
        }

        return _columnListCache.GetOrAdd("Updateable", _ => updateable);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchUpsert(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null)
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

        // Validate upsert key exists and is usable (PK preferred, writable Id fallback).
        _ = ResolveUpsertKey();

        // For databases that support multi-row upsert via ON CONFLICT or ON DUPLICATE KEY
        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BuildBatchUpsertOnConflict(entities, ctx);
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return BuildBatchUpsertOnDuplicate(entities, ctx);
        }

        // Fallback: databases with MERGE (SQL Server, Oracle, Firebird) or unknown
        // Use individual BuildUpsert per entity
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
    public async ValueTask<int> BatchUpsertAsync(
        IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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
        // Single entity fast path
        if (entities.Count == 1)
        {
            return await UpsertAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
        }

        // MERGE-family dialects use one container per entity. Preserve successful entities'
        // prepared audit state while restoring only the first container that did not execute and
        // every container after it if execution aborts partway through the batch.
        var auditSnapshots = _hasAuditColumns
            ? entities.Select(SnapshotAuditFields).ToArray()
            : Array.Empty<AuditFieldSnapshot>();
        var containers = BuildBatchUpsert(entities, ctx);
        var totalAffected = 0;
        var completedContainers = 0;
        var dialect = GetDialect(ctx);

        // Whether a rows-affected shortfall reliably means "version conflict" depends on which of
        // BuildBatchUpsert's three SQL shapes is in play:
        //  - Per-entity MERGE fallback (SQL Server/Oracle/Firebird): guard always present, same as
        //    single-entity UpsertAsync's already-correct check.
        //  - Chunked ON CONFLICT (PostgreSQL/CockroachDB): guard only present when the dialect
        //    supports a WHERE predicate on DO UPDATE; without it (SQLite/DuckDB) every row always
        //    succeeds unconditionally, so this never spuriously fires for them either way.
        //  - Chunked ON DUPLICATE KEY (MySQL/MariaDB): deliberately EXCLUDED — no version guard
        //    exists there, and the driver reports 0-affected for a row whose values didn't change
        //    (an ordinary no-op upsert, not a conflict). Treating that as a conflict would be a
        //    false positive this fix must not introduce.
        var versionConflictDetectionApplies = _versionColumn != null &&
            (!ctx.DataSourceInfo.SupportsInsertOnConflict && !ctx.DataSourceInfo.SupportsOnDuplicateKey
             || dialect.SupportsOnConflictWhere);

        try
        {
            foreach (var sc in containers)
            {
                await using var owned = sc;
                cancellationToken.ThrowIfCancellationRequested();
                var affected = await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                    .ConfigureAwait(false);

                if (versionConflictDetectionApplies &&
                    _batchContainerEntities.TryGetValue(sc, out var chunkEntities) &&
                    affected < chunkEntities.Count)
                {
                    throw new ConcurrencyConflictException(
                        BuildBatchConflictMessage(chunkEntities, affected), ctx.Product);
                }

                totalAffected += affected;
                completedContainers++;
            }
        }
        catch
        {
            RestoreBatchAuditFields(containers, completedContainers, entities, auditSnapshots);

            throw;
        }

        return totalAffected;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private void PrepareVersionForCreate(TEntity entity)
    {
        if (_versionColumn == null)
        {
            return;
        }

        var current = _versionColumn.MakeParameterValueFromField(entity);
        if (current == null || Utils.IsZeroNumeric(current))
        {
            var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                         _versionColumn.PropertyInfo.PropertyType;
            if (Utils.IsZeroNumeric(TypeCoercionHelper.ConvertWithCache(0, target)))
            {
                var one = TypeCoercionHelper.ConvertWithCache(1, target);
                _versionColumn.PropertyInfo.SetValue(entity, one);
            }
        }
    }

    private ISqlContainer BuildBatchInsertContainer(
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

        // Delegate structure to dialect (ANSI VALUES, Oracle INSERT ALL, etc.)
        dialect.BuildBatchInsertSql(wrappedTableName, wrappedColumnNames, chunk.Count, sc.Query,
            (row, col) => insertableColumns[col].MakeParameterValueFromField(chunk[row]));

        if (overridesSystemIdentity)
        {
            // BuildBatchInsertSql's ANSI shape (the only shape reachable here — the only
            // dialects that can ever set overridesSystemIdentity are the Postgres family, none
            // of which override BuildBatchInsertSql) always emits ") VALUES " exactly once,
            // immediately after the column list. Matches the single-row upsert path's identical
            // OVERRIDING SYSTEM VALUE placement in TableGateway.Upsert.cs.
            sc.Query.Replace(") VALUES ", ") OVERRIDING SYSTEM VALUE VALUES ");
        }

        // Value binding for each entity
        for (var row = 0; row < chunk.Count; row++)
        {
            var entity = chunk[row];

            for (var c = 0; c < insertableColumns.Count; c++)
            {
                var column = insertableColumns[c];
                var value = column.MakeParameterValueFromField(entity);

                // Skip parameter creation if it was inlined as NULL literal
                if (value == null || value == DBNull.Value)
                {
                    continue;
                }

                var name = counters.NextBatch();
                var p = dialect.CreateDbParameter(name, column.DbType, value);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(p, column);
                }

                sc.AddParameter(p);
            }
        }

        TrackBatchContainer(sc, chunk);
        return sc;
    }

    /// <summary>
    /// FEAT-005: builds a single-row-shaped INSERT (one parameter per column, not per cell) whose
    /// parameters are bound to arrays of per-row values, for dialects where
    /// <see cref="SqlDialect.SupportsArrayBinding"/> is true (currently only Oracle, via
    /// <see cref="SqlDialect.ConfigureArrayBinding"/>'s ArrayBindCount reflection hook — see
    /// docs/planning/bulk-loading-design.md's Part 2). Entities must already be fully prepared
    /// (audit/version/writable-ID) by the caller — this method only reads column values, it never
    /// mutates entities, unlike <see cref="PrepareInsertContainer"/>'s single-row path.
    /// </summary>
    private ISqlContainer BuildArrayBoundInsertContainer(
        IReadOnlyList<TEntity> chunk,
        IReadOnlyList<IColumnInfo> insertableColumns,
        IDatabaseContext ctx,
        ISqlDialect dialect)
    {
        var sc = ctx.CreateSqlContainer();
        var counters = new ClauseCounters();

        var wrappedTableName = BuildWrappedTableName(dialect);

        sc.Query.Append("INSERT INTO ");
        sc.Query.Append(wrappedTableName);
        sc.Query.Append(" (");
        for (var c = 0; c < insertableColumns.Count; c++)
        {
            if (c > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(insertableColumns[c].Name));
        }

        sc.Query.Append(") VALUES (");

        for (var c = 0; c < insertableColumns.Count; c++)
        {
            var column = insertableColumns[c];
            var name = counters.NextIns();

            // Row 0's own prepared value becomes the parameter "shell" (Precision/Scale/any
            // reflection-based provider-specific configuration CreateDbParameter already applied
            // for this exact column/value combination), then its scalar .Value is replaced with
            // the full per-row array below — reuses all of CreateDbParameter's existing
            // type-coercion logic (Guid formatting, enum storage, DateTimeOffset-to-UTC, etc.)
            // instead of duplicating it for array binding specifically.
            var firstValue = column.MakeParameterValueFromField(chunk[0]);
            var p = dialect.CreateDbParameter(name, column.DbType, firstValue);
            if (column.IsJsonType)
            {
                dialect.TryMarkJsonParameter(p, column);
            }

            var values = new object[chunk.Count];
            values[0] = p.Value ?? DBNull.Value;
            for (var row = 1; row < chunk.Count; row++)
            {
                var rowValue = column.MakeParameterValueFromField(chunk[row]);
                var rowShell = dialect.CreateDbParameter($"{name}_row{row}", column.DbType, rowValue);
                values[row] = rowShell.Value ?? DBNull.Value;
            }

            p.Value = values;
            sc.AddParameter(p);

            if (c > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(sc.MakeParameterName(p));
        }

        sc.Query.Append(')');

        if (sc is SqlContainer concreteContainer)
        {
            concreteContainer.ArrayBindRowCount = chunk.Count;
        }

        TrackBatchContainer(sc, chunk);
        return sc;
    }

    private IReadOnlyList<ISqlContainer> BuildBatchUpsertOnConflict(
        IReadOnlyList<TEntity> entities, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var insertableColumns = GetCachedInsertableColumns();
        var template = GetTemplatesForDialect(dialect);

        // Resolve audit values once for the whole batch (not once per entity)
        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

        // Prepare all entities
        foreach (var entity in entities)
        {
            PrepareForInsertOrUpsert(entity, auditValues);
        }

        // Resolve conflict key once for all chunks.
        var conflictCols = ResolveUpsertKey();

        // Same condition as the single-row ON CONFLICT/MERGE upsert paths in
        // TableGateway.Upsert.cs — batch upsert must match single-row upsert behavior.
        var overridesSystemIdentity = dialect.SupportsOverridingSystemValue && (_idColumn?.IsIdWritable == true);

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect, overridesSystemIdentity);

            // Append ON CONFLICT clause
            sc.Query.Append(" ON CONFLICT (");
            for (var i = 0; i < conflictCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(", ");
                }

                sc.Query.Append(dialect.WrapSimpleName(conflictCols[i].Name));
            }

            sc.Query.Append(") DO UPDATE SET ").Append(template.UpsertUpdateFragmentOnConflict);

            if (template.UpsertOnConflictVersionWhere != null)
            {
                sc.Query.Append(" ").Append(template.UpsertOnConflictVersionWhere);
            }

            result.Add(sc);
        }

        return result;
    }

    private IReadOnlyList<ISqlContainer> BuildBatchUpsertOnDuplicate(
        IReadOnlyList<TEntity> entities, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var insertableColumns = GetCachedInsertableColumns();
        var template = GetTemplatesForDialect(dialect);

        // Resolve audit values once for the whole batch (not once per entity)
        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

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
            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragmentOnConflict);
            result.Add(sc);
        }

        return result;
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

        for (var containerIndex = firstUnexecutedContainer; containerIndex < containers.Count; containerIndex++)
        {
            if (!_batchContainerEntities.TryGetValue(containers[containerIndex], out var chunk))
            {
                continue;
            }

            foreach (var entity in chunk)
            {
                for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
                {
                    if (ReferenceEquals(entities[entityIndex], entity))
                    {
                        RestoreAuditFields(entity, snapshots[entityIndex]);
                        break;
                    }
                }
            }
        }
    }
}
