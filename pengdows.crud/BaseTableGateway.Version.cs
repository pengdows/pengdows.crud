// =============================================================================
// FILE: BaseTableGateway.Version.cs
// PURPOSE: The [Version] rules both gateways share: its starting value, the post-update write-back
//          and the concurrency-conflict errors (DRY-016).
//
// AI SUMMARY:
// - InitializeVersion() starts an unset (null/0) app-managed version at 1 on create and upsert.
// - WriteBackIncrementedVersion() is called after a successful UPDATE (rowsAffected > 0) by
//   UpdateAsync and BatchUpdateAsync on both TableGateway and PrimaryKeyTableGateway.
// - VersionConflict()/BatchVersionConflict() build the ConcurrencyConflictException;
//   UpsertCanDetectVersionConflict() says whether a single-row upsert's rows affected can show one.
// =============================================================================

using System.Data.Common;
using System.Globalization;
using pengdows.crud.dialects;
using pengdows.crud.exceptions;
using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: post-update version-column write-back.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <summary>
    /// After a successful UPDATE, writes the new [Version] value back into the caller's entity so
    /// the same instance can be updated again without a spurious version conflict.
    /// </summary>
    /// <remarks>
    /// The UPDATE increments the version server-side with a fixed "version = version + 1" and the
    /// WHERE clause matched the entity's current value, so a successful write (rows affected &gt; 0)
    /// means the new value is exactly "current + 1"; no round trip is needed. Opaque version
    /// columns (<see cref="byte"/>[] and RowVersion) are DB-generated and left untouched. Call only
    /// after the write is known to have succeeded.
    /// </remarks>
    /// <summary>
    /// An app-managed [Version] the entity left unset (null or 0) starts at 1 on create and upsert. An
    /// opaque version (<see cref="byte"/>[], RowVersion) is the database's to set.
    /// </summary>
    private protected void InitializeVersion(TEntity entity)
    {
        if (_versionColumn == null || _versionColumn.IsOpaqueVersionColumn())
        {
            return;
        }

        var current = _versionColumn.MakeParameterValueFromField(entity);
        if (current == null || Utils.IsZeroNumeric(current))
        {
            var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                         _versionColumn.PropertyInfo.PropertyType;
            SetColumnValue(_versionColumn, entity, TypeCoercionHelper.ConvertWithCache(1, target));
        }
    }

    /// <summary>A write that matched no row of a versioned entity: a stale version or a deleted row.</summary>
    private protected static ConcurrencyConflictException VersionConflict(IDatabaseContext ctx) =>
        new($"Concurrency conflict on {typeof(TEntity).Name}: version mismatch or row deleted.", ctx.Product);

    /// <summary>A batch container that affected fewer rows than it carried entities.</summary>
    private protected ConcurrencyConflictException BatchVersionConflict(IDatabaseContext ctx,
        IReadOnlyList<TEntity> chunkEntities, int affected) =>
        new(chunkEntities.Count == 1
                ? $"Concurrency conflict on {typeof(TEntity).Name} " +
                  $"({DescribeConflictKey(chunkEntities[0])}): version mismatch or row deleted."
                : $"Concurrency conflict on {typeof(TEntity).Name}: expected {chunkEntities.Count} row(s) " +
                  $"affected but {affected} succeeded. Which specific entity/entities conflicted cannot be " +
                  "individually identified from this batch SQL shape — no RETURNING/OUTPUT is used for batch " +
                  "operations, by design, for cross-dialect portability. Re-read every entity in this batch " +
                  "from the database before retrying; do not assume only some are stale.",
            ctx.Product);

    /// <summary>The entity's key for a conflict message: its [PrimaryKey] columns by default.</summary>
    private protected virtual string DescribeConflictKey(TEntity entity) =>
        _tableInfo.PrimaryKeys.Count > 0
            ? string.Join(", ", _tableInfo.PrimaryKeys.Select(pk => $"{pk.Name}={pk.MakeParameterValueFromField(entity)}"))
            : "key unknown";

    /// <summary>
    /// Whether a single-row upsert that affected no row reveals a version conflict: only when its
    /// statement carries a version guard whose skip it reports (ON CONFLICT ... WHERE, or a guarded
    /// MERGE that reports the skipped row). An opaque version is never guarded.
    /// </summary>
    private protected bool UpsertCanDetectVersionConflict(ISqlDialect dialect) =>
        _versionColumn != null && !_versionColumn.IsOpaqueVersionColumn() &&
        (dialect.SupportsOnConflictWhere || (dialect.SupportsMerge && dialect.MergeUpsertReportsSkippedVersionRow()));

    /// <summary>
    /// Appends the optimistic-lock predicate to an UPDATE's WHERE: <c>AND version = @v</c>, or
    /// <c>AND version IS NULL</c> for a null version. Returns the parameter to add, if any.
    /// </summary>
    private protected DbParameter? AppendVersionCondition(ISqlContainer sc, object? versionValue, ISqlDialect dialect,
        ref ClauseCounters counters)
    {
        if (versionValue == null)
        {
            sc.Query.Append(SqlFragments.And).Append(WrapColumnReference(dialect, _versionColumn!.Name)).Append(" IS NULL");
            return null;
        }

        var name = counters.NextVer();
        var pVersion = dialect.CreateDbParameter(name, _versionColumn!.DbType, versionValue);
        sc.Query.Append(SqlFragments.And).Append(WrapColumnReference(dialect, _versionColumn.Name)).Append(" = ");
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

    private protected void WriteBackIncrementedVersion(TEntity entity)
    {
        if (_versionColumn == null || _versionColumn.IsOpaqueVersionColumn())
        {
            return;
        }

        var current = _versionColumn.MakeParameterValueFromField(entity);
        var currentNumeric = current == null ? 0L : Convert.ToInt64(current, CultureInfo.InvariantCulture);

        var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                     _versionColumn.PropertyInfo.PropertyType;
        var next = TypeCoercionHelper.ConvertWithCache(currentNumeric + 1, target);
        SetColumnValue(_versionColumn, entity, next);
    }
}
