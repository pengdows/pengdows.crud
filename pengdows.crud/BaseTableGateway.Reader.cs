// =============================================================================
// FILE: BaseTableGateway.Reader.cs
// PURPOSE: Monolithic DataReader-to-entity mapping using compiled expression trees.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity using a compiled plan.
// - Caches plans by recordset shape (RecordsetShape: field names/types with structural
//   equality), not a bare hash, so a hash collision can never reuse the wrong mapper.
// - Shared by all gateway variants.
// =============================================================================

using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using pengdows.crud.@internal;
using pengdows.crud.exceptions;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: DataReader mapping to entities.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    // Hot path cache: most recently used plan (which carries its own shape) to avoid hash/dictionary
    // overhead. One reference, so a racing load can never pair one shape with another's plan.
    private HybridRecordsetPlan? _hotPlan;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var plan = GetOrBuildRecordsetPlan(reader);
        return MapReaderToObjectWithPlan(reader, plan);
    }

    private TEntity MapReaderToObjectWithPlan(ITrackedReader reader, HybridRecordsetPlan plan)
    {
        try
        {
            return plan.CompiledMapper(reader);
        }
        catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException)
        {
            // TYPE-008: a stored value the property can't hold (e.g. a NUMERIC above
            // decimal.MaxValue) surfaced as whatever the provider threw. Report it as a mapping
            // failure naming the column; never a truncated or default value.
            throw CreateMappingException(reader, ex);
        }
    }

    // Slow path, only after a failure: find the column that can't be read into its property.
    private DataMappingException CreateMappingException(ITrackedReader reader, Exception failure)
    {
        IColumnInfo? failing = null;
        for (var i = 0; i < reader.FieldCount && failing == null; i++)
        {
            if (!_columnsByNameCI.TryGetValue(reader.GetName(i), out var column))
            {
                continue;
            }

            try
            {
                var value = reader.GetValue(i);
                if (value is not null && value is not DBNull)
                {
                    TypeCoercionHelper.Coerce(value, value.GetType(), column.PropertyInfo.PropertyType);
                }
            }
            catch (Exception)
            {
                failing = column;
            }
        }

        var target = failing == null
            ? $"a column into {typeof(TEntity).Name}"
            : $"column '{failing.Name}' into {typeof(TEntity).Name}.{failing.PropertyInfo.Name} " +
              $"({failing.PropertyInfo.PropertyType.Name})";
        return new DataMappingException($"Could not read {target}: {failure.Message}",
            enums.SupportedDatabase.Unknown, failure);
    }

    private HybridRecordsetPlan GetOrBuildRecordsetPlan(ITrackedReader reader)
    {
        var fieldCount = reader.FieldCount;

        var names = RecordsetFieldArrayPool.RentStringArray(fieldCount);
        var fieldTypes = RecordsetFieldArrayPool.RentTypeArray(fieldCount);

        try
        {
            for (var i = 0; i < fieldCount; i++)
            {
                names[i] = reader.GetName(i);
                fieldTypes[i] = reader.GetFieldType(i);
            }

            // Lookup-only: backed by the rented arrays above, never stored as a dictionary key.
            var lookupShape = new RecordsetShape(names, fieldTypes, fieldCount);

            // The reading context's options, not the gateway's own: a singleton gateway reads
            // through tenant contexts on other databases (REV-033). A reader that wasn't opened by
            // a context carries Default, and then the gateway's own options apply.
            var options = reader is IInternalTrackedReader internalReader &&
                          !ReferenceEquals(internalReader.CoercionOptions, TypeCoercionOptions.Default)
                ? internalReader.CoercionOptions
                : _coercionOptions;

            var hotPlan = Volatile.Read(ref _hotPlan);
            if (hotPlan != null && ReferenceEquals(hotPlan.Options, options) && hotPlan.Shape.Equals(lookupShape))
            {
                return hotPlan;
            }

            if (_readerPlans.TryGet(new ReaderPlanKey(lookupShape, options), out var existingPlan))
            {
                Volatile.Write(ref _hotPlan, existingPlan);
                return existingPlan;
            }

            // The key must outlive this call (the rented arrays are returned below), so persist
            // a copy before inserting.
            var persistedShape = lookupShape.Persist();
            var compiledMapper = CompiledMapperFactory<TEntity>.Create(reader, _columnsByNameCI, EnumParseBehavior, names, fieldTypes,
                coercionOptions: options);
            var plan = new HybridRecordsetPlan(compiledMapper, persistedShape, options);
            var added = _readerPlans.GetOrAdd(new ReaderPlanKey(persistedShape, options), _ => plan);
            Volatile.Write(ref _hotPlan, added);

            return added;
        }
        finally
        {
            RecordsetFieldArrayPool.ReturnStringArray(names, fieldCount);
            RecordsetFieldArrayPool.ReturnTypeArray(fieldTypes, fieldCount);
        }
    }

    public Action<object, object?> GetOrCreateSetter(PropertyInfo prop)
    {
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
