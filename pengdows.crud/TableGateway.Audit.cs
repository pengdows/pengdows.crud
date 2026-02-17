// =============================================================================
// FILE: TableGateway.Audit.cs
// PURPOSE: Audit field handling for CreatedBy/On and LastUpdatedBy/On columns.
//
// AI SUMMARY:
// - SetAuditFields() populates audit columns during Create and Update:
//   * On Create: Sets CreatedBy, CreatedOn, LastUpdatedBy, LastUpdatedOn
//   * On Update: Sets only LastUpdatedBy, LastUpdatedOn
// - Requires AuditValueResolver if entity has user audit columns (CreatedBy/LastUpdatedBy).
// - Time-only audit (CreatedOn/LastUpdatedOn) works without resolver (uses UTC now).
// - Supports both DateTime and DateTimeOffset timestamp properties.
// - Uses compiled delegates (GetOrCreateSetter / FastGetter) instead of raw reflection.
// - Coerce() helper handles type conversion for audit values:
//   * String to Guid parsing
//   * Culture-invariant numeric conversion
// - Throws InvalidOperationException if user audit columns exist but no resolver provided.
// - Skips audit processing if entity has no audit columns (performance optimization).
// =============================================================================

using System.Globalization;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: Audit field population logic.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
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
    /// Converts a DateTime to the correct boxed type for the target property.
    /// </summary>
    private static object CoerceTimestamp(DateTime utcNow, Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        return utcNow;
    }

    private void SetAuditFields(TEntity obj, bool updateOnly)
    {
        if (obj == null)
        {
            return;
        }

        // Skip resolving audit values when no audit columns are present
        if (!_hasAuditColumns)
        {
            return;
        }

        // Check if we have user-based audit fields (non-time fields)
        var hasUserAuditFields = _tableInfo.CreatedBy != null || _tableInfo.LastUpdatedBy != null;

        // If user-based audit fields exist but no resolver is configured, this is a usage error.
        // Tests expect an InvalidOperationException rather than letting the database fail later.
        if (hasUserAuditFields && _auditValueResolver is null)
        {
            throw new InvalidOperationException("AuditValues resolver is required for user-based audit fields.");
        }

        var auditValues = _auditValueResolver?.Resolve();

        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;

        // Handle LastUpdated fields
        if (_tableInfo.LastUpdatedOn?.PropertyInfo != null)
        {
            var setter = GetOrCreateSetter(_tableInfo.LastUpdatedOn.PropertyInfo);
            var coercedTime = CoerceTimestamp(utcNow, _tableInfo.LastUpdatedOn.PropertyInfo.PropertyType);
            setter(obj, coercedTime);
        }

        if (_tableInfo.LastUpdatedBy?.PropertyInfo != null && auditValues != null)
        {
            var coercedUserId = Coerce(auditValues!.UserId, _tableInfo.LastUpdatedBy.PropertyInfo.PropertyType);
            var setter = GetOrCreateSetter(_tableInfo.LastUpdatedBy.PropertyInfo);
            setter(obj, coercedUserId);
        }
        // When no resolver is provided, we've already thrown above if user audit fields exist

        if (updateOnly)
        {
            return;
        }

        // Handle Created fields (only for new entities)
        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj);
            if (IsDefaultTimestamp(currentValue))
            {
                var setter = GetOrCreateSetter(_tableInfo.CreatedOn.PropertyInfo);
                var coercedTime = CoerceTimestamp(utcNow, _tableInfo.CreatedOn.PropertyInfo.PropertyType);
                setter(obj, coercedTime);
            }
        }

        if (_tableInfo.CreatedBy?.PropertyInfo != null && auditValues != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
            {
                var coercedUserId = Coerce(auditValues.UserId, _tableInfo.CreatedBy.PropertyInfo.PropertyType);
                var setter = GetOrCreateSetter(_tableInfo.CreatedBy.PropertyInfo);
                setter(obj, coercedUserId);
            }
        }
    }
}
