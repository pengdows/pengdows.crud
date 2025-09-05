using System.Globalization;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
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

        if (!_hasAuditColumns)
        {
            return;
        }

        var hasUserAuditFields = _tableInfo.CreatedBy != null || _tableInfo.LastUpdatedBy != null;
        var hasTimeAuditFields = _tableInfo.CreatedOn != null || _tableInfo.LastUpdatedOn != null;
        var auditValues = _auditValueResolver?.Resolve();

        if (auditValues == null && hasUserAuditFields)
        {
            throw new InvalidOperationException("No AuditValues could be found by the resolver.");
        }
        var utcNow = auditValues?.UtcNow ?? DateTime.UtcNow;

        if (_tableInfo.LastUpdatedOn?.PropertyInfo != null)
        {
            _tableInfo.LastUpdatedOn.PropertyInfo.SetValue(obj, utcNow);
        }

        if (_tableInfo.LastUpdatedBy?.PropertyInfo != null && auditValues != null)
        {
            var coercedUserId = Coerce(auditValues!.UserId, _tableInfo.LastUpdatedBy.PropertyInfo.PropertyType);
            _tableInfo.LastUpdatedBy.PropertyInfo.SetValue(obj, coercedUserId);
        }
        else if (_tableInfo.LastUpdatedBy?.PropertyInfo != null)
        {
            var current = _tableInfo.LastUpdatedBy.PropertyInfo.GetValue(obj) as string;
            if (string.IsNullOrEmpty(current))
            {
                var coercedSystem = Coerce("system", _tableInfo.LastUpdatedBy.PropertyInfo.PropertyType);
                _tableInfo.LastUpdatedBy.PropertyInfo.SetValue(obj, coercedSystem);
            }
        }

        if (updateOnly)
        {
            return;
        }

        if (_tableInfo.CreatedOn?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedOn.PropertyInfo.GetValue(obj) as DateTime?;
            if (currentValue == null || currentValue == default(DateTime))
            {
                _tableInfo.CreatedOn.PropertyInfo.SetValue(obj, utcNow);
            }
        }

        if (_tableInfo.CreatedBy?.PropertyInfo != null)
        {
            var currentValue = _tableInfo.CreatedBy.PropertyInfo.GetValue(obj);
            if (currentValue == null
                || currentValue as string == string.Empty
                || Utils.IsZeroNumeric(currentValue)
                || (currentValue is Guid guid && guid == Guid.Empty))
            {
                var coercedUserId = Coerce(auditValues!.UserId, _tableInfo.CreatedBy.PropertyInfo.PropertyType);
                _tableInfo.CreatedBy.PropertyInfo.SetValue(obj, coercedUserId);
            }
        }
    }
}

