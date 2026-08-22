// =============================================================================
// FILE: BaseTableGateway.Version.cs
// PURPOSE: Post-update [Version] value write-back for app-managed (non-opaque) version columns.
//
// AI SUMMARY:
// - WriteBackIncrementedVersion() is called after a successful UpdateAsync (rowsAffected > 0).
// - Shared by all gateway variants (TableGateway and PrimaryKeyTableGateway).
// =============================================================================

using System.Globalization;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: post-update version-column write-back.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <summary>
    /// After a successful UPDATE, writes the new [Version] value back into the caller's entity —
    /// but only for app-managed (non-<see cref="IColumnInfo.IsOpaqueVersionColumn"/>) version columns.
    /// </summary>
    /// <remarks>
    /// The UPDATE's SET clause increments the version server-side with a fixed, deterministic
    /// expression ("version = version + 1") — pengdows never sends a literal new value. The
    /// entity's version property is never mutated while building the UPDATE (the WHERE clause
    /// reads it as-is for the optimistic-concurrency check), so at the point a caller learns the
    /// write succeeded, the entity still holds the pre-update value. Since the WHERE clause's
    /// version match is exactly what made rowsAffected &gt; 0 possible, the new value is
    /// deterministically "current + 1" — no extra round trip needed to learn it.
    /// Opaque version columns (<see cref="byte[]"/>,
    /// <see cref="pengdows.crud.types.valueobjects.RowVersion"/>) are excluded: their new value is
    /// DB-generated (not a fixed "+1"), so there is no free fix for those — see
    /// docs/FUTURE_WORK.md's "entity freshness after a successful write" entry.
    /// Call only when the write is already known to have succeeded (rowsAffected &gt; 0); never
    /// call this on a failed/conflicted write.
    /// </remarks>
    protected void WriteBackIncrementedVersion(TEntity entity)
    {
        if (_versionColumn == null || _versionColumn.IsOpaqueVersionColumn)
        {
            return;
        }

        var current = _versionColumn.MakeParameterValueFromField(entity);
        var currentNumeric = current == null ? 0L : Convert.ToInt64(current, CultureInfo.InvariantCulture);

        var target = Nullable.GetUnderlyingType(_versionColumn.PropertyInfo.PropertyType) ??
                     _versionColumn.PropertyInfo.PropertyType;
        var next = TypeCoercionHelper.ConvertWithCache(currentNumeric + 1, target);
        _versionColumn.PropertyInfo.SetValue(entity, next);
    }
}
