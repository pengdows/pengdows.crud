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
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// BaseTableGateway partial: DataReader mapping to entities.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    // Hot path cache: most recently used plan to avoid hash/dictionary overhead
    private HybridRecordsetPlan? _hotPlan;
    private RecordsetShape _hotShape;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var plan = GetOrBuildRecordsetPlan(reader);
        return MapReaderToObjectWithPlan(reader, plan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TEntity MapReaderToObjectWithPlan(ITrackedReader reader, HybridRecordsetPlan plan)
    {
        return plan.CompiledMapper(reader);
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

            var hotPlan = Volatile.Read(ref _hotPlan);
            if (hotPlan != null && _hotShape.Equals(lookupShape))
            {
                return hotPlan;
            }

            if (_readerPlans.TryGet(lookupShape, out var existingPlan))
            {
                _hotShape = lookupShape.Persist();
                Volatile.Write(ref _hotPlan, existingPlan);
                return existingPlan;
            }

            var compiledMapper = CompiledMapperFactory<TEntity>.Create(reader, _columnsByNameCI, EnumParseBehavior, names, fieldTypes);
            var plan = new HybridRecordsetPlan(compiledMapper);

            // The key must outlive this call (the rented arrays are returned below), so persist
            // a copy before inserting.
            var persistedShape = lookupShape.Persist();
            var added = _readerPlans.GetOrAdd(persistedShape, _ => plan);
            _hotShape = persistedShape;
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
