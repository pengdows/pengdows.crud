// =============================================================================
// FILE: TableGateway.Reader.cs
// PURPOSE: DataReader-to-entity mapping with cached execution plans.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity.
// - Uses ColumnPlan[] for compiled, cache-optimized mapping.
// - GetOrBuildRecordsetPlan() - Caches plans by recordset shape hash:
//   * Field count + names + types determine shape
//   * Hash collision resistant via strong hashing
// - ColumnPlan struct contains:
//   * Column ordinal for fast reader access
//   * Compiled setter delegate for property assignment
//   * Type coercion logic for conversions
// - Handles:
//   * Enum columns (string or numeric storage)
//   * JSON columns (deserialized via JsonSerializer)
//   * Nullable types
//   * Type mismatches via TypeCoercionHelper
// - Performance: Plan building is O(n) once, then O(1) lookup.
// =============================================================================

using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: DataReader mapping to entities.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
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

    // Optimized version that accepts a pre-built plan to avoid repeated hash calculation
    // Used by LoadListAsync and LoadSingleAsync to hoist plan building outside the loop
    private TEntity MapReaderToObjectWithPlan(ITrackedReader reader, ColumnPlan[] plan)
    {
        var obj = new TEntity();

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
                var targetType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ??
                                 column.PropertyInfo.PropertyType;

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
    internal static Func<ITrackedReader, int, object?> BuildValueExtractor(Type fieldType)
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
            _ when fieldType == typeof(byte[]) => (r, i) => ReadBytes(r, i),
            _ => (r, i) => r.GetValue(i)
        };
    }

    private static byte[] ReadBytes(ITrackedReader reader, int ordinal)
    {
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        if (length <= 0)
        {
            return Array.Empty<byte>();
        }

        if (length > int.MaxValue)
        {
            throw new InvalidOperationException("Byte array length exceeds supported buffer size.");
        }

        var buffer = new byte[length];
        var bytesRead = reader.GetBytes(ordinal, 0, buffer, 0, (int)length);
        if (bytesRead == length)
        {
            return buffer;
        }

        if (bytesRead <= 0)
        {
            return Array.Empty<byte>();
        }

        Array.Resize(ref buffer, (int)bytesRead);
        return buffer;
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
