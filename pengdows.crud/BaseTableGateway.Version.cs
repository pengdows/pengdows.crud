// =============================================================================
// FILE: BaseTableGateway.Version.cs
// PURPOSE: Post-update [Version] value write-back for app-managed (non-opaque) version columns.
//
// AI SUMMARY:
// - WriteBackIncrementedVersion() is called after a successful UPDATE (rowsAffected > 0) by
//   UpdateAsync and BatchUpdateAsync on both TableGateway and PrimaryKeyTableGateway.
// =============================================================================

using System.Globalization;
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
        _versionColumn.PropertyInfo.SetValue(entity, next);
    }
}
