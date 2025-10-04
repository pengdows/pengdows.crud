using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.wrappers;

namespace pengdows.crud;

public partial class EntityHelper<TEntity, TRowID>
{
    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var obj = new TEntity();

        var plan = GetOrBuildRecordsetPlan(reader);
        for (var idx = 0; idx < plan.Length; idx++)
        {
            plan[idx].Apply(reader, obj);
        }

        return obj;
    }

    private ColumnPlan[] GetOrBuildRecordsetPlan(ITrackedReader reader)
    {
        // Compute a stronger hash for the recordset shape: field count + names + types
        var fieldCount = reader.FieldCount;
        var hash = fieldCount * 397;
        for (var i = 0; i < fieldCount; i++)
        {
            var name = reader.GetName(i);
            hash = unchecked(hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(name));

            // Include field type to strengthen hash and avoid shape collisions
            var fieldType = reader.GetFieldType(i);
            hash = unchecked(hash * 31 + fieldType.GetHashCode());
        }

        // Try to get existing plan from thread-safe cache
        if (_readerPlans.TryGet(hash, out var existingPlan))
        {
            return existingPlan;
        }

        // Build new optimized plan with pre-compiled delegates
        var list = new List<ColumnPlan>(fieldCount);
        for (var i = 0; i < fieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (_columnsByNameCI.TryGetValue(colName, out var column))
            {
                var ordinal = i;
                var fieldType = reader.GetFieldType(ordinal);
                var targetType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ?? column.PropertyInfo.PropertyType;

                // Build optimized delegates
                var valueExtractor = BuildValueExtractor(fieldType);
                var coercer = BuildCoercer(column, fieldType, targetType);
                var setter = GetOrCreateSetter(column.PropertyInfo);

                list.Add(new ColumnPlan(ordinal, valueExtractor, coercer, setter, column.PropertyInfo.PropertyType));
            }
        }

        var plan = list.ToArray();
        return _readerPlans.GetOrAdd(hash, _ => plan);
    }

    // Pre-compiled value extractors for optimal performance
    private static Func<ITrackedReader, int, object?> BuildValueExtractor(Type fieldType)
    {
        return Type.GetTypeCode(fieldType) switch
        {
            TypeCode.Int32 => (r, i) => r.GetInt32(i),
            TypeCode.Int64 => (r, i) => r.GetInt64(i),
            TypeCode.String => (r, i) => r.GetString(i),
            TypeCode.DateTime => (r, i) => r.GetDateTime(i),
            TypeCode.Decimal => (r, i) => r.GetDecimal(i),
            TypeCode.Boolean => (r, i) => r.GetBoolean(i),
            TypeCode.Int16 => (r, i) => r.GetInt16(i),
            TypeCode.Byte => (r, i) => r.GetByte(i),
            TypeCode.Double => (r, i) => r.GetDouble(i),
            TypeCode.Single => (r, i) => r.GetFloat(i),
            _ when fieldType == typeof(Guid) => (r, i) => r.GetGuid(i),
            _ when fieldType == typeof(byte[]) => (r, i) => ((DbDataReader)r).GetFieldValue<byte[]>(i),
            _ => (r, i) => r.GetValue(i)
        };
    }

    // Pre-compiled coercers - null if no coercion needed
    private Func<object?, object?>? BuildCoercer(IColumnInfo column, Type fieldType, Type targetType)
    {
        // No coercion needed if types match and no special handling required
        if (targetType.IsAssignableFrom(fieldType) && !column.IsJsonType && column.EnumType == null)
        {
            return null;
        }

        return value =>
        {
            if (value == null)
            {
                return null;
            }

            var runtimeType = value.GetType();

            try
            {
                return TypeCoercionHelper.Coerce(value, runtimeType, column, EnumParseBehavior, _coercionOptions);
            }
            catch (JsonException ex)
            {
                Logger.LogDebug(ex, "Failed to deserialize JSON for column {Column}", column.Name);
                return null;
            }
        };
    }

    public Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
        // The generated setter casts directly; a database NULL assigned to a non-nullable property will throw.
        // This fail-fast behavior surfaces unexpected schema mismatches immediately.
        return _propertySetters.GetOrAdd(prop, p =>
        {
            var objParam = Expression.Parameter(typeof(object));
            var valueParam = Expression.Parameter(typeof(object));

            var castObj = Expression.Convert(objParam, p.DeclaringType!);
            var castValue = Expression.Convert(valueParam, p.PropertyType);

            var propertyAccess = Expression.Property(castObj, p);
            var assignment = Expression.Assign(propertyAccess, castValue);

            var lambda = Expression.Lambda<Action<object, object?>>(assignment, objParam, valueParam);
            return lambda.Compile();
        });
    }
}
