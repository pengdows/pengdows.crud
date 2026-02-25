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

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit);
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

        // Validate upsert key exists — same logic as ResolveUpsertKey
        var hasWritableId = _idColumn != null && _idColumn.IsIdIsWritable;
        if (!hasWritableId && _tableInfo.PrimaryKeys.Count == 0)
        {
            throw new NotSupportedException(UpsertNoKeyMessage);
        }

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

        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");

        // Column list (same for all rows)
        for (var c = 0; c < insertableColumns.Count; c++)
        {
            if (c > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapSimpleName(insertableColumns[c].Name));
        }

        sc.Query.Append(") VALUES ");

        // Value tuples for each entity
        for (var row = 0; row < chunk.Count; row++)
        {
            if (row > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append('(');
            var entity = chunk[row];

            for (var c = 0; c < insertableColumns.Count; c++)
            {
                if (c > 0)
                {
                    sc.Query.Append(", ");
                }

                var column = insertableColumns[c];
                var value = column.MakeParameterValueFromField(entity);

                if (Utils.IsNullOrDbNull(value))
                {
                    sc.Query.Append("NULL");
                }
                else
                {
                    var name = counters.NextBatch();
                    var p = dialect.CreateDbParameter(name, column.DbType, value);
                    if (column.IsJsonType)
                    {
                        dialect.TryMarkJsonParameter(p, column);
                    }

                    sc.AddParameter(p);
                    var marker = dialect.MakeParameterName(p);
                    if (column.IsJsonType)
                    {
                        marker = dialect.RenderJsonArgument(marker, column);
                    }

                    sc.Query.Append(marker);
                }
            }

            sc.Query.Append(')');
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

        // Resolve conflict key
        var keys = _tableInfo.PrimaryKeys;
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException(UpsertNoKeyMessage);
        }

        var conflictCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit);
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

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var sc = BuildBatchInsertContainer(chunk, insertableColumns, ctx, dialect);

            // Append ON DUPLICATE KEY UPDATE clause
            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragment);
            result.Add(sc);
        }

        return result;
    }
}