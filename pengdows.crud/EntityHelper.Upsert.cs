using System;
using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;
using pengdows.crud.enums;

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

        var conflictCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(columns[i]);
        }
        sc.Query.Append(") VALUES (");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(values[i]);
        }
        sc.Query.Append(") ON CONFLICT (");
        for (var i = 0; i < conflictCols.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapObjectName(conflictCols[i].Name));
        }
        sc.Query.Append(") DO UPDATE SET ")
            .Append(updateSet.ToString());

        sc.AddParameters(parameters);
        return sc;
    }

    private static string FormatFirebirdValueExpression(string placeholder, IColumnInfo column)
    {
        var typeName = GetFirebirdDataType(column);
        if (string.Equals(placeholder, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return $"CAST(NULL AS {typeName})";
        }

        return $"CAST({placeholder} AS {typeName})";
    }

    private static string GetFirebirdDataType(IColumnInfo column) =>
        column.DbType switch
        {
            DbType.Boolean => "SMALLINT",
            DbType.Byte => "SMALLINT",
            DbType.SByte => "SMALLINT",
            DbType.Int16 => "SMALLINT",
            DbType.UInt16 => "SMALLINT",
            DbType.Int32 => "INTEGER",
            DbType.UInt32 => "BIGINT",
            DbType.Int64 => "BIGINT",
            DbType.UInt64 => "BIGINT",
            DbType.Decimal => "DECIMAL(18,2)",
            DbType.Double => "DOUBLE PRECISION",
            DbType.Single => "DOUBLE PRECISION",
            DbType.Date => "DATE",
            DbType.DateTime => "TIMESTAMP",
            DbType.AnsiStringFixedLength => "VARCHAR(255)",
            DbType.AnsiString => "VARCHAR(255)",
            DbType.String => "VARCHAR(255)",
            DbType.StringFixedLength => "VARCHAR(255)",
            DbType.Guid => "CHAR(36)",
            DbType.Binary => "BLOB",
            _ => "VARCHAR(255)"
        };

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
                placeholder = FormatFirebirdValueExpression("NULL", column);
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
            .Append(" (");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(columns[i]);
        }
        sc.Query.Append(") VALUES (");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(values[i]);
        }
        sc.Query.Append(")");
        var incomingAlias = dialect.UpsertIncomingAlias;
        if (!string.IsNullOrEmpty(incomingAlias))
        {
            sc.Query.Append(" AS ").Append(dialect.WrapObjectName(incomingAlias));
        }
        sc.Query.Append(" ON DUPLICATE KEY UPDATE ")
            .Append(updateSet.ToString());

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        if (ctx.DataSourceInfo.Product == SupportedDatabase.Firebird)
        {
            return BuildFirebirdMergeUpsert(entity, ctx);
        }

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

        var insertColumns = new List<string>();
        foreach (var column in GetCachedInsertableColumns())
        {
            insertColumns.Add(dialect.WrapObjectName(column.Name));
        }

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

        var joinCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };
        var join = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        for (var i = 0; i < joinCols.Count; i++)
        {
            if (i > 0)
            {
                join.Append(" AND ");
            }

            join.Append("t.");
            join.Append(dialect.WrapObjectName(joinCols[i].Name));
            join.Append(" = s.");
            join.Append(dialect.WrapObjectName(joinCols[i].Name));
        }

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("MERGE INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" t USING (VALUES (")
            ;
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(values[i]);
        }
        sc.Query.Append(")) AS s (");
        for (var i = 0; i < srcColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(srcColumns[i]);
        }
        sc.Query.Append(") ON ")
            .Append(join.ToString())
            .Append(" WHEN MATCHED THEN UPDATE SET ")
            .Append(updateSet.ToString())
            .Append(" WHEN NOT MATCHED THEN INSERT (")
            ;
        for (var i = 0; i < insertColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(insertColumns[i]);
        }
        sc.Query.Append(") VALUES (");
        for (var i = 0; i < insertColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append("s.");
            sc.Query.Append(insertColumns[i]);
        }
        sc.Query.Append(");");

        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildFirebirdMergeUpsert(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        PrepareForInsertOrUpsert(entity);

        var values = new List<string>();
        var parameters = new List<DbParameter>();
        var counters = new ClauseCounters();

        var insertColumns = new List<string>();
        foreach (var column in GetCachedInsertableColumns())
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

            insertColumns.Add(dialect.WrapObjectName(column.Name));
            values.Add(placeholder);
        }

        var keys = _tableInfo.PrimaryKeys;
        if (_idColumn == null && keys.Count == 0)
        {
            throw new NotSupportedException("Upsert requires an Id or a composite primary key.");
        }

        var joinCols = keys.Count > 0 ? keys : new List<IColumnInfo> { _idColumn! };

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("UPDATE OR INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (");
        for (var i = 0; i < insertColumns.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(insertColumns[i]);
        }
        sc.Query.Append(") VALUES (");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(values[i]);
        }
        sc.Query.Append(") MATCHING (");
        for (var i = 0; i < joinCols.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(", ");
            }

            sc.Query.Append(dialect.WrapObjectName(joinCols[i].Name));
        }
        sc.Query.Append(");");

        sc.AddParameters(parameters);
        return sc;
    }
}
