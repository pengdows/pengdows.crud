// =============================================================================
// FILE: BaseTableGateway.Audit.cs
// PURPOSE: Audit field handling for CreatedBy/On and LastUpdatedBy/On columns.
//
// AI SUMMARY:
// - SetAuditFields() populates audit columns during Create and Update.
// - Shared by all gateway variants (TableGateway and PrimaryKeyTableGateway).
// =============================================================================

using System.Globalization;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: Audit field population logic.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    /// <summary>
    /// Type-safe coercion for audit field values (handles string to Guid, etc.)
    /// </summary>
    private static object? Coerce(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(value))
        {
            return value;
        }

        if (t == typeof(Guid) && value is string s)
        {
            if (!Guid.TryParse(s, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Cannot parse '{s}' as Guid for audit column of type {targetType.Name}.");
            }

            return parsed;
        }

        return TypeCoercionHelper.ConvertWithCache(value, t);
    }

    /// <summary>
    /// Returns true if the timestamp value is null/default for DateTime or DateTimeOffset.
    /// </summary>
    private static bool IsDefaultTimestamp(object? value)
    {
        return value switch
        {
            null => true,
            DateTime dt => dt == default,
            DateTimeOffset dto => dto == default,
            _ => false
        };
    }

    /// <summary>
    /// Converts a DateTimeOffset to the correct boxed type for the target property.
    /// </summary>
    private static object CoerceTimestamp(DateTimeOffset timestamp, Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying == typeof(DateTimeOffset))
        {
            return timestamp;
        }

        return timestamp.UtcDateTime;
    }

    private static DateTimeOffset ResolveAuditTimestamp(IAuditValues? auditValues)
    {
        if (auditValues?.TimestampOffset is DateTimeOffset offset)
        {
            if (offset.Offset != TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"TimestampOffset must be UTC (Offset must be TimeSpan.Zero); got {offset.Offset}.");
            }

            return offset;
        }

        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;
        return new DateTimeOffset(utcNow, TimeSpan.Zero);
    }

    protected void SetAuditFields(TEntity obj, bool updateOnly)
    {
        if (obj == null)
        {
            return;
        }

        if (!_hasAuditColumns)
        {
            return;
        }

        var hasUserAuditFields = _tableInfo.CreatedBy != null || _tableInfo.LastUpdatedBy != null;

        if (hasUserAuditFields && _auditValueResolver is null)
        {
            throw new InvalidOperationException("AuditValues resolver is required for user-based audit fields.");
        }

        var auditValues = _auditValueResolver?.Resolve();
        SetAuditFields(obj, updateOnly, auditValues);
    }

    /// <summary>
    /// Applies pre-resolved audit values to a single entity. Used by batch operations to
    /// avoid calling <see cref="IAuditValueResolver.Resolve"/> once per entity.
    /// </summary>
    protected void SetAuditFields(TEntity obj, bool updateOnly, IAuditValues? auditValues)
    {
        if (obj == null)
        {
            return;
        }

        if (!_hasAuditColumns)
        {
            return;
        }

        var timestamp = ResolveAuditTimestamp(auditValues);

        if (_auditLastUpdatedOnSetter != null
            && (updateOnly ? !_tableInfo.LastUpdatedOn!.IsNonUpdateable : !_tableInfo.LastUpdatedOn!.IsNonInsertable))
        {
            var coercedTime = CoerceTimestamp(timestamp, _tableInfo.LastUpdatedOn!.PropertyInfo.PropertyType);
            _auditLastUpdatedOnSetter(obj, coercedTime);
        }

        if (_auditLastUpdatedBySetter != null && auditValues != null
            && (updateOnly ? !_tableInfo.LastUpdatedBy!.IsNonUpdateable : !_tableInfo.LastUpdatedBy!.IsNonInsertable))
        {
            var coercedUserId = Coerce(auditValues.UserId, _tableInfo.LastUpdatedBy!.PropertyInfo.PropertyType);
            _auditLastUpdatedBySetter(obj, coercedUserId);
        }

        if (updateOnly)
        {
            return;
        }

        if (_auditCreatedOnSetter != null && !_tableInfo.CreatedOn!.IsNonInsertable)
        {
            var currentValue = _tableInfo.CreatedOn!.MakeParameterValueFromField(obj);
            if (AuditCreationPolicy == pengdows.crud.enums.AuditCreationPolicy.Authoritative || IsDefaultTimestamp(currentValue))
            {
                var coercedTime = CoerceTimestamp(timestamp, _tableInfo.CreatedOn.PropertyInfo.PropertyType);
                _auditCreatedOnSetter(obj, coercedTime);
            }
        }

        if (_auditCreatedBySetter != null && auditValues != null && !_tableInfo.CreatedBy!.IsNonInsertable)
        {
            var currentValue = _tableInfo.CreatedBy!.MakeParameterValueFromField(obj);
            if (AuditCreationPolicy == pengdows.crud.enums.AuditCreationPolicy.Authoritative
                || currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
            {
                var coercedUserId = Coerce(auditValues.UserId, _tableInfo.CreatedBy.PropertyInfo.PropertyType);
                _auditCreatedBySetter(obj, coercedUserId);
            }
        }
    }

    /// <summary>
    /// Snapshot of an entity's audit-column values captured before <see cref="SetAuditFields"/>
    /// mutates them. <see cref="SetAuditFields"/> runs during Build (before any SQL executes), so
    /// if the subsequent Execute never succeeds, the entity's audit fields would otherwise claim a
    /// write that never persisted. Pair <see cref="SnapshotAuditFields"/> with
    /// <see cref="RestoreAuditFields"/> around Execute to undo that.
    /// </summary>
    protected readonly struct AuditFieldSnapshot
    {
        private readonly bool _hasSnapshot;

        internal AuditFieldSnapshot(object? lastUpdatedOn, object? lastUpdatedBy, object? createdOn, object? createdBy)
        {
            LastUpdatedOn = lastUpdatedOn;
            LastUpdatedBy = lastUpdatedBy;
            CreatedOn = createdOn;
            CreatedBy = createdBy;
            _hasSnapshot = true;
        }

        internal bool HasSnapshot => _hasSnapshot;
        internal object? LastUpdatedOn { get; }
        internal object? LastUpdatedBy { get; }
        internal object? CreatedOn { get; }
        internal object? CreatedBy { get; }
    }

    /// <summary>
    /// Captures the entity's current audit-column values before a Build method mutates them via
    /// <see cref="SetAuditFields"/>. Returns a no-op snapshot (restoring is a no-op) when the
    /// entity has no audit columns.
    /// </summary>
    protected AuditFieldSnapshot SnapshotAuditFields(TEntity obj)
    {
        if (obj == null || !_hasAuditColumns)
        {
            return default;
        }

        return new AuditFieldSnapshot(
            _tableInfo.LastUpdatedOn?.PropertyInfo.GetValue(obj),
            _tableInfo.LastUpdatedBy?.PropertyInfo.GetValue(obj),
            _tableInfo.CreatedOn?.PropertyInfo.GetValue(obj),
            _tableInfo.CreatedBy?.PropertyInfo.GetValue(obj));
    }

    /// <summary>
    /// Restores audit-column values captured by <see cref="SnapshotAuditFields"/>. Call from a
    /// catch block (or before manually raising an error after a 0-rows-affected result) when the
    /// SQL built with the mutated values never executed successfully.
    /// </summary>
    protected void RestoreAuditFields(TEntity obj, in AuditFieldSnapshot snapshot)
    {
        if (obj == null || !snapshot.HasSnapshot)
        {
            return;
        }

        _auditLastUpdatedOnSetter?.Invoke(obj, snapshot.LastUpdatedOn);
        _auditLastUpdatedBySetter?.Invoke(obj, snapshot.LastUpdatedBy);
        _auditCreatedOnSetter?.Invoke(obj, snapshot.CreatedOn);
        _auditCreatedBySetter?.Invoke(obj, snapshot.CreatedBy);
    }

    /// <summary>
    /// A catch block only reacts to a THROWN exception — it can't see a plain "return false".
    /// Use this at every unsuccessful-result return point (not just in catch blocks) so a write
    /// that fails without throwing (e.g. ExecuteNonQueryAsync affecting 0 rows) still gets its
    /// audit fields restored.
    /// </summary>
    protected bool RestoreAuditFieldsIfFailed(bool succeeded, TEntity obj, in AuditFieldSnapshot snapshot)
    {
        if (!succeeded)
        {
            RestoreAuditFields(obj, snapshot);
        }

        return succeeded;
    }

    /// <summary>
    /// Validates audit resolver requirements and resolves audit values once for use
    /// across an entire batch.
    /// </summary>
    protected IAuditValues? ResolveAuditValuesForBatch()
    {
        if (!_hasAuditColumns)
        {
            return null;
        }

        var hasUserAuditFields = _tableInfo.CreatedBy != null || _tableInfo.LastUpdatedBy != null;
        if (hasUserAuditFields && _auditValueResolver is null)
        {
            throw new InvalidOperationException("AuditValues resolver is required for user-based audit fields.");
        }

        return _auditValueResolver?.Resolve();
    }
}
