// =============================================================================
// FILE: TableGateway.Retrieve.cs
// PURPOSE: SELECT query building and entity retrieval operations.
//
// AI SUMMARY:
// - BuildBaseRetrieve() - Creates SELECT with all columns, no WHERE clause.
// - BuildRetrieve() - SELECT with WHERE id IN (...) clause.
// - RetrieveAsync() - Loads multiple entities by their row IDs.
// - RetrieveOneAsync(TRowID) - Loads single entity by row ID.
// - RetrieveOneAsync(TEntity) - Loads entity by primary key values.
// - LoadListAsync() - Executes query and maps all rows to entities.
// - LoadSingleAsync() - Executes query and maps first row or null.
// - SQL caching by alias and database product for performance.
// - Uses dialect-specific identifier quoting and parameter formatting.
// - Primary key lookup uses [PrimaryKey] columns, not [Id].
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: SELECT query building and retrieval operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    /// <inheritdoc/>
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
            var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            sb.Append("SELECT ");
            for (var i = 0; i < _tableInfo.OrderedColumns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                if (hasAlias)
                {
                    sb.Append(dialect.WrapObjectName(alias));
                    sb.Append(dialect.CompositeIdentifierSeparator);
                }

                sb.Append(dialect.WrapObjectName(_tableInfo.OrderedColumns[i].Name));
            }

            sb.Append("\nFROM ");
            sb.Append(BuildWrappedTableName(dialect));
            if (hasAlias)
            {
                sb.Append(' ');
                sb.Append(dialect.WrapObjectName(alias));
            }

            return sb.ToString();
        });

        sc.Query.Append(sql);
        return sc;
    }

    /// <inheritdoc/>
    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds,
        string alias, IDatabaseContext? context = null)
    {
        return BuildRetrieveInternal(listOfIds, alias, context, true);
    }

    internal ISqlContainer BuildRetrieveInternal(IReadOnlyCollection<TRowID>? listOfIds,
        string alias, IDatabaseContext? context, bool deduplicate)
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

        BuildWhereInternal(
            wrappedColumnName,
            listOfIds,
            sc,
            deduplicate
        );

        return sc;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfIds, "", context);
    }

    /// <inheritdoc/>
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

    public void BuildWhereByPrimaryKey(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer sc, string alias,
        ISqlDialect dialect)
    {
        ValidateWhereInputs(listOfObjects, sc);

        var keys = GetPrimaryKeys();
        CheckParameterLimit(sc, listOfObjects!.Count * keys.Count);

        var parameters = new List<DbParameter>(listOfObjects.Count * keys.Count);
        var wrappedAlias = BuildAliasPrefix(alias, dialect);
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        var counters = new ClauseCounters();
        var index = 0;

        foreach (var entity in listOfObjects!)
        {
            if (index++ > 0)
            {
                sb.Append(SqlFragments.Or);
            }

            sb.Append(BuildPrimaryKeyClause(entity, keys, wrappedAlias, parameters, dialect, counters));
        }

        if (sb.Length == 0)
        {
            return;
        }

        sc.AddParameters(parameters);
        AppendWherePrefix(sc);
        sc.Query.Append(sb.ToString());
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

    private static string BuildAliasPrefix(string alias, ISqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        return dialect.WrapObjectName(alias) + ".";
    }

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias,
        List<DbParameter> parameters, ISqlDialect dialect, ClauseCounters counters)
    {
        var clause = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        clause.Append('(');
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                clause.Append(SqlFragments.And);
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
            sc.Query.Append('\n');
            sc.Query.Append(SqlFragments.Where);
            sc.HasWhereAppended = true;
        }
        else
        {
            sc.Query.Append('\n');
            sc.Query.Append(SqlFragments.And);
        }
    }

    public ISqlContainer BuildWhere(string wrappedColumnName, IEnumerable<TRowID> ids, ISqlContainer sqlContainer)
    {
        return BuildWhereInternal(wrappedColumnName, ids, sqlContainer, true);
    }

    internal ISqlContainer BuildWhereInternal(string wrappedColumnName, IEnumerable<TRowID> ids,
        ISqlContainer sqlContainer, bool deduplicate)
    {
        if (ids is null)
        {
            return sqlContainer;
        }

        // Build a non-null list and allow at most one NULL (rendered as IS NULL)
        var nonNullIds = new List<TRowID>();
        var seen = deduplicate ? new HashSet<TRowID?>() : null;
        var hasNull = false;
        foreach (var id in ids)
        {
            if (Utils.IsNullOrDbNull(id))
            {
                hasNull = true;
                continue;
            }

            if (!deduplicate)
            {
                nonNullIds.Add(id);
                continue;
            }

            if (seen!.Add(id))
            {
                nonNullIds.Add(id);
            }
        }

        if (nonNullIds.Count == 0 && !hasNull)
        {
            return sqlContainer;
        }

        var dialect = ((ISqlDialectProvider)sqlContainer).Dialect;
        CheckParameterLimit(sqlContainer, dialect.SupportsSetValuedParameters ? 1 : nonNullIds.Count);

        // Only NULL provided
        if (nonNullIds.Count == 0 && hasNull)
        {
            AppendWherePrefix(sqlContainer);
            sqlContainer.Query.Append(wrappedColumnName).Append(" IS NULL");
            return sqlContainer;
        }

        // Single non-null value = equality (optionally OR IS NULL when hasNull)
        if (nonNullIds.Count == 1)
        {
            var paramName = sqlContainer.MakeParameterName("p0");
            var product = (((ISqlDialectProvider)sqlContainer).Dialect as SqlDialect)?.DatabaseType
                          ?? SupportedDatabase.Unknown;
            var key = $"WhereEquals:{wrappedColumnName}:{product}{(hasNull ? ":Null" : string.Empty)}";
            var equalityCore = GetCachedQuery(key, () => string.Concat(wrappedColumnName, " = ", paramName));
            _queryCache.GetOrAdd($"WhereQuery:{wrappedColumnName}:1", _ => equalityCore);

            AppendWherePrefix(sqlContainer);
            if (hasNull)
            {
                sqlContainer.Query.Append('(')
                    .Append(equalityCore)
                    .Append(SqlFragments.Or)
                    .Append(wrappedColumnName)
                    .Append(" IS NULL)");
            }
            else
            {
                sqlContainer.Query.Append(equalityCore);
            }

            var parameter = sqlContainer.CreateDbParameter(paramName, _idColumn!.DbType, nonNullIds[0]);
            sqlContainer.AddParameter(parameter);
            return sqlContainer;
        }

        // Set-valued parameters (ANY)
        if (dialect.SupportsSetValuedParameters)
        {
            var paramName = sqlContainer.MakeParameterName("w0");
            var anyCore = GetCachedQuery($"WhereAny:{wrappedColumnName}",
                () => string.Concat(wrappedColumnName, " = ANY(", paramName, ")"));

            AppendWherePrefix(sqlContainer);
            if (hasNull)
            {
                sqlContainer.Query.Append('(')
                    .Append(anyCore)
                    .Append(SqlFragments.Or)
                    .Append(wrappedColumnName)
                    .Append(" IS NULL)");
            }
            else
            {
                sqlContainer.Query.Append(anyCore);
            }

            var parameter = sqlContainer.CreateDbParameter(paramName, DbType.Object, nonNullIds.ToArray());
            sqlContainer.AddParameter(parameter);
            return sqlContainer;
        }

        // IN-list with bucketing (round up to power of 2 for cache efficiency)
        var bucket = nonNullIds.Count <= 1
            ? 1
            : (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)nonNullIds.Count);

        // Cache parameter names by bucket size only (not column name) since names are position-based
        // This improves cache hit rate - w0, w1, w2 are reused across all columns
        var paramNamesKey = $"WhereParams:{bucket}";
        if (!_whereParameterNames.TryGet(paramNamesKey, out var names))
        {
            names = new string[bucket];
            for (var i = 0; i < bucket; i++)
            {
                names[i] = sqlContainer.MakeParameterName($"w{i}");
            }

            _whereParameterNames.GetOrAdd(paramNamesKey, _ => names);
        }

        // Query cache must include column name since it generates "columnName IN (w0, w1, w2)"
        var queryKey = $"WhereQuery:{wrappedColumnName}:{bucket}";
        var inCore = GetCachedQuery(queryKey, () =>
        {
            var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            sb.Append(wrappedColumnName);
            sb.Append(SqlFragments.In);
            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(SqlFragments.Comma);
                }

                sb.Append(names[i]);
            }

            sb.Append(SqlFragments.CloseParen);
            return sb.ToString();
        });

        AppendWherePrefix(sqlContainer);
        if (hasNull)
        {
            sqlContainer.Query.Append('(')
                .Append(inCore)
                .Append(SqlFragments.Or)
                .Append(wrappedColumnName)
                .Append(" IS NULL)");
        }
        else
        {
            sqlContainer.Query.Append(inCore);
        }

        var dbType = _idColumn!.DbType;
        var isPositional = sqlContainer.MakeParameterName("w0") == sqlContainer.MakeParameterName("w1");
        var lastIndex = nonNullIds.Count - 1;
        for (var i = 0; i < bucket; i++)
        {
            var name = names[i];
            var value = i < nonNullIds.Count ? nonNullIds[i] : nonNullIds[lastIndex];

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
