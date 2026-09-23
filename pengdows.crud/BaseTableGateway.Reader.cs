// =============================================================================
// FILE: BaseTableGateway.Reader.cs
// PURPOSE: Monolithic DataReader-to-entity mapping using compiled expression trees.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity using a compiled plan.
// - Caches plans by recordset shape hash (long).
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
    private long _hotHash;

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
            var hashBuilder = new HashCode();
            hashBuilder.Add(fieldCount);

            for (var i = 0; i < fieldCount; i++)
            {
                names[i] = reader.GetName(i);
                fieldTypes[i] = reader.GetFieldType(i);
                hashBuilder.Add(names[i], StringComparer.OrdinalIgnoreCase);
                hashBuilder.Add(fieldTypes[i]);
            }

            var hash = (long)hashBuilder.ToHashCode();

            var hotPlan = Volatile.Read(ref _hotPlan);
            if (hotPlan != null && hash == _hotHash)
            {
                return hotPlan;
            }

            if (_readerPlans.TryGet(hash, out var existingPlan))
            {
                _hotHash = hash;
                Volatile.Write(ref _hotPlan, existingPlan);
                return existingPlan;
            }

            var compiledMapper = CompiledMapperFactory<TEntity>.Create(reader, _columnsByNameCI, EnumParseBehavior, names, fieldTypes);
            var plan = new HybridRecordsetPlan(compiledMapper);

            var added = _readerPlans.GetOrAdd(hash, _ => plan);
            _hotHash = hash;
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
