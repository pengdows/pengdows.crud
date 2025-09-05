using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using pengdows.crud.@internal;
using pengdows.crud.dialects;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
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

    private (string sql, List<DbParameter> parameters) BuildUpdateByKey(TEntity updated,
        IReadOnlyList<IColumnInfo> keyCols, ISqlDialect dialect)
    {
        if (_hasAuditColumns)
        {
            SetAuditFields(updated, true);
        }
        var counters = new ClauseCounters();
        var (setClause, parameters) = BuildSetClause(updated, null, dialect, counters);
        if (setClause.Length == 0)
        {
            throw new InvalidOperationException("No changes detected for update.");
        }

        if (_versionColumn != null && _versionColumn.PropertyInfo.PropertyType != typeof(byte[]))
        {
            IncrementVersion(setClause, dialect);
        }

        var where = new StringBuilder();
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0)
            {
                where.Append(" AND ");
            }

            var key = keyCols[i];
            var v = key.MakeParameterValueFromField(updated);
            var name = counters.NextKey();
            var p = dialect.CreateDbParameter(name, key.DbType, v);
            parameters.Add(p);
            where.Append($"{dialect.WrapObjectName(key.Name)} = {dialect.MakeParameterName(p)}");
        }

        var sql = $"UPDATE {BuildWrappedTableName(dialect)} SET {setClause} WHERE {where}";
        if (_versionColumn != null)
        {
            var vv = _versionColumn.MakeParameterValueFromField(updated);
            var name = counters.NextVer();
            var p = dialect.CreateDbParameter(name, _versionColumn.DbType, vv);
            parameters.Add(p);
            sql += $" AND {dialect.WrapObjectName(_versionColumn.Name)} = {dialect.MakeParameterName(p)}";
        }

        return (sql, parameters);
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
            var name = counters.NextIns();
            var param = dialect.CreateDbParameter(name, column.DbType, value);
            parameters.Add(param);
            columns.Add(dialect.WrapObjectName(column.Name));
            values.Add(dialect.MakeParameterName(param));
        }

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON CONFLICT (");

        var keyCols = ResolveUpsertKey();
        for (var i = 0; i < keyCols.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(dialect.WrapObjectName(keyCols[i].Name));
        }

        var (updateSql, updateParams) = BuildUpdateByKey(entity, keyCols, dialect);
        parameters.AddRange(updateParams);
        sb.Append(") DO UPDATE SET ")
            .Append(updateSql.Substring(updateSql.IndexOf(" SET ", StringComparison.Ordinal) + 5));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append(sb);
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

        foreach (var column in GetCachedInsertableColumns())
        {
            var value = column.MakeParameterValueFromField(entity);
            var name = counters.NextIns();
            var param = dialect.CreateDbParameter(name, column.DbType, value);
            parameters.Add(param);
            columns.Add(dialect.WrapObjectName(column.Name));
            values.Add(dialect.MakeParameterName(param));
        }

        var sb = new StringBuilder();
        sb.Append("INSERT INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" (")
            .Append(string.Join(", ", columns))
            .Append(") VALUES (")
            .Append(string.Join(", ", values))
            .Append(") ON DUPLICATE KEY UPDATE ");

        var (updateSql, updateParams) = BuildUpdateByKey(entity, ResolveUpsertKey(), dialect);
        parameters.AddRange(updateParams);
        sb.Append(updateSql.Substring(updateSql.IndexOf(" SET ", StringComparison.Ordinal) + 5));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append(sb);
        sc.AddParameters(parameters);
        return sc;
    }

    private ISqlContainer BuildUpsertMerge(TEntity entity, IDatabaseContext context)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        PrepareForInsertOrUpsert(entity);

        var insertColumns = new List<string>();
        var values = new List<string>();
        var parameters = new List<DbParameter>();
        var updateSet = new StringBuilder();
        var srcColumns = new List<string>();
        var counters = new ClauseCounters();

        foreach (var column in GetCachedInsertableColumns())
        {
            var value = column.MakeParameterValueFromField(entity);
            var name = counters.NextIns();
            var param = dialect.CreateDbParameter(name, column.DbType, value);
            parameters.Add(param);
            insertColumns.Add(dialect.WrapObjectName(column.Name));
            values.Add(dialect.MakeParameterName(param));
            srcColumns.Add(dialect.WrapObjectName(column.Name));
        }

        var keyCols = ResolveUpsertKey();
        var (updateSql, updateParams) = BuildUpdateByKey(entity, keyCols, dialect);
        parameters.AddRange(updateParams);
        var setStart = updateSql.IndexOf(" SET ", StringComparison.Ordinal) + 5;
        var whereStart = updateSql.IndexOf(" WHERE ", StringComparison.Ordinal);
        var setClause = whereStart > setStart
            ? updateSql.Substring(setStart, whereStart - setStart)
            : updateSql.Substring(setStart);
        var setParts = setClause.Split(", ");
        for (var i = 0; i < setParts.Length; i++)
        {
            if (i > 0)
            {
                updateSet.Append(", ");
            }
            var part = setParts[i];
            var eq = part.IndexOf('=');
            var left = part.Substring(0, eq).Trim();
            var right = part.Substring(eq + 1).Trim();
            var colName = left.Trim().Trim('"');
            var wrapped = dialect.WrapObjectName(colName);
            updateSet.Append("t.").Append(left).Append(" = ")
                     .Append(right.Replace(wrapped, "t." + wrapped));
        }

        var join = string.Join(" AND ", (keyCols.Count > 0 ? keyCols : new List<IColumnInfo> { _idColumn! })
            .Select(k => $"t.{dialect.WrapObjectName(k.Name)} = s.{dialect.WrapObjectName(k.Name)}"));

        var sc = ctx.CreateSqlContainer();
        sc.Query.Append("MERGE INTO ")
            .Append(BuildWrappedTableName(dialect))
            .Append(" AS t USING (VALUES (")
            .Append(string.Join(", ", values))
            .Append(")")
            .Append(" AS s (")
            .Append(string.Join(", ", srcColumns))
            .Append(") ON ")
            .Append(join)
            .Append(" WHEN MATCHED THEN UPDATE SET ")
            .Append(updateSet)
            .Append(" WHEN NOT MATCHED THEN INSERT (")
            .Append(string.Join(", ", insertColumns))
            .Append(") VALUES (")
            .Append(string.Join(", ", insertColumns.Select(c => "s." + c)))
            .Append(");");

        sc.AddParameters(parameters);
        return sc;
    }
}

