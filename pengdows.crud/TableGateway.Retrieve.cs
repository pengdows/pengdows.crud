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
using pengdows.crud.infrastructure;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: SELECT query building and retrieval operations.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    // BuildBaseRetrieve, BuildBaseRetrieveDirect, BuildBaseRetrieveSql moved to BaseTableGateway.Retrieve.cs
    // BuildRetrieve(entities), BuildWhereByPrimaryKey, GetPrimaryKeys, BuildAliasPrefix,
    // BuildPrimaryKeyClause, AppendWherePrefix, ValidateWhereInputs moved to BaseTableGateway.Retrieve.cs

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
    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TRowID>? listOfIds, IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfIds, "", context);
    }

    // BuildRetrieve(entities), BuildWhereByPrimaryKey, GetPrimaryKeys, AppendWherePrefix
    // moved to BaseTableGateway.Retrieve.cs

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
            try
            {
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
                inCore = sb.ToString();
                inCache.GetOrAdd(queryKey, _ => inCore);
            }
            finally
            {
                sb.Dispose();
            }
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
