// =============================================================================
// FILE: PrimaryKeyTableGateway.Upsert.cs
// PURPOSE: UPSERT operations keyed on [PrimaryKey] columns.
//          Provider-specific: MERGE (SQL Server/Oracle/Firebird), ON CONFLICT (PostgreSQL),
//          ON DUPLICATE KEY UPDATE (MySQL/MariaDB).
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
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

        if (ctx.DataSourceInfo.IsUsingFallbackDialect)
        {
            throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
        }

        // Firebird uses UPDATE OR INSERT MATCHING which has no UPDATE SET clause — allowed for pure-PK entities.
        // All other dialects require at least one non-PK updateable column.
        if (ctx.DataSourceInfo.Product != SupportedDatabase.Firebird)
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
    public async Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        await using var sc = BuildUpsert(entity, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
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

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return BuildPkBatchUpsertOnDuplicate(entities, ctx);
        }

        // Fallback: one-by-one (MERGE or unsupported — BuildUpsert handles its own guard)
        var result = new List<ISqlContainer>(entities.Count);
        foreach (var entity in entities)
        {
            result.Add(BuildUpsert(entity, ctx));
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<int> BatchUpsertAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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

        if (entities.Count == 1)
        {
            return await UpsertAsync(entities[0], context, cancellationToken).ConfigureAwait(false);
        }

        var containers = BuildBatchUpsert(entities, context);
        var total = 0;
        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            total += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
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

        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.MakeParameterValueFromField(entity);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(entity, TypeCoercionHelper.ConvertWithCache(1, t));
        }
    }

    private void PrepareForPkUpsert(TEntity entity, IAuditValues? cachedAuditValues)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(entity, false, cachedAuditValues);
        }

        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.MakeParameterValueFromField(entity);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(entity, TypeCoercionHelper.ConvertWithCache(1, t));
        }
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

        sc.Query.Append(") DO UPDATE SET ").Append(template.UpsertUpdateFragment);

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

        sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragment);

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildPkUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        if (context.DataSourceInfo.Product == SupportedDatabase.Firebird)
        {
            return BuildPkFirebirdMergeUpsert(entity, context);
        }

        var dialect = GetDialect(context);
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
            if (col.IsJsonType)
            {
                dialect.TryMarkJsonParameter(param, col);
            }

            parameters.Add(param);
        }

        var mergeSource = dialect.RenderMergeSource(insertableColumns.ToList(), paramNames);

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
                .Append(" WHEN MATCHED THEN UPDATE SET ")
                .Append(template.UpsertUpdateFragment)
                .Append(" WHEN NOT MATCHED THEN INSERT (")
                .Append(insertColSb.AsSpan())
                .Append(") VALUES (")
                .Append(insertValSb.AsSpan())
                .Append(");");

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
                if (col.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(param, col);
                    valSb.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(pName), col));
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

            sc.Query.Append(") DO UPDATE SET ").Append(template.UpsertUpdateFragment);
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
                sc.Query.Append(" AS ").Append(incomingAlias);
            }

            sc.Query.Append(" ON DUPLICATE KEY UPDATE ").Append(template.UpsertUpdateFragment);
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

        dialect.BuildBatchInsertSql(wrappedTableName, wrappedColumnNames, chunk.Count, sc.Query,
            (row, col) => insertableColumns[col].MakeParameterValueFromField(chunk[row]));

        for (var row = 0; row < chunk.Count; row++)
        {
            var entity = chunk[row];
            for (var c = 0; c < insertableColumns.Count; c++)
            {
                var column = insertableColumns[c];
                var value = column.MakeParameterValueFromField(entity);

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
}
