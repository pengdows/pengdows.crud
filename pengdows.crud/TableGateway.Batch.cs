// =============================================================================
// FILE: TableGateway.Batch.cs
// PURPOSE: Batch INSERT and UPSERT operations with multi-row VALUES syntax.
//
// AI SUMMARY:
// - BuildBatchCreate() - Generates multi-row INSERT INTO t (cols) VALUES (...), (...)
// - BatchCreateAsync() - Executes batch insert, returns total affected rows
// - BuildBatchUpsert() - Generates dialect-specific batch upsert:
//   * PostgreSQL/CockroachDB: multi-row INSERT ... ON CONFLICT DO UPDATE
//   * MySQL/MariaDB: multi-row INSERT ... ON DUPLICATE KEY UPDATE
//   * SQL Server/Oracle/Firebird: falls back to individual BuildUpsert per entity
// - Auto-chunks based on dialect's MaxParameterLimit (with 10% headroom)
// - Sequential parameter naming via ClauseCounters.NextBatch() (b0, b1, b2, ...)
// - NULL values are inlined as NULL literal (no parameter consumed)
// - No RETURNING support for batch (too complex across databases)
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: Batch INSERT and UPSERT operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
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

        // Fallback for dialects that cannot execute EXECUTE BLOCK / multi-row INSERT with ADO.NET parameters
        if (!dialect.SupportsBatchInsert)
        {
            var fallback = new List<ISqlContainer>(entities.Count);
            foreach (var entity in entities)
            {
                fallback.Add(BuildCreate(entity, ctx));
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

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<int> BatchCreateAsync(
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

        // Single entity fast path
        if (entities.Count == 1)
        {
            var ctx = context ?? _context;
            var success = await CreateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
            return success ? 1 : 0;
        }

        var containers = BuildBatchCreate(entities, context);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
    }

    /// <inheritdoc/>
    public Task<int> CreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchCreateAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public Task<int> UpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpdateAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public Task<int> UpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
        => BatchUpsertAsync(entities, context, cancellationToken);

    /// <inheritdoc/>
    public async Task<int> BatchUpdateAsync(
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

        // Single entity fast path
        if (entities.Count == 1)
        {
            return await UpdateAsync(entities[0], context, cancellationToken).ConfigureAwait(false);
        }

        var containers = BuildBatchUpdate(entities, context);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        return totalAffected;
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
                fallback.Add(BuildUpdateAsync(entity, false, ctx).Result);
            }

            return fallback;
        }

        var updateableColumns = GetCachedUpdateableColumns();
        var keyColumns = _tableInfo.PrimaryKeys.Count > 0 ? _tableInfo.PrimaryKeys : (_idColumn != null ? new List<IColumnInfo> { _idColumn } : throw new InvalidOperationException("Batch update requires an [Id] or [PrimaryKey]."));

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

        // Chunking calculation: keyCols + updateableCols
        var totalParamsPerRow = keyColumns.Count + updateableColumns.Count;
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
                    else
                    {
                        colInfo = updateableColumns[col - keyColumns.Count];
                    }

                    return colInfo.MakeParameterValueFromField(entity);
                });

            // Value binding
            for (var row = 0; row < chunk.Count; row++)
            {
                var entity = chunk[row];
                // Bind Keys first, then Updateable columns (matching the getValue order above)
                foreach (var col in keyColumns)
                {
                    var val = col.MakeParameterValueFromField(entity);
                    if (val == null || val == DBNull.Value) continue;
                    sc.AddParameter(dialect.CreateDbParameter(counters.NextBatch(), col.DbType, val));
                }

                foreach (var col in updateableColumns)
                {
                    var val = col.MakeParameterValueFromField(entity);
                    if (val == null || val == DBNull.Value) continue;
                    sc.AddParameter(dialect.CreateDbParameter(counters.NextBatch(), col.DbType, val));
                }
            }

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
            if (!c.IsNonUpdateable && !c.IsId && !_tableInfo.PrimaryKeys.Contains(c))
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
            result.Add(BuildUpsert(entity, ctx));
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<int> BatchUpsertAsync(
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

        // Single entity fast path
        if (entities.Count == 1)
        {
            return await UpsertAsync(entities[0], context, cancellationToken).ConfigureAwait(false);
        }

        var containers = BuildBatchUpsert(entities, context);
        var totalAffected = 0;

        foreach (var sc in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalAffected += await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken)
                .ConfigureAwait(false);
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

        // Delegate structure to dialect (ANSI VALUES, Oracle INSERT ALL, etc.)
        dialect.BuildBatchInsertSql(wrappedTableName, wrappedColumnNames, chunk.Count, sc.Query, 
            (row, col) => insertableColumns[col].MakeParameterValueFromField(chunk[row]));

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

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect);

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

            sc.Query.Append(") DO UPDATE SET ").Append(template.UpsertUpdateFragment);
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

            // MySQL 8.0.20+: declare the row alias (AS incoming) between VALUES and ON DUPLICATE KEY UPDATE
            var incomingAlias = dialect.UpsertIncomingAlias;
            if (!string.IsNullOrEmpty(incomingAlias))
            {
                sc.Query.Append(" AS ").Append(incomingAlias);
            }

            // Append ON DUPLICATE KEY UPDATE clause
            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragment);
            result.Add(sc);
        }

        return result;
    }
}
