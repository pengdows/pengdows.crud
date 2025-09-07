using System;
using System.Globalization;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    /// <summary>
    /// Type-safe coercion for audit field values (handles string to Guid, etc.)
    /// </summary>
    private static object? Coerce(object? value, Type targetType)
    {
        if (value is null) return null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(value)) return value;
        if (t == typeof(Guid) && value is string s) return Guid.Parse(s);
        return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
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
        var hasTimeAuditFields = _tableInfo.CreatedOn != null || _tableInfo.LastUpdatedOn != null;

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
            _tableInfo.LastUpdatedOn.PropertyInfo.SetValue(obj, utcNow);
        }

        if (_tableInfo.LastUpdatedBy?.PropertyInfo != null && auditValues != null)
        {
            var coercedUserId = Coerce(auditValues!.UserId, _tableInfo.LastUpdatedBy.PropertyInfo.PropertyType);
            _tableInfo.LastUpdatedBy.PropertyInfo.SetValue(obj, coercedUserId);
        }
        // When no resolver is provided, we've already thrown above if user audit fields exist

        if (updateOnly)
        {
            return;
        }

        // Handle Created fields (only for new entities)
        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
            {
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, utcNow);
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
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, coercedUserId);
            }
        }
    }
}
