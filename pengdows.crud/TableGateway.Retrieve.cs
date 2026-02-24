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

        // Fast path: clone from cached template for alias "a" (the most common alias)
        if (alias == "a")
        {
            var containerTemplates = GetContainerTemplatesForDialect(dialect, ctx);
            return containerTemplates.BaseRetrieveTemplate.Clone(ctx);
        }

        var sc = ctx.CreateSqlContainer();

        var brCache = GetOrCreateQueryCache(dialect);
        var brKey = $"BaseRetrieve:{alias}";
        if (!brCache.TryGet(brKey, out var sql))
        {
            sql = BuildBaseRetrieveSql(alias, dialect);
            brCache.GetOrAdd(brKey, _ => sql);
        }

        sc.Query.Append(sql);
        return sc;
    }

    /// <summary>
    /// Builds base SELECT SQL directly without using cached container templates.
    /// Used during template initialization to avoid circular dependency.
    /// </summary>
    private ISqlContainer BuildBaseRetrieveDirect(string alias, IDatabaseContext context, ISqlDialect dialect)
    {
        var sc = context.CreateSqlContainer();

        var brCache = GetOrCreateQueryCache(dialect);
        var brKey = $"BaseRetrieve:{alias}";
        if (!brCache.TryGet(brKey, out var sql))
        {
            sql = BuildBaseRetrieveSql(alias, dialect);
            brCache.GetOrAdd(brKey, _ => sql);
        }

        sc.Query.Append(sql);
        return sc;
    }

    private string BuildBaseRetrieveSql(string alias, ISqlDialect dialect)
    {
        var hasAlias = !string.IsNullOrWhiteSpace(alias);
        var wrappedAliasPrefix = hasAlias
            ? dialect.WrapSimpleName(alias) + dialect.CompositeIdentifierSeparator
            : string.Empty;
        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        sb.Append("SELECT ");
        for (var i = 0; i < _tableInfo.OrderedColumns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(wrappedAliasPrefix);
            sb.Append(dialect.WrapSimpleName(_tableInfo.OrderedColumns[i].Name));
        }

        sb.Append("\nFROM ");
        sb.Append(BuildWrappedTableName(dialect));
        if (hasAlias)
        {
            sb.Append(' ');
            sb.Append(dialect.WrapSimpleName(alias));
        }

        return sb.ToString();
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
        var dialect = ((ISqlDialectProvider)sc).Dialect;
        var wrappedAlias = "";
        if (!string.IsNullOrWhiteSpace(alias))
        {
            wrappedAlias = dialect.WrapSimpleName(alias) + dialect.CompositeIdentifierSeparator;
        }

        var wrappedColumnName = wrappedAlias + dialect.WrapSimpleName(_idColumn.Name);

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
        var dialect = ((ISqlDialectProvider)sc).Dialect;
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

            sb.Append(BuildPrimaryKeyClause(entity, keys, wrappedAlias, parameters, dialect, ref counters));
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
            throw new InvalidOperationException($"No primary keys found for type {typeof(TEntity).Name}");
        }

        return keys;
    }

    private static string BuildAliasPrefix(string alias, ISqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        return dialect.WrapSimpleName(alias) + ".";
    }

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias,
        List<DbParameter> parameters, ISqlDialect dialect, ref ClauseCounters counters)
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
            clause.Append(dialect.WrapSimpleName(pk.Name));

            if (Utils.IsNullOrDbNull(value))
            {
                clause.Append(" IS NULL");
            }
            else
            {
                clause.Append(" = ");
                clause.Append(dialect.MakeParameterName(name));

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

        // Fast path: single-element collection — skip List/HashSet allocation entirely.
        // Handles the most common case: BuildRetrieve(new[]{ id }), RetrieveOneAsync(id).
        if (ids is IReadOnlyCollection<TRowID> singleCheck && singleCheck.Count == 1)
        {
            // Access element without allocating an IEnumerator box
            var singleId = singleCheck is IList<TRowID> singleList ? singleList[0] : singleCheck.First();

            if (Utils.IsNullOrDbNull(singleId))
            {
                AppendWherePrefix(sqlContainer);
                sqlContainer.Query.Append(wrappedColumnName).Append(" IS NULL");
                return sqlContainer;
            }

            var dialect1 = ((ISqlDialectProvider)sqlContainer).Dialect;
            CheckParameterLimit(sqlContainer, 1);

            // Use CachedSqlTemplates (keyed per dialect) instead of _queryCache to avoid
            // cross-dialect collision: wrappedColumnName is identical for dialects that share
            // the same quote style (e.g. SQLite and PostgreSQL both use '"'), so a shared
            // _queryCache key would embed the wrong parameter marker for the second dialect.
            var template1 = GetTemplatesForDialect(dialect1);
            AppendWherePrefix(sqlContainer);
            var wrappedIdName = dialect1.WrapSimpleName(_idColumn!.Name);
            if (string.Equals(wrappedColumnName, wrappedIdName, StringComparison.Ordinal))
            {
                sqlContainer.Query.Append(template1.IdEqualityWhereBody!);
            }
            else
            {
                sqlContainer.Query.Append(wrappedColumnName)
                    .Append(" = ")
                    .Append(dialect1.MakeParameterName("p0"));
            }
            var parameter = dialect1.CreateDbParameter("p0", _idColumn!.DbType, singleId);
            sqlContainer.AddParameter(parameter);
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
        // Reached when: deduplication reduced multiple inputs to 1 unique ID, or
        // the caller used a non-IReadOnlyCollection enumerable.
        // Use CachedSqlTemplates (dialect-keyed) to avoid cross-dialect collision in _queryCache.
        if (nonNullIds.Count == 1)
        {
            var template2 = GetTemplatesForDialect(dialect);
            var body = hasNull ? template2.IdEqualityNullableWhereBody! : template2.IdEqualityWhereBody!;
            AppendWherePrefix(sqlContainer);
            sqlContainer.Query.Append(body);
            var parameter = sqlContainer.CreateDbParameter("p0", _idColumn!.DbType, nonNullIds[0]);
            sqlContainer.AddParameter(parameter);
            return sqlContainer;
        }

        // Set-valued parameters (ANY)
        if (dialect.SupportsSetValuedParameters)
        {
            var paramName = sqlContainer.MakeParameterName("w0");
            var anyCache = GetOrCreateQueryCache(dialect);
            var anyKey = $"WhereAny:{wrappedColumnName}";
            if (!anyCache.TryGet(anyKey, out var anyCore))
            {
                anyCore = string.Concat(wrappedColumnName, " = ANY(", paramName, ")");
                anyCache.GetOrAdd(anyKey, _ => anyCore);
            }

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

        // Parameter names are dialect-specific (e.g. "@w0" vs ":w0"), so cache per dialect + bucket.
        var paramNamesCache = GetOrCreateParamNamesCache(dialect);
        var paramNamesKey = $"WhereParams:{bucket}";
        if (!paramNamesCache.TryGet(paramNamesKey, out var names))
        {
            names = new string[bucket];
            for (var i = 0; i < bucket; i++)
            {
                names[i] = sqlContainer.MakeParameterName($"w{i}");
            }

            paramNamesCache.GetOrAdd(paramNamesKey, _ => names);
        }

        // Query cache must include column name since it generates "columnName IN (w0, w1, w2)"
        var inCache = GetOrCreateQueryCache(dialect);
        var queryKey = $"WhereQuery:{wrappedColumnName}:{bucket}";
        if (!inCache.TryGet(queryKey, out var inCore))
        {
            var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
            sb.Append(wrappedColumnName);
            sb.Append(SqlFragments.In);
            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(SqlFragments.Comma);
                sb.Append(names[i]);
            }

            sb.Append(SqlFragments.CloseParen);
            inCore = sb.ToString();
            inCache.GetOrAdd(queryKey, _ => inCore);
        }

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
                // For positional providers, names[i] is "?" (the SQL marker), not a parameter name.
                // Use the raw logical name so the ordered-dictionary tracking stays correct.
                var parameter = sqlContainer.CreateDbParameter($"w{i}", dbType, value);
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
