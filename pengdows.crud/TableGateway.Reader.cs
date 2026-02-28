// =============================================================================
// FILE: TableGateway.Reader.cs
// PURPOSE: Monolithic DataReader-to-entity mapping using compiled expression trees.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity using a monolithic plan.
// - Caches plans by recordset shape hash (long).
// - GetOrBuildRecordsetPlan() - Builds a single compiled Func<ITrackedReader, TEntity>
//   per unique schema shape, eliminating loop-over-delegates and boxing.
// - Uses CompiledMapperFactory to unroll IsDBNull checks and inlines coercion.
// - Performance: Zero per-row delegate dispatch, near-manual mapping speed.
// =============================================================================

using System.Buffers;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// TableGateway partial: DataReader mapping to entities.
/// </summary>
public partial class TableGateway<TEntity, TRowID>
{
    private const int FieldPoolMaxLength = 64;
    private const int FieldPoolArraysPerBucket = 32;

    private static readonly ArrayPool<string> FieldNamePool =
        ArrayPool<string>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    private static readonly ArrayPool<Type> FieldTypePool =
        ArrayPool<Type>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TEntity MapReaderToObject(ITrackedReader reader)
    {
        var plan = GetOrBuildRecordsetPlan(reader);
        return MapReaderToObjectWithPlan(reader, plan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TEntity MapReaderToObjectWithPlan(ITrackedReader reader, HybridRecordsetPlan plan)
    {
        // Execute the monolithic compiled mapper (zero delegate dispatch overhead)
        return plan.CompiledMapper(reader);
    }

    private HybridRecordsetPlan GetOrBuildRecordsetPlan(ITrackedReader reader)
    {
        var fieldCount = reader.FieldCount;

        // Use ArrayPool to reduce allocations during hash calculation
        var names = RentStringArray(fieldCount);
        var fieldTypes = RentTypeArray(fieldCount);

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

            if (_readerPlans.TryGet(hash, out var existingPlan))
            {
                return existingPlan;
            }

            // Build monolithic compiled mapper using factory
            var compiledMapper = CompiledMapperFactory<TEntity>.Create(reader, _columnsByNameCI, EnumParseBehavior, names, fieldTypes);
            var plan = new HybridRecordsetPlan(compiledMapper);

            return _readerPlans.GetOrAdd(hash, _ => plan);
        }
        finally
        {
            ReturnStringArray(names, fieldCount);
            ReturnTypeArray(fieldTypes, fieldCount);
        }
    }

    private static string[] RentStringArray(int size)
    {
        return size <= FieldPoolMaxLength
            ? FieldNamePool.Rent(size)
            : ArrayPool<string>.Shared.Rent(size);
    }

    private static void ReturnStringArray(string[] array, int size)
    {
        if (size <= FieldPoolMaxLength)
        {
            FieldNamePool.Return(array, clearArray: true);
        }
        else
        {
            ArrayPool<string>.Shared.Return(array, clearArray: true);
        }
    }

    private static Type[] RentTypeArray(int size)
    {
        return size <= FieldPoolMaxLength
            ? FieldTypePool.Rent(size)
            : ArrayPool<Type>.Shared.Rent(size);
    }

    private static void ReturnTypeArray(Type[] array, int size)
    {
        if (size <= FieldPoolMaxLength)
        {
            FieldTypePool.Return(array, clearArray: false);
        }
        else
        {
            ArrayPool<Type>.Shared.Return(array, clearArray: false);
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
