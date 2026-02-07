// =============================================================================
// FILE: TableGateway.Reader.cs
// PURPOSE: DataReader-to-entity mapping with cached execution plans.
//
// AI SUMMARY:
// - MapReaderToObject() - Converts current DataReader row to TEntity.
// - Uses ColumnPlan[] for compiled, cache-optimized mapping.
// - GetOrBuildRecordsetPlan() - Caches plans by recordset shape hash:
//   * Hash key is long; computed in a single pass over field count + names + types
//   * Names and field-type arrays are allocated once in that pass and reused
//     by the plan-build loop — no duplicate GetName/GetFieldType calls
//   * Plans cached in a BoundedCache<long, ColumnPlan[]> keyed by shape hash
// - ColumnPlan struct contains:
//   * Column ordinal for fast reader access
//   * Compiled setter delegate for property assignment
//   * Type coercion delegate, resolved once at plan-build time via
//     TypeCoercionHelper.ResolveCoercer (no per-row registry lookups)
// - Handles:
//   * Enum columns (string or numeric storage)
//   * JSON columns (deserialized via JsonSerializer)
//   * Nullable types
//   * Type mismatches via TypeCoercionHelper
// - Performance: Plan building is O(n) once, then O(1) lookup.
// =============================================================================

using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using pengdows.crud.exceptions;
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

    // Optimized version that accepts a pre-built plan to avoid repeated hash calculation
    // Used by LoadListAsync and LoadSingleAsync to hoist plan building outside the loop
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TEntity MapReaderToObjectWithPlan(ITrackedReader reader, HybridRecordsetPlan plan)
    {
        // Fast path: Use compiled mapper for all direct columns (zero delegate overhead)
        var obj = plan.CompiledMapper != null
            ? plan.CompiledMapper(reader)
            : new TEntity();

        // Slow path: Apply coercion plans for complex columns (JSON, enums, etc.)
        if (plan.CoercionPlans != null)
        {
            foreach (var coercionPlan in plan.CoercionPlans)
            {
                coercionPlan.Apply(reader, obj);
            }
        }

        return obj;
    }

    private HybridRecordsetPlan GetOrBuildRecordsetPlan(ITrackedReader reader)
    {
        var fieldCount = reader.FieldCount;

        // Single pass: collect names and field-types into local arrays while
        // computing the shape hash.  The plan-build loop below reuses these
        // arrays, so GetName / GetFieldType are called exactly once per column
        // regardless of whether the plan is already cached.
        // Use ArrayPool to reduce allocations (5-10% improvement for high-throughput queries)
        var names = RentStringArray(fieldCount);
        var fieldTypes = RentTypeArray(fieldCount);

        try
        {
            // Use HashCode struct for better hash distribution (SIMD-optimized)
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

            // Build hybrid plan: separate direct columns from coercion columns
            var directColumns = new List<(int ordinal, IColumnInfo column, Type fieldType)>(fieldCount);
            var coercionPlans = new List<CoercedColumnPlan>(fieldCount);

            for (var i = 0; i < fieldCount; i++)
            {
                if (_columnsByNameCI.TryGetValue(names[i], out var column))
                {
                    var fieldType = fieldTypes[i];
                    var targetType = Nullable.GetUnderlyingType(column.PropertyInfo.PropertyType) ??
                                     column.PropertyInfo.PropertyType;

                    // byte[] requires special GetBytes handling - always use delegate path
                    if (fieldType == typeof(byte[]))
                    {
                        var valueExtractor = BuildValueExtractor(fieldType);
                        var setter = GetOrCreateSetter(column.PropertyInfo);
                        coercionPlans.Add(new CoercedColumnPlan(i, valueExtractor, val => val, setter, column.PropertyInfo.PropertyType));
                        continue;
                    }

                    // Check if we need complex coercion (JSON, enums, complex type conversion)
                    var coercer = BuildCoercer(column, fieldType, targetType);

                    // Simple numeric conversions and exact matches can be compiled inline
                    var canCompileInline = coercer == null || IsSimpleConversion(fieldType, targetType);

                    if (canCompileInline)
                    {
                        // Direct match or simple conversion - compile into expression tree
                        directColumns.Add((i, column, fieldType));
                    }
                    else
                    {
                        // Complex handling required - use delegate-based plan
                        var valueExtractor = BuildValueExtractor(fieldType);
                        var setter = GetOrCreateSetter(column.PropertyInfo);
                        coercionPlans.Add(new CoercedColumnPlan(i, valueExtractor, coercer!, setter, column.PropertyInfo.PropertyType));
                    }
                }
            }

            // Build compiled mapper for direct columns (zero delegate overhead)
            Func<ITrackedReader, TEntity>? compiledMapper = null;
            if (directColumns.Count > 0)
            {
                compiledMapper = BuildCompiledMapper(directColumns);
            }

            var plan = new HybridRecordsetPlan(
                compiledMapper,
                coercionPlans.Count > 0 ? coercionPlans.ToArray() : null
            );

            return _readerPlans.GetOrAdd(hash, _ => plan);
        }
        finally
        {
            // Always return arrays to pool, even if an exception occurs
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

    // Check if a type conversion can be compiled inline (vs requiring delegate-based coercion)
    private static bool IsSimpleConversion(Type fieldType, Type targetType)
    {
        // Exact match
        if (fieldType == targetType)
        {
            return true;
        }

        // Common database type mismatches that can be handled with Expression.Convert
        // SQLite: INTEGER → Int64, but we often want Int32, Int16, Byte, or Boolean
        if (fieldType == typeof(long))
        {
            return targetType == typeof(int) ||
                   targetType == typeof(short) ||
                   targetType == typeof(byte) ||
                   targetType == typeof(bool);
        }

        // SQLite: REAL → Double, but we often want Decimal or Single
        if (fieldType == typeof(double))
        {
            return targetType == typeof(decimal) ||
                   targetType == typeof(float);
        }

        // Other simple numeric widening/narrowing
        if (fieldType == typeof(int) && targetType == typeof(long))
        {
            return true;
        }

        return false;
    }

    // Build a single compiled expression for all direct-match columns (Dapper-style)
    // This eliminates ALL delegate dispatch overhead for the common case
    private Func<ITrackedReader, TEntity> BuildCompiledMapper(
        List<(int ordinal, IColumnInfo column, Type fieldType)> directColumns)
    {
        var readerParam = Expression.Parameter(typeof(ITrackedReader), "reader");
        var entityVar = Expression.Variable(typeof(TEntity), "entity");

        var expressions = new List<Expression>
        {
            // var entity = new TEntity();
            Expression.Assign(entityVar, Expression.New(typeof(TEntity)))
        };

        foreach (var (ordinal, column, fieldType) in directColumns)
        {
            // Generate: if (!reader.IsDBNull(ordinal)) { entity.Property = ConvertedValue(reader.GetXxx(ordinal)); }
            var ordinalExpr = Expression.Constant(ordinal);
            var isDbNullMethod = typeof(IDataRecord).GetMethod(nameof(IDataRecord.IsDBNull))!;
            var notDbNull = Expression.Not(Expression.Call(readerParam, isDbNullMethod, ordinalExpr));

            // Get the appropriate reader.GetXxx method
            var getMethod = GetReaderMethod(fieldType);
            var getValue = Expression.Call(readerParam, getMethod, ordinalExpr);

            // Build the conversion expression
            var targetType = column.PropertyInfo.PropertyType;
            var finalValue = BuildConversionExpression(getValue, fieldType, targetType);

            // entity.Property = finalValue
            var propertyAccess = Expression.Property(entityVar, column.PropertyInfo);
            var assignment = Expression.Assign(propertyAccess, finalValue);

            // Wrap in null check: if (!reader.IsDBNull(ordinal)) { assignment }
            var conditionalAssignment = Expression.IfThen(notDbNull, assignment);
            expressions.Add(conditionalAssignment);
        }

        // return entity;
        expressions.Add(entityVar);

        var block = Expression.Block(
            new[] { entityVar },
            expressions
        );

        return Expression.Lambda<Func<ITrackedReader, TEntity>>(block, readerParam).Compile();
    }

    // Build an inline conversion expression for simple type conversions
    private static Expression BuildConversionExpression(Expression value, Type sourceType, Type targetType)
    {
        // Handle nullable target types
        var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Exact match - no conversion needed
        if (sourceType == underlyingTargetType)
        {
            return targetType != underlyingTargetType
                ? Expression.Convert(value, targetType) // Wrap in Nullable<T>
                : value;
        }

        // Int64 → Boolean: value != 0
        if (sourceType == typeof(long) && underlyingTargetType == typeof(bool))
        {
            var notEqualZero = Expression.NotEqual(value, Expression.Constant(0L));
            return targetType != underlyingTargetType
                ? Expression.Convert(notEqualZero, targetType)
                : notEqualZero;
        }

        // Int64 → Int32/Int16/Byte: checked cast
        if (sourceType == typeof(long) &&
            (underlyingTargetType == typeof(int) ||
             underlyingTargetType == typeof(short) ||
             underlyingTargetType == typeof(byte)))
        {
            var converted = Expression.ConvertChecked(value, underlyingTargetType);
            return targetType != underlyingTargetType
                ? Expression.Convert(converted, targetType)
                : converted;
        }

        // Double → Decimal: explicit conversion
        if (sourceType == typeof(double) && underlyingTargetType == typeof(decimal))
        {
            var decimalCtor = typeof(decimal).GetConstructor(new[] { typeof(double) })!;
            var converted = Expression.New(decimalCtor, value);
            return targetType != underlyingTargetType
                ? Expression.Convert(converted, targetType)
                : converted;
        }

        // Double → Single: explicit cast
        if (sourceType == typeof(double) && underlyingTargetType == typeof(float))
        {
            var converted = Expression.Convert(value, underlyingTargetType);
            return targetType != underlyingTargetType
                ? Expression.Convert(converted, targetType)
                : converted;
        }

        // Int32 → Int64: widening (safe)
        if (sourceType == typeof(int) && underlyingTargetType == typeof(long))
        {
            var converted = Expression.Convert(value, underlyingTargetType);
            return targetType != underlyingTargetType
                ? Expression.Convert(converted, targetType)
                : converted;
        }

        // Fallback: standard conversion (for any other cases)
        return Expression.Convert(value, targetType);
    }

    // Map field type to the appropriate IDataReader.GetXxx method
    private static MethodInfo GetReaderMethod(Type fieldType)
    {
        var typeName = Type.GetTypeCode(fieldType) switch
        {
            TypeCode.Int32 => nameof(IDataRecord.GetInt32),
            TypeCode.Int64 => nameof(IDataRecord.GetInt64),
            TypeCode.String => nameof(IDataRecord.GetString),
            TypeCode.DateTime => nameof(IDataRecord.GetDateTime),
            TypeCode.Decimal => nameof(IDataRecord.GetDecimal),
            TypeCode.Boolean => nameof(IDataRecord.GetBoolean),
            TypeCode.Int16 => nameof(IDataRecord.GetInt16),
            TypeCode.Byte => nameof(IDataRecord.GetByte),
            TypeCode.Double => nameof(IDataRecord.GetDouble),
            TypeCode.Single => nameof(IDataRecord.GetFloat),
            _ when fieldType == typeof(Guid) => nameof(IDataRecord.GetGuid),
            _ => nameof(IDataRecord.GetValue)
        };

        return typeof(IDataRecord).GetMethod(typeName, new[] { typeof(int) })!;
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

    // Pre-compiled coercers — null if no coercion needed.
    // The coercion path is resolved once at plan-build time via ResolveCoercer;
    // the returned delegate skips registry lookups on every subsequent row.
    private Func<object?, object?>? BuildCoercer(IColumnInfo column, Type fieldType, Type targetType)
    {
        if (targetType.IsAssignableFrom(fieldType) && !column.IsJsonType && column.EnumType == null)
        {
            return null;
        }

                var resolved = TypeCoercionHelper.ResolveCoercer(
                    column, _coercionOptions.Provider, EnumParseBehavior, _coercionOptions, fieldType);

        return value =>
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                return resolved(value);
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
