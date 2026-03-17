// =============================================================================
// FILE: PrimaryKeyTableGateway.Delete.cs
// PURPOSE: Batch DELETE and batch CREATE operations keyed on [PrimaryKey] columns.
// =============================================================================

using System.Data;
using System.Data.Common;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// PrimaryKeyTableGateway partial: DELETE and batch CREATE operations.
/// </summary>
public partial class PrimaryKeyTableGateway<TEntity>
{
    // =========================================================================
    // BATCH CREATE
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchCreate(IReadOnlyList<TEntity> entities,
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

        if (!dialect.SupportsBatchInsert)
        {
            var fallback = new List<ISqlContainer>(entities.Count);
            foreach (var entity in entities)
            {
                fallback.Add(BuildCreate(entity, ctx));
            }

            return fallback;
        }

        var insertableColumns = GetCachedInsertableColumns();
        var auditValues = _hasAuditColumns ? ResolveAuditValuesForBatch() : null;

        foreach (var entity in entities)
        {
            if (_hasAuditColumns)
            {
                SetAuditFields(entity, false, auditValues);
            }

            PrepareVersionForCreate(entity);
        }

        var chunks = ChunkList(entities, insertableColumns.Count, ctx.MaxParameterLimit, dialect.MaxRowsPerBatch);
        var result = new List<ISqlContainer>(chunks.Count);

        foreach (var chunk in chunks)
        {
            result.Add(BuildPkBatchInsertContainer(chunk, insertableColumns, ctx, dialect));
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchCreateAsync(IReadOnlyList<TEntity> entities, IDatabaseContext? context = null,
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

        if (entities.Count == 1)
        {
            var ctx = context ?? _context;
            return await CreateAsync(entities[0], ctx, cancellationToken).ConfigureAwait(false) ? 1 : 0;
        }

        var containers = BuildBatchCreate(entities, context);
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
    // BATCH DELETE
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyList<ISqlContainer> BuildBatchDelete(IReadOnlyCollection<TEntity> entities,
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
        var pkCols = _tableInfo.PrimaryKeys;

        var result = new List<ISqlContainer>(entities.Count);

        foreach (var entity in entities)
        {
            var sc = ctx.CreateSqlContainer();
            var parameters = new List<DbParameter>(pkCols.Count);
            var counters = new ClauseCounters();

            sc.Query.Append("DELETE FROM ").Append(BuildWrappedTableName(dialect)).Append('\n')
                .Append(SqlFragments.Where);

            for (var i = 0; i < pkCols.Count; i++)
            {
                if (i > 0)
                {
                    sc.Query.Append(SqlFragments.And);
                }

                var pk = pkCols[i];
                var value = pk.MakeParameterValueFromField(entity);

                sc.Query.Append(dialect.WrapSimpleName(pk.Name));

                if (Utils.IsNullOrDbNull(value))
                {
                    sc.Query.Append(" IS NULL");
                }
                else
                {
                    var pName = counters.NextKey();
                    var param = dialect.CreateDbParameter(pName, pk.DbType, value);
                    parameters.Add(param);
                    sc.Query.Append(" = ");
                    if (dialect.SupportsNamedParameters)
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

            sc.AddParameters(parameters);
            result.Add(sc);
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask<int> BatchDeleteAsync(IReadOnlyCollection<TEntity> entities, IDatabaseContext? context = null,
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

        var containers = BuildBatchDelete(entities, context);
        var total = 0;
        foreach (var sc in containers)
        {
            await using var owned = sc;
            cancellationToken.ThrowIfCancellationRequested();
            total += await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }
}
