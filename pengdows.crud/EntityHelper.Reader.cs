using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
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
            var p = plan[idx];
            var raw = reader.IsDBNull(p.Ordinal) ? null : reader.GetValue(p.Ordinal);
            var coerced = p.Converter(raw);
            try
            {
                p.Setter(obj, coerced);
            }
            catch (Exception ex)
            {
                var name = reader.GetName(p.Ordinal);
                throw new InvalidValueException(
                    $"Unable to set property from value that was stored in the database: {name} :{ex.Message}");
            }
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

        // Build new plan
        var list = new List<ColumnPlan>(fieldCount);
        for (var i = 0; i < fieldCount; i++)
        {
            var colName = reader.GetName(i);
            if (_columnsByNameCI.TryGetValue(colName, out var column))
            {
                var setter = GetOrCreateSetter(column.PropertyInfo);
                var converter = GetOrCreateReaderConverter(column);
                list.Add(new ColumnPlan(i, setter, converter));
            }
        }

        var plan = list.ToArray();

        return _readerPlans.GetOrAdd(hash, _ => plan);
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
