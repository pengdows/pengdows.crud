// =============================================================================
// FILE: BaseTableGateway.Reader.cs
// PURPOSE: Monolithic DataReader-to-entity mapping using compiled expression trees.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity using a compiled plan.
// - Caches plans by recordset shape (RecordsetShape: field names/types with structural
//   equality) — not a bare hash — so a hash collision between two different shapes can never
//   silently reuse the wrong compiled mapper.
// - Shared by all gateway variants.
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
/// BaseTableGateway partial: DataReader mapping to entities.
/// </summary>
public abstract partial class BaseTableGateway<TEntity>
{
    private const int FieldPoolMaxLength = 64;
    private const int FieldPoolArraysPerBucket = 32;

    private static readonly ArrayPool<string> FieldNamePool =
        ArrayPool<string>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    private static readonly ArrayPool<Type> FieldTypePool =
        ArrayPool<Type>.Create(FieldPoolMaxLength, FieldPoolArraysPerBucket);

    // Hot path cache: most recently used plan to avoid hash/dictionary overhead
    private HybridRecordsetPlan? _hotPlan;
    private RecordsetShape _hotShape;

    // CORE-013: plans were previously keyed directly by a 32-bit HashCode widened to long, with
    // no verification that a hash hit actually came from the same schema. Two different
    // projections/shapes for the same entity that happened to hash-collide would silently reuse
    // the wrong compiled mapper — a positional mismatch that can throw an InvalidCastException or,
    // worse, silently assign the wrong value to the wrong property. Keying by this
    // structurally-equatable shape instead lets ConcurrentDictionary's own correct collision
    // handling (hash to find the bucket, Equals to confirm the entry) do what it already
    // guarantees, rather than trusting a bare hash value as if it were unique.
    private readonly struct RecordsetShape : IEquatable<RecordsetShape>
    {
        private readonly string[] _names;
        private readonly Type[] _types;
        private readonly int _fieldCount;

        // Lookup-only constructor: wraps caller-owned (possibly pooled/oversized/rented) arrays.
        // Never persisted as a dictionary key — see Persist().
        public RecordsetShape(string[] names, Type[] types, int fieldCount)
        {
            _names = names;
            _types = types;
            _fieldCount = fieldCount;
        }

        /// <summary>
        /// Returns a copy safe to store as a cache key — the rented arrays backing a lookup-only
        /// instance get returned to the pool (and their contents cleared/reused) as soon as
        /// GetOrBuildRecordsetPlan's finally block runs.
        /// </summary>
        public RecordsetShape Persist()
        {
            var names = new string[_fieldCount];
            var types = new Type[_fieldCount];
            Array.Copy(_names, names, _fieldCount);
            Array.Copy(_types, types, _fieldCount);
            return new RecordsetShape(names, types, _fieldCount);
        }

        public bool Equals(RecordsetShape other)
        {
            if (_fieldCount != other._fieldCount)
            {
                return false;
            }

            for (var i = 0; i < _fieldCount; i++)
            {
                if (_types[i] != other._types[i])
                {
                    return false;
                }

                if (!string.Equals(_names[i], other._names[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is RecordsetShape other && Equals(other);

        public override int GetHashCode()
        {
            var hashBuilder = new HashCode();
            hashBuilder.Add(_fieldCount);
            for (var i = 0; i < _fieldCount; i++)
            {
                hashBuilder.Add(_names[i], StringComparer.OrdinalIgnoreCase);
                hashBuilder.Add(_types[i]);
            }

            return hashBuilder.ToHashCode();
        }
    }

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

        var names = RentStringArray(fieldCount);
        var fieldTypes = RentTypeArray(fieldCount);

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

            // Cache miss: the key must outlive this call, so persist a copy before inserting —
            // the lookup shape's backing arrays get returned to the pool in the finally below.
            var persistedShape = lookupShape.Persist();
            var added = _readerPlans.GetOrAdd(persistedShape, _ => plan);
            _hotShape = persistedShape;
            Volatile.Write(ref _hotPlan, added);

            return added;
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
