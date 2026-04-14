// =============================================================================
// FILE: PrimaryKeyTableGateway.Update.cs
// PURPOSE: UPDATE and batch UPDATE operations keyed on [PrimaryKey] columns.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// PrimaryKeyTableGateway partial: UPDATE operations.
/// </summary>
public partial class PrimaryKeyTableGateway<TEntity>
{
    /// <inheritdoc/>
    public ValueTask<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (objectToUpdate == null)
        {
            throw new ArgumentNullException(nameof(objectToUpdate));
        }

        var ctx = context ?? _context;
        return ValueTask.FromResult(BuildUpdateByPk(objectToUpdate, ctx));
    }

    /// <inheritdoc/>
    public ValueTask<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null, CancellationToken cancellationToken = default)
    {
        // TODO: PrimaryKeyTableGateway does not support loading the original entity by row ID
        // (there is no TRowID type parameter). The loadOriginal flag is ignored.
        return BuildUpdateAsync(objectToUpdate, context, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpdateAsync(TEntity objectToUpdate, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (objectToUpdate == null)
        {
            throw new ArgumentNullException(nameof(objectToUpdate));
        }

        var ctx = context ?? _context;
        await using var sc = await BuildUpdateAsync(objectToUpdate, ctx, cancellationToken).ConfigureAwait(false);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _context;
        await using var sc =
            await BuildUpdateAsync(objectToUpdate, loadOriginal, ctx, cancellationToken).ConfigureAwait(false);
        return await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
    }

    // =========================================================================
    // BATCH UPDATE
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchUpdate(IReadOnlyList<TEntity> entities,
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
        var dialect = GetDialect(ctx);

        var auditValues = _auditValueResolver != null && _hasAuditColumns
            ? ResolveAuditValuesForBatch()
            : null;

        var result = new List<ISqlContainer>(entities.Count);
        foreach (var entity in entities)
        {
            if (_hasAuditColumns)
            {
                SetAuditFields(entity, true, auditValues);
            }

            result.Add(BuildUpdateByPk(entity, ctx, dialect, auditAlreadySet: true));
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchUpdateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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

        var ctx = context ?? _context;
        if (entities.Count == 1)
        {
            return await UpdateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
        }

        var containers = BuildBatchUpdate(entities, ctx);
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
    // Private helpers
    // =========================================================================

    private ISqlContainer BuildUpdateByPk(TEntity entity, IDatabaseContext? context = null,
        ISqlDialect? preResolvedDialect = null, bool auditAlreadySet = false)
    {
        var ctx = context ?? _context;
        var dialect = preResolvedDialect ?? GetDialect(ctx);

        if (_hasAuditColumns && !auditAlreadySet)
        {
            SetAuditFields(entity, true);
        }

        var template = GetPkTemplatesForDialect(dialect);
        var sc = ctx.CreateSqlContainer();
        var counters = new ClauseCounters();

        sc.Query.Append(template.UpdateSqlPrefix);

        var parameters = new List<DbParameter>(template.UpdateColumns.Count + _tableInfo.PrimaryKeys.Count);
        var columnsAdded = 0;

        for (var i = 0; i < template.UpdateColumns.Count; i++)
        {
            var col = template.UpdateColumns[i];
            var value = col.MakeParameterValueFromField(entity);

            if (columnsAdded > 0)
            {
                sc.Query.Append(SqlFragments.Comma);
            }

            columnsAdded++;

            if (Utils.IsNullOrDbNull(value))
            {
                sc.Query.Append(template.UpdateColumnWrappedNames[i]);
                sc.Query.Append(" = NULL");
            }
            else
            {
                var pName = counters.NextSet();
                var param = dialect.CreateDbParameter(pName, col.DbType, value);
                if (col.IsJsonType)
                {
                    dialect.TryMarkJsonParameter(param, col);
                }

                parameters.Add(param);
                sc.Query.Append(template.UpdateColumnWrappedNames[i]);
                sc.Query.Append(SqlFragments.EqualsOp);
                if (col.IsJsonType)
                {
                    sc.Query.Append(dialect.RenderJsonArgument(dialect.MakeParameterName(pName), col));
                }
                else if (dialect.SupportsNamedParameters)
                {
                    sc.Query.Append(dialect.ParameterMarker);
                    sc.Query.Append(pName);
                }
                else
                {
                    sc.Query.Append('?');
                }
            }
        }

        if (columnsAdded == 0)
        {
            throw new InvalidOperationException("No updatable columns found for UPDATE.");
        }

        // Append version increment if applicable
        if (template.VersionIncrementClause != null)
        {
            sc.Query.Append(template.VersionIncrementClause);
        }

        // Build WHERE by PK columns
        sc.Query.Append('\n').Append(SqlFragments.Where);

        var pkCols = _tableInfo.PrimaryKeys;
        for (var i = 0; i < pkCols.Count; i++)
        {
            if (i > 0)
            {
                sc.Query.Append(SqlFragments.And);
            }

            var pk = pkCols[i];
            var pkValue = pk.MakeParameterValueFromField(entity);
            var pkName = counters.NextKey();

            sc.Query.Append(dialect.WrapSimpleName(pk.Name));

            if (Utils.IsNullOrDbNull(pkValue))
            {
                sc.Query.Append(" IS NULL");
            }
            else
            {
                var pkParam = dialect.CreateDbParameter(pkName, pk.DbType, pkValue);
                parameters.Add(pkParam);
                sc.Query.Append(" = ");
                if (dialect.SupportsNamedParameters)
                {
                    sc.Query.Append(dialect.ParameterMarker);
                    sc.Query.Append(pkName);
                }
                else
                {
                    sc.Query.Append('?');
                }
            }
        }

        // Append version condition if applicable
        if (_versionColumn != null)
        {
            var versionValue = _versionColumn.MakeParameterValueFromField(entity);
            var versionParam = AppendPkVersionCondition(sc, versionValue, dialect, ref counters);
            if (versionParam != null)
            {
                parameters.Add(versionParam);
            }
        }

        sc.AddParameters(parameters);
        return sc;
    }

    private DbParameter? AppendPkVersionCondition(ISqlContainer sc, object? versionValue, ISqlDialect dialect,
        ref ClauseCounters counters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(SqlFragments.And)
                .Append(sc.WrapObjectName(_versionColumn!.Name))
                .Append(" IS NULL");
            return null;
        }

        var name = counters.NextVer();
        var pVersion = dialect.CreateDbParameter(name, _versionColumn!.DbType, versionValue);
        sc.Query.Append(SqlFragments.And)
            .Append(sc.WrapObjectName(_versionColumn.Name))
            .Append(" = ");
        if (dialect.SupportsNamedParameters)
        {
            sc.Query.Append(dialect.ParameterMarker);
            sc.Query.Append(name);
        }
        else
        {
            sc.Query.Append('?');
        }

        return pVersion;
    }
}
