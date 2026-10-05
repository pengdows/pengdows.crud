// =============================================================================
// FILE: PrimaryKeyTableGateway.Update.cs
// PURPOSE: UPDATE and batch UPDATE operations keyed on [PrimaryKey] columns.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
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
        return DeclaredTypesPending(ctx)
            ? BuildUpdateAfterDeclaredTypesAsync(objectToUpdate, ctx, cancellationToken)
            : ValueTask.FromResult(BuildUpdateByPk(objectToUpdate, ctx));
    }

    // TYPE-020: the first build on a context learns the table's declared column types first.
    private async ValueTask<ISqlContainer> BuildUpdateAfterDeclaredTypesAsync(TEntity objectToUpdate,
        IDatabaseContext ctx, CancellationToken cancellationToken)
    {
        await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false);
        return BuildUpdateByPk(objectToUpdate, ctx);
    }

    /// <inheritdoc/>
    public ValueTask<ISqlContainer> BuildUpdateAsync(TEntity objectToUpdate, bool loadOriginal,
        IDatabaseContext? context = null, CancellationToken cancellationToken = default)
    {
        // loadOriginal exists for symmetry with ITableGateway and is deliberately ignored: every
        // updateable column is written, keyed on the [PrimaryKey] columns.
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

        // BuildUpdateAsync mutates audit fields as a side effect of building the UPDATE, before
        // anything executes. Restore them if the write never actually succeeds.
        var auditSnapshot = SnapshotAuditFields(objectToUpdate);
        try
        {
            var ctx = context ?? _context;
            await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
            await using var sc = await BuildUpdateAsync(objectToUpdate, ctx, cancellationToken).ConfigureAwait(false);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            RestoreAuditFieldsIfFailed(rowsAffected != 0, objectToUpdate, auditSnapshot);
            if (rowsAffected == 0 && _versionColumn != null)
            {
                throw VersionConflict(ctx);
            }

            if (rowsAffected != 0)
            {
                WriteBackIncrementedVersion(objectToUpdate);
            }

            return rowsAffected;
        }
        catch
        {
            RestoreAuditFields(objectToUpdate, auditSnapshot);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<int> UpdateAsync(TEntity objectToUpdate, bool loadOriginal, IDatabaseContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // See the UpdateAsync overload above for why the audit snapshot exists.
        var auditSnapshot = SnapshotAuditFields(objectToUpdate);
        try
        {
            var ctx = context ?? _context;
            await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
            await using var sc =
                await BuildUpdateAsync(objectToUpdate, loadOriginal, ctx, cancellationToken).ConfigureAwait(false);
            var rowsAffected = await sc.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
            RestoreAuditFieldsIfFailed(rowsAffected != 0, objectToUpdate, auditSnapshot);
            if (rowsAffected == 0 && _versionColumn != null)
            {
                throw VersionConflict(ctx);
            }

            if (rowsAffected != 0)
            {
                WriteBackIncrementedVersion(objectToUpdate);
            }

            return rowsAffected;
        }
        catch
        {
            RestoreAuditFields(objectToUpdate, auditSnapshot);
            throw;
        }
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

            var container = BuildUpdateByPk(entity, ctx, dialect, auditAlreadySet: true);
            TrackBatchContainer(container, [entity]);
            result.Add(container);
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
        await EnsureDeclaredTypesAsync(ctx, cancellationToken).ConfigureAwait(false); // TYPE-020
        if (entities.Count == 1)
        {
            return await UpdateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false);
        }

        var auditSnapshots = SnapshotBatchAuditFields(entities);
        return await ExecuteBatchAsync(entities, BuildBatchUpdate(entities, ctx), auditSnapshots,
            BatchCheck.Update, ctx, cancellationToken).ConfigureAwait(false);
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
                dialect.MarkColumnParameter(param, col);

                parameters.Add(param);
                sc.Query.Append(template.UpdateColumnWrappedNames[i]);
                sc.Query.Append(SqlFragments.EqualsOp);
                if (dialect.RendersColumnArgument(col))
                {
                    sc.Query.Append(dialect.RenderColumnArgument(dialect.MakeParameterName(pName), col));
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

            sc.Query.Append(WrapColumnReference(dialect, pk.Name));

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
            var versionParam = AppendVersionCondition(sc, versionValue, dialect, ref counters);
            if (versionParam != null)
            {
                parameters.Add(versionParam);
            }
        }

        sc.AddParameters(parameters);
        return sc;
    }
}
