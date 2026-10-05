// =============================================================================
// FILE: BaseTableGateway.Batch.cs
// PURPOSE: The batch bookkeeping both gateways share (DRY-016).
//
// AI SUMMARY:
// - _batchContainerEntities maps a batch container to the entities it carries, weakly.
// - TrackBatchContainer() records it; RestoreBatchAuditFields() restores the audit fields of the
//   entities in containers that never ran after a partial batch failure.
// - ExecuteBatchAsync() runs a batch's containers in order and sums rows affected, applying the
//   operation's per-container version check (BatchCheck); every batch entry point uses it.
// =============================================================================

using System.Data;
using System.Runtime.CompilerServices;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: batch container ownership and audit restore.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    // Batch SQL can group multiple entities into a single container. Keep the ownership metadata
    // weakly attached to that container so a partial batch failure restores audit fields only for
    // containers that never completed, without extending the container's lifetime.
    private protected readonly ConditionalWeakTable<ISqlContainer, IReadOnlyList<TEntity>> _batchContainerEntities = new();

    private protected void TrackBatchContainer(ISqlContainer container, IReadOnlyList<TEntity> entities)
    {
        _batchContainerEntities.Remove(container);
        _batchContainerEntities.Add(container, entities);
    }

    private protected void RestoreBatchAuditFields(
        IReadOnlyList<ISqlContainer> containers,
        int firstUnexecutedContainer,
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<AuditFieldSnapshot> snapshots)
    {
        if (!_hasAuditColumns)
        {
            return;
        }

        // Each entity's snapshot by reference identity, built once (its first position, as the
        // linear search found): the search per entity made the restore O(N x chunk) (REV-065).
        var snapshotIndex = new Dictionary<TEntity, int>(entities.Count, ReferenceEqualityComparer.Instance);
        for (var entityIndex = 0; entityIndex < entities.Count; entityIndex++)
        {
            snapshotIndex.TryAdd(entities[entityIndex], entityIndex);
        }

        for (var containerIndex = firstUnexecutedContainer; containerIndex < containers.Count; containerIndex++)
        {
            if (!_batchContainerEntities.TryGetValue(containers[containerIndex], out var chunk))
            {
                continue;
            }

            foreach (var entity in chunk)
            {
                if (snapshotIndex.TryGetValue(entity, out var entityIndex))
                {
                    RestoreAuditFields(entity, snapshots[entityIndex]);
                }
            }
        }
    }

    /// <summary>What a batch checks of each container's rows affected.</summary>
    private protected enum BatchCheck
    {
        /// <summary>Nothing: the rows affected are summed.</summary>
        None,

        /// <summary>A version-guarded upsert: fewer rows than entities is a version conflict.</summary>
        UpsertGuard,

        /// <summary>One UPDATE per entity: none affected on a versioned entity is a conflict; a
        /// successful one writes the incremented version back.</summary>
        Update
    }

    /// <summary>The entities' audit fields before a batch sets them, or none when there are none.</summary>
    private protected AuditFieldSnapshot[] SnapshotBatchAuditFields(IReadOnlyList<TEntity> entities) =>
        _hasAuditColumns ? entities.Select(SnapshotAuditFields).ToArray() : Array.Empty<AuditFieldSnapshot>();

    /// <summary>
    /// Runs <paramref name="containers"/> in order and returns the rows they affected, checking each
    /// as <paramref name="check"/> says. When one fails, the audit fields of the entities in it and
    /// every later container are restored from <paramref name="snapshots"/> (none for a delete).
    /// </summary>
    private protected async ValueTask<int> ExecuteBatchAsync(IReadOnlyList<TEntity> entities,
        IReadOnlyList<ISqlContainer> containers, AuditFieldSnapshot[]? snapshots, BatchCheck check,
        IDatabaseContext ctx, CancellationToken cancellationToken)
    {
        var total = 0;
        var completedContainers = 0;
        try
        {
            foreach (var sc in containers)
            {
                await using var owned = sc;
                cancellationToken.ThrowIfCancellationRequested();
                var affected = await owned.ExecuteNonQueryAsync(CommandType.Text, cancellationToken).ConfigureAwait(false);
                if (check != BatchCheck.None && _batchContainerEntities.TryGetValue(sc, out var chunkEntities))
                {
                    // A guarded upsert that skips a stale row reports it as not affected; an UPDATE's
                    // WHERE deterministically matches or doesn't, so 0 on a versioned entity is a
                    // conflict (or a deleted row).
                    if ((check == BatchCheck.UpsertGuard && affected < chunkEntities.Count) ||
                        (check == BatchCheck.Update && affected == 0 && _versionColumn != null))
                    {
                        throw BatchVersionConflict(ctx, chunkEntities, affected);
                    }

                    // This container's UPDATE succeeded, so its [Version] increment took effect.
                    if (check == BatchCheck.Update && affected != 0)
                    {
                        foreach (var entity in chunkEntities)
                        {
                            WriteBackIncrementedVersion(entity);
                        }
                    }
                }

                total += affected;
                completedContainers++;
            }
        }
        catch when (snapshots != null)
        {
            RestoreBatchAuditFields(containers, completedContainers, entities, snapshots);
            throw;
        }

        return total;
    }
}
