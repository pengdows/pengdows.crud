using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    public ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

        var sc = ctx.CreateSqlContainer();

        // Build SQL directly for this dialect, cached per alias + product
        var cacheKey = ctx.Product == _context.Product
            ? $"BaseRetrieve:{alias}"
            : $"BaseRetrieve:{alias}:{ctx.Product}";

        var sql = GetCachedQuery(cacheKey, () =>
        {
            var hasAlias = !string.IsNullOrWhiteSpace(alias);
            var selectList = _tableInfo.OrderedColumns
                .Select(col => (hasAlias
                    ? dialect.WrapObjectName(alias) + dialect.CompositeIdentifierSeparator
                    : string.Empty) + dialect.WrapObjectName(col.Name));
            var sb = new StringBuilder();
            sb.Append("SELECT ")
                .Append(string.Join(", ", selectList))
                .Append("\nFROM ")
                .Append(BuildWrappedTableName(dialect));
            if (hasAlias)
            {
                sb.Append(' ').Append(dialect.WrapObjectName(alias));
            }

            return sb.ToString();
        });

        sc.Query.Append(sql);
        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds,
        string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        if (_idColumn == null)
        {
            throw new InvalidOperationException(
                "Single-ID operations require a designated Id column; use composite-key helpers.");
        }
        var sc = BuildBaseRetrieve(alias, ctx);
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
        {
            wrappedAlias = sc.WrapObjectName(alias) + sc.CompositeIdentifierSeparator;
        }

        var wrappedColumnName = wrappedAlias + sc.WrapObjectName(_idColumn.Name);

        if (listOfIds == null || listOfIds.Count == 0)
        {
            throw new ArgumentException("IDs cannot be null or empty.", nameof(listOfIds));
        }

        if (listOfIds.Any(id => Utils.IsNullOrDbNull(id)))
        {
            throw new ArgumentException("IDs cannot be null", nameof(listOfIds));
        }

        BuildWhere(
            wrappedColumnName,
            listOfIds,
            sc
        );

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var sc = BuildBaseRetrieve(alias, ctx);
        BuildWhereByPrimaryKey(
            listOfObjects,
            sc,
            alias,
            dialect);

        return sc;
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfIds, "", context);
    }

    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfObjects, string.Empty, context);
    }

    public void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias = "")
    {
        var dialectProvider = sc as ISqlDialectProvider;
        var dialect = dialectProvider?.Dialect ?? _dialect;
        BuildWhereByPrimaryKey(listOfObjects, sc, alias, dialect);
    }

    public void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias, ISqlDialect dialect)
    {
        ValidateWhereInputs(listOfObjects, sc);

        var keys = GetPrimaryKeys();
        CheckParameterLimit(sc, listOfObjects!.Count * keys.Count);

        var parameters = new List<DbParameter>();
        var wrappedAlias = BuildAliasPrefix(alias);
        var sb = new StringBuilder();
        var counters = new ClauseCounters();
        var index = 0;

        foreach (var entity in listOfObjects!)
        {
            if (index++ > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append(BuildPrimaryKeyClause(entity, keys, wrappedAlias, parameters, dialect, counters));
        }

        if (sb.Length == 0)
        {
            return;
        }

        sc.AddParameters(parameters);
        AppendWherePrefix(sc);
        sc.Query.Append(sb);
    }

    private void ValidateWhereInputs(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer _)
    {
        if (listOfObjects == null || listOfObjects.Count == 0)
        {
            throw new ArgumentException("List of objects cannot be null or empty.", nameof(listOfObjects));
        }
    }

    private IReadOnlyList<IColumnInfo> GetPrimaryKeys()
    {
        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count < 1)
        {
            throw new Exception($"No primary keys found for type {typeof(TEntity).Name}");
        }

        return keys;
    }

    private static string BuildAliasPrefix(string alias) =>
        string.IsNullOrWhiteSpace(alias) ? string.Empty : alias + ".";

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias,
        List<DbParameter> parameters, ISqlDialect dialect, ClauseCounters counters)
    {
        var clause = new StringBuilder("(");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                clause.Append(" AND ");
            }

            var pk = keys[i];
            var value = pk.MakeParameterValueFromField(entity);
            var name = counters.NextKey();
            var parameter = dialect.CreateDbParameter(name, pk.DbType, value);

            clause.Append(alias);
            clause.Append(dialect.WrapObjectName(pk.Name));

            if (Utils.IsNullOrDbNull(value))
            {
                clause.Append(" IS NULL");
            }
            else
            {
                clause.Append(" = ");
                clause.Append(dialect.MakeParameterName(parameter));
                parameters.Add(parameter);
            }
        }

        clause.Append(')');
        return clause.ToString();
    }

    private void AppendWherePrefix(ISqlContainer sc)
    {
        if (!sc.HasWhereAppended)
        {
            sc.Query.Append("\n WHERE ");
            sc.HasWhereAppended = true;
        }
        else
        {
            sc.Query.Append("\n AND ");
        }
    }

    public ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sqlContainer)
    {
        var list = ids?.Distinct().ToList();
        if (list is null || list.Count == 0)
        {
            return sqlContainer;
        }

        var dialect = ((ISqlDialectProvider)sqlContainer).Dialect;

        CheckParameterLimit(sqlContainer, dialect.SupportsSetValuedParameters ? 1 : list.Count);

        if (list.Any(Utils.IsNullOrDbNull))
        {
            throw new ArgumentException("IDs cannot be null", nameof(ids));
        }

        if (dialect.SupportsSetValuedParameters)
        {
            var paramName = sqlContainer.MakeParameterName("w0");
            var anySql = GetCachedQuery($"WhereAny:{wrappedColumnName}",
                () => string.Concat(wrappedColumnName, " = ANY(", paramName, ")"));

            AppendWherePrefix(sqlContainer);
            sqlContainer.Query.Append(anySql);

            var parameter = sqlContainer.CreateDbParameter(paramName, DbType.Object, list.ToArray());
            sqlContainer.AddParameter(parameter);

            return sqlContainer;
        }

        var bucket = 1;
        for (; bucket < list.Count; bucket <<= 1)
        {
        }

        var key = $"Where:{wrappedColumnName}:{bucket}";
        if (!_whereParameterNames.TryGet(key, out var names))
        {
            names = new string[bucket];
            for (var i = 0; i < bucket; i++)
            {
                names[i] = sqlContainer.MakeParameterName($"w{i}");
            }

            _whereParameterNames.GetOrAdd(key, _ => names);
        }

        var sql = GetCachedQuery(key,
            () => string.Concat(wrappedColumnName, " IN (", string.Join(", ", names), ")"));

        AppendWherePrefix(sqlContainer);
        sqlContainer.Query.Append(sql);

        var dbType = _idColumn!.DbType;
        var isPositional = sqlContainer.MakeParameterName("w0") == sqlContainer.MakeParameterName("w1");
        var lastIndex = list.Count - 1;
        for (var i = 0; i < bucket; i++)
        {
            var name = names[i];
            var value = i < list.Count ? list[i] : list[lastIndex];

            if (isPositional)
            {
                var parameter = sqlContainer.CreateDbParameter(name, dbType, value);
                sqlContainer.AddParameter(parameter);
                continue;
            }

            try
            {
                sqlContainer.SetParameterValue(name, value);
            }
            catch (KeyNotFoundException)
            {
                var parameter = sqlContainer.CreateDbParameter(name, dbType, value);
                sqlContainer.AddParameter(parameter);
            }
        }

        return sqlContainer;
    }

}
