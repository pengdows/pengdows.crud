// =============================================================================
// FILE: BaseTableGateway.Retrieve.cs
// PURPOSE: Identity-neutral SELECT query building.
//          BuildBaseRetrieve, BuildWhereByPrimaryKey, and related helpers
//          are shared by both PrimaryKeyTableGateway and TableGateway.
// =============================================================================

using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: identity-neutral SELECT building and primary-key WHERE helpers.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <inheritdoc/>
    public ISqlContainer BuildBaseRetrieve(string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);

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

    /// <inheritdoc/>
    public ISqlContainer BuildBaseRetrieve(string alias, IReadOnlyCollection<string> extraSelectExpressions,
        IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        if (extraSelectExpressions.Count == 0)
        {
            return BuildBaseRetrieve(alias, ctx);
        }
        var dialect = GetDialect(ctx);
        var sc = ctx.CreateSqlContainer();
        sc.Query.Append(BuildBaseRetrieveSql(alias, dialect, extraSelectExpressions));
        return sc;
    }

    /// <summary>
    /// Builds base SELECT SQL directly without using cached container templates.
    /// Used during template initialization to avoid circular dependency.
    /// </summary>
    protected ISqlContainer BuildBaseRetrieveDirect(string alias, IDatabaseContext context, ISqlDialect dialect)
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

    protected string BuildBaseRetrieveSql(string alias, ISqlDialect dialect,
        IReadOnlyCollection<string>? extraSelectExpressions = null)
    {
        var hasAlias = !string.IsNullOrWhiteSpace(alias);
        var wrappedAliasPrefix = hasAlias
            ? dialect.WrapSimpleName(alias) + dialect.CompositeIdentifierSeparator
            : string.Empty;

        var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        sb.Append("SELECT ");
        for (var i = 0; i < _tableInfo.OrderedColumns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(wrappedAliasPrefix);
            sb.Append(dialect.WrapSimpleName(_tableInfo.OrderedColumns[i].Name));
        }

        if (extraSelectExpressions != null)
        {
            foreach (var expr in extraSelectExpressions)
            {
                sb.Append(", ");
                sb.Append(dialect.WrapObjectName(expr));
            }
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
    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        string alias, IDatabaseContext? context = null)
    {
        var ctx = context ?? _context;
        var dialect = GetDialect(ctx);
        var sc = BuildBaseRetrieve(alias, ctx);
        BuildWhereByPrimaryKey(listOfObjects, sc, alias, dialect);
        return sc;
    }

    /// <inheritdoc/>
    public ISqlContainer BuildRetrieve(IReadOnlyCollection<TEntity>? listOfObjects,
        IDatabaseContext? context = null)
    {
        return BuildRetrieve(listOfObjects, string.Empty, context);
    }

    /// <inheritdoc/>
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
        try
        {
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
        finally
        {
            sb.Dispose();
        }
    }

    private void ValidateWhereInputs(IReadOnlyCollection<TEntity>? listOfObjects, ISqlContainer _)
    {
        if (listOfObjects == null || listOfObjects.Count == 0)
        {
            throw new ArgumentException("List of objects cannot be null or empty.", nameof(listOfObjects));
        }
    }

    internal IReadOnlyList<IColumnInfo> GetPrimaryKeys()
    {
        var keys = _tableInfo.PrimaryKeys;
        if (keys.Count < 1)
        {
            throw new InvalidOperationException($"No primary keys found for type {typeof(TEntity).Name}");
        }

        return keys;
    }

    protected static string BuildAliasPrefix(string alias, ISqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        return dialect.WrapSimpleName(alias) + dialect.CompositeIdentifierSeparator;
    }

    private string BuildPrimaryKeyClause(TEntity entity, IReadOnlyList<IColumnInfo> keys, string alias,
        List<DbParameter> parameters, ISqlDialect dialect, ref ClauseCounters counters)
    {
        var clause = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
        try
        {
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
        finally
        {
            clause.Dispose();
        }
    }

    protected void AppendWherePrefix(ISqlContainer sc)
    {
        if (!sc.HasWhereAppended)
        {
            sc.Query.Append('\n');
            sc.Query.Append(SqlFragments.Where);
            if (sc is SqlContainer sqlContainer)
            {
                sqlContainer.HasWhereAppended = true;
            }
        }
        else
        {
            sc.Query.Append('\n');
            sc.Query.Append(SqlFragments.And);
        }
    }
}
