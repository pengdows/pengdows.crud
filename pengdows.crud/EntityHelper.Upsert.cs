using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    public Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context = null)
    {
        return UpsertAsync(entity, context, CancellationToken.None);
    }

    public async Task<int> UpsertAsync(TEntity entity, IDatabaseContext? context, CancellationToken cancellationToken)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        // BuildUpsert creates a dynamic container - proper disposal required to avoid resource leaks
        // Use async disposal for async operations
        await using var sc = BuildUpsert(entity, ctx);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    public ISqlContainer BuildUpsert(TEntity entity, IDatabaseContext? context = null)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var ctx = context ?? _context;
        if (_idColumn == null && _tableInfo.PrimaryKeys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        if (ctx.DataSourceInfo.IsUsingFallbackDialect)
        {
            throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
        }

        if (ctx.DataSourceInfo.SupportsMerge)
        {
            return BuildUpsertMerge(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsInsertOnConflict)
        {
            return BuildUpsertOnConflict(entity, ctx);
        }

        if (ctx.DataSourceInfo.SupportsOnDuplicateKey)
        {
            return BuildUpsertOnDuplicate(entity, ctx);
        }

        throw new NotSupportedException($"Upsert not supported for {ctx.Product}");
    }

    private IReadOnlyList<IColumnInfo> ResolveUpsertKey()
    {
        if (_idColumn != null && _idColumn.IsIdIsWritable)
        {
            return new List<IColumnInfo> { _idColumn! };
        }

        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count > 0)
        {
            return keys;
        }

        throw new NotSupportedException("Upsert requires client-assigned Id or [PrimaryKey] attributes.");
    }

    private void PrepareForInsertOrUpsert(TEntity e)
    {
        if (_auditValueResolver != null)
        {
            SetAuditFields(e, updateOnly: false);
        }
        if (_versionColumn == null || _versionColumn.PropertyInfo.PropertyType == typeof(byte[]))
        {
            return;
        }

        var v = _versionColumn.PropertyInfo.GetValue(e);
        if (v == null || Utils.IsZeroNumeric(v))
        {
            var t = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                    _versionColumn.PropertyInfo.PropertyType;
            _versionColumn.PropertyInfo.SetValue(e, Convert.ChangeType(1, t));
        }
    }

    private ISqlContainer BuildUpsertOnConflict(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();
        var counters = new ClauseCounters();

        foreach (var column in GetCachedInsertableColumns())
        {
            var value = column.MakeParameterValueFromField(entity);

            if (_auditValueResolver == null && (column.IsCreatedBy || column.IsLastUpdatedBy) && Utils.IsNullOrDbNull(value))
            {
                continue;
            }

            columns.Add(dialect.WrapObjectName(column.Name));
            if (Utils.IsNullOrDbNull(value))
            {
                values.Add("NULL");
            }
            else
            {
                var name = counters.NextIns();
                var p = dialect.CreateDbParameter(name, column.DbType, value);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(p, column);
                }
                parameters.Add(p);
                var marker = dialect.MakeParameterName(p);
                if (column.IsJsonType)
                {
                    marker = dialect.RenderJsonArgument(marker, column);
                }

                values.Add(marker);
            }
        }

        var updateSet = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        foreach (var column in GetCachedUpdatableColumns())
        {
            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"{dialect.WrapObjectName(column.Name)} = {dialect.UpsertIncomingColumn(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append($", {dialect.WrapObjectName(_versionColumn.Name)} = {dialect.WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.PrimaryKeys;
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var conflictCols = (keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! })
            .Select(k => dialect.WrapObjectName(k.Name));

        var conflictTarget = string.Join(", ", conflictCols);

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON CONFLICT (")
            .Append(conflictTarget)
            .Append(") DO UPDATE SET ")
            .Append(updateSet.ToString());

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertOnDuplicate(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var columns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();
        var counters = new ClauseCounters();

        foreach (var column in _tableInfo.OrderedColumns)
        {
            var value = column.MakeParameterValueFromField(entity);
            string placeholder;
            if (Utils.IsNullOrDbNull(value))
            {
                placeholder = "NULL";
            }
            else
            {
                var name = counters.NextIns();
                var p = dialect.CreateDbParameter(name, column.DbType, value);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(p, column);
                }
                parameters.Add(p);
                var marker = dialect.MakeParameterName(p);
                if (column.IsJsonType)
                {
                    marker = dialect.RenderJsonArgument(marker, column);
                }

                placeholder = marker;
            }

            columns.Add(dialect.WrapObjectName(column.Name));
            values.Add(placeholder);
        }

        var updateSet = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        foreach (var column in GetCachedUpdatableColumns())
        {
            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"{dialect.WrapObjectName(column.Name)} = {dialect.UpsertIncomingColumn(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append($", {dialect.WrapObjectName(_versionColumn.Name)} = {dialect.WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.PrimaryKeys;
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON DUPLICATE KEY UPDATE ")
            .Append(updateSet.ToString());

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var srcColumns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();
        var counters = new ClauseCounters();

        foreach (var column in _tableInfo.OrderedColumns)
        {
            var value = column.MakeParameterValueFromField(entity);
            string placeholder;
            if (Utils.IsNullOrDbNull(value))
            {
                placeholder = "NULL";
            }
            else
            {
                var name = counters.NextIns();
                var p = dialect.CreateDbParameter(name, column.DbType, value);
                if (column.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(p, column);
                }
                parameters.Add(p);
                var marker = dialect.MakeParameterName(p);
                if (column.IsJsonType)
                {
                    marker = dialect.RenderJsonArgument(marker, column);
                }

                placeholder = marker;
            }

            srcColumns.Add(dialect.WrapObjectName(column.Name));
            values.Add(placeholder);
        }

        var insertColumns = GetCachedInsertableColumns()
            .Select(c => dialect.WrapObjectName(c.Name))
            .ToList();

        var updateSet = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        // PostgreSQL MERGE does not allow target alias on left side of UPDATE SET
        // SQL Server/Oracle do allow it
        var targetPrefix = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";

        foreach (var column in GetCachedUpdatableColumns())
        {
            if (_auditValueResolver == null && column.IsLastUpdatedBy)
            {
                continue;
            }

            if (updateSet.Length > 0)
            {
                updateSet.Append(", ");
            }

            updateSet.Append($"{targetPrefix}{dialect.WrapObjectName(column.Name)} = s.{dialect.WrapObjectName(column.Name)}");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            updateSet.Append(
                $", {targetPrefix}{dialect.WrapObjectName(_versionColumn.Name)} = {targetPrefix}{dialect.WrapObjectName(_versionColumn.Name)} + 1");
        }

        var keys = _tableInfo.PrimaryKeys;
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var join = string.Join(" AND ", (keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! })
            .Select(k => $"t.{dialect.WrapObjectName(k.Name)} = s.{dialect.WrapObjectName(k.Name)}"));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("MERGE INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" t USING (VALUES (")
            .Append(string.Join(", ", values))
            .Append(")) AS s (")
            .Append(string.Join(", ", srcColumns))
            .Append(") ON ")
            .Append(join)
            .Append(" WHEN MATCHED THEN UPDATE SET ")
            .Append(updateSet.ToString())
            .Append(" WHEN NOT MATCHED THEN INSERT (")
            .Append(string.Join(", ", insertColumns))
            .Append(") VALUES (")
            .Append(string.Join(", ", insertColumns.Select(c => "s." + c)))
            .Append(");");

        sc.AddParameters(parameters);
        return sc;
    }
}
