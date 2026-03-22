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

        if (_auditLastUpdatedOnSetter != null)
        {
            var coercedTime = CoerceTimestamp(timestamp, _tableInfo.LastUpdatedOn!.PropertyInfo.PropertyType);
            _auditLastUpdatedOnSetter(obj, coercedTime);
        }

        if (_auditLastUpdatedBySetter != null && auditValues != null)
        {
            var coercedUserId = Coerce(auditValues.UserId, _tableInfo.LastUpdatedBy!.PropertyInfo.PropertyType);
            _auditLastUpdatedBySetter(obj, coercedUserId);
        }

        if (updateOnly)
        {
            return;
        }

        if (_auditCreatedOnSetter != null)
        {
            var currentValue = _tableInfo.CreatedOn!.MakeParameterValueFromField(obj);
            if (IsDefaultTimestamp(currentValue))
            {
                var coercedTime = CoerceTimestamp(timestamp, _tableInfo.CreatedOn.PropertyInfo.PropertyType);
                _auditCreatedOnSetter(obj, coercedTime);
            }
        }

        if (_auditCreatedBySetter != null && auditValues != null)
        {
            var currentValue = _tableInfo.CreatedBy!.MakeParameterValueFromField(obj);
            if (currentValue == null
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
