using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

namespace pengdows.crud.@internal;

/// <summary>
/// Factory that compiles monolithic, unrolled row mappers for entities.
/// Eliminates loop-over-delegates and boxing overhead.
/// </summary>
internal static class CompiledMapperFactory<TEntity> where TEntity : class, new()
{
    private static readonly MethodInfo IsDbNullMethod = typeof(IDataRecord).GetMethod(nameof(IDataRecord.IsDBNull))!;

    private static readonly MethodInfo CoerceMethod = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.Coerce),
        BindingFlags.Public | BindingFlags.Static,
        null,
        new[] { typeof(object), typeof(Type), typeof(Type), typeof(TypeCoercionOptions) },
        null)!;

    private static readonly MethodInfo NormalizeDateTimeMethod = typeof(TypeCoercionHelper).GetMethod(
        nameof(TypeCoercionHelper.NormalizeDateTime),
        BindingFlags.NonPublic | BindingFlags.Static,
        null,
        new[] { typeof(DateTime) },
        null)!;

    public static Func<TReader, TEntity> Create<TReader>(
        TReader reader,
        IReadOnlyDictionary<string, IColumnInfo> columnsByName,
        EnumParseFailureMode enumMode,
        string[] fieldNames,
        Type[] fieldTypes,
        Func<string, string>? namePolicy = null,
        bool strict = false) where TReader : IDataRecord
    {
        var readerParam = Expression.Parameter(typeof(TReader), "reader");
        var entityVar = Expression.Variable(typeof(TEntity), "entity");

        var expressions = new List<Expression>
        {
            Expression.Assign(entityVar, Expression.New(typeof(TEntity)))
        };

        var fieldCount = reader.FieldCount;
        for (var i = 0; i < fieldCount; i++)
        {
            var rawName = fieldNames[i];
            var fieldName = namePolicy != null ? namePolicy(rawName) : rawName;
            
            if (!columnsByName.TryGetValue(fieldName, out var column))
            {
                continue;
            }

            var fieldType = fieldTypes[i];
            var property = column.PropertyInfo;
            var targetType = property.PropertyType;

            var ordinalExpr = Expression.Constant(i);
            var notDbNull = Expression.Not(Expression.Call(readerParam, IsDbNullMethod, ordinalExpr));

            // Define the assignment logic first so we can wrap it in a try-catch
            Expression valueReadExpr;

            if (column.IsJsonType)
            {
                var getString = typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetString))!;
                var jsonStr = Expression.Call(readerParam, getString, ordinalExpr);
                
                // Use JsonSerializer.Deserialize<T>(string, JsonSerializerOptions)
                var deserializeMethod = ResolveJsonDeserializeMethod(targetType);
                valueReadExpr = Expression.Call(deserializeMethod, jsonStr, Expression.Constant(column.JsonSerializerOptions));
                
                // For JSON, we return default (null for objects) on error to match old behavior
                var catchBlock = Expression.Catch(typeof(JsonException), Expression.Default(targetType));
                valueReadExpr = Expression.TryCatch(valueReadExpr, catchBlock);
            }
            else if (fieldType == typeof(byte[]))
            {
                if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                {
                    var readGuidMethod = typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.ReadGuidFromBytes))!;
                    valueReadExpr = Expression.Call(readGuidMethod, readerParam, ordinalExpr);
                    if (targetType == typeof(Guid?))
                    {
                        valueReadExpr = Expression.Convert(valueReadExpr, typeof(Guid?));
                    }
                }
                else
                {
                    var readBytesMethod = typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.ReadBytes))!;
                    var call = Expression.Call(readBytesMethod, readerParam, ordinalExpr);
                    valueReadExpr = BuildConversionExpression(call, fieldType, targetType);
                }

                var exParam = Expression.Parameter(typeof(Exception), "ex");
                var catchBlock = Expression.Catch(exParam, 
                    Expression.Throw(
                        Expression.New(typeof(exceptions.InvalidValueException).GetConstructor(new[] { typeof(string) })!,
                        Expression.Constant($"Unable to set property from value that was stored in the database: {rawName}")),
                        targetType));
                
                valueReadExpr = Expression.TryCatch(valueReadExpr, catchBlock);
            }
            else if (column.IsEnum)
            {
                var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                Expression enumReadExpr;
                if (column.EnumAsString)
                {
                    var getString = typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetString))!;
                    var enumStr = Expression.Call(readerParam, getString, ordinalExpr);
                    
                    var mapperMethod = typeof(EnumMappingCache).GetMethod(nameof(EnumMappingCache.GetEnumFromString))!.MakeGenericMethod(underlyingTarget);
                    enumReadExpr = Expression.Call(mapperMethod, enumStr);
                }
                else
                {
                    var getMethod = GetReaderMethod(fieldType);
                    var rawValue = Expression.Call(readerParam, getMethod, ordinalExpr);
                    var convertedValue = BuildConversionExpression(rawValue, fieldType, underlyingTarget);
                    
                    var mapperMethod = typeof(EnumMappingCache).GetMethod(nameof(EnumMappingCache.ValidateEnumValue))!.MakeGenericMethod(underlyingTarget);
                    enumReadExpr = Expression.Call(mapperMethod, convertedValue);
                }

                Expression finalEnumExpr = targetType != underlyingTarget 
                    ? Expression.Convert(enumReadExpr, targetType) 
                    : enumReadExpr;

                if (enumMode == EnumParseFailureMode.Throw)
                {
                    valueReadExpr = finalEnumExpr;
                }
                else
                {
                    var defaultValue = Expression.Default(targetType);
                    var catchBlock = Expression.Catch(typeof(Exception), defaultValue);
                    valueReadExpr = Expression.TryCatch(finalEnumExpr, catchBlock);
                }
            }
            else
            {
                var getMethod = GetReaderMethod(fieldType);
                var rawValue = Expression.Call(readerParam, getMethod, ordinalExpr);
                
                // OPTIMIZATION: For most common primitive types where source and target match,
                // bypass BuildConversionExpression's potential boxing/Coerce paths.
                var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (fieldType == underlyingTarget)
                {
                    if (underlyingTarget == typeof(DateTime))
                    {
                        // Inlined NormalizeDateTime
                        valueReadExpr = Expression.Call(NormalizeDateTimeMethod, rawValue);
                    }
                    else
                    {
                        valueReadExpr = rawValue;
                    }

                    if (targetType != underlyingTarget)
                    {
                        valueReadExpr = Expression.Convert(valueReadExpr, targetType);
                    }
                }
                else
                {
                    valueReadExpr = BuildConversionExpression(rawValue, fieldType, targetType);
                }
            }

            // Directly assign without per-column try-catch for maximum performance.
            // Exceptions will bubble up naturally.
            var propertyAccess = Expression.Property(entityVar, property);
            var assignment = Expression.Assign(propertyAccess, valueReadExpr);
            
            expressions.Add(Expression.IfThen(notDbNull, assignment));
        }

        expressions.Add(entityVar);

        var block = Expression.Block(new[] { entityVar }, expressions);
        return Expression.Lambda<Func<TReader, TEntity>>(block, readerParam).Compile();
    }

    private static MethodInfo ResolveJsonDeserializeMethod(Type targetType)
    {
        var methods = typeof(JsonSerializer).GetMethods();
        foreach (var m in methods)
        {
            if (m.Name == nameof(JsonSerializer.Deserialize) &&
                m.IsGenericMethod &&
                m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[1].ParameterType == typeof(JsonSerializerOptions))
            {
                return m.MakeGenericMethod(targetType);
            }
        }
        throw new InvalidOperationException("Could not find JsonSerializer.Deserialize<T>(string, options) overload.");
    }

    private static MethodInfo GetReaderMethod(Type fieldType)
    {
        var typeCode = Type.GetTypeCode(fieldType);
        var methodName = typeCode switch
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

        var method = typeof(IDataRecord).GetMethod(methodName, new[] { typeof(int) });
        if (method == null)
        {
             // Fallback to GetValue
             return typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetValue), new[] { typeof(int) })!;
        }
        return method;
    }

    private static Expression BuildConversionExpression(Expression value, Type sourceType, Type targetType)
    {
        var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (sourceType == underlyingTargetType)
        {
            var result = value.Type != sourceType ? Expression.Convert(value, sourceType) : value;

            // Normalize DateTime: treat Unspecified as UTC, convert Local to UTC.
            // Many drivers (Snowflake TIMESTAMP_NTZ, SQL Server datetime, MySQL, Oracle)
            // return DateTimeKind.Unspecified for timezone-naive columns that store UTC values.
            // Without this, calling .ToUniversalTime() on the entity property would incorrectly
            // apply the host's local timezone offset (e.g. UTC-6 → +6 hour drift).
            if (underlyingTargetType == typeof(DateTime))
            {
                result = Expression.Call(NormalizeDateTimeMethod, result);
            }

            return targetType != underlyingTargetType
                ? Expression.Convert(result, targetType)
                : result;
        }

        // Simple numeric/bool conversions
        if (IsNumericType(sourceType) && IsNumericType(underlyingTargetType))
        {
             var converted = Expression.Convert(value, underlyingTargetType);
             return targetType != underlyingTargetType ? Expression.Convert(converted, targetType) : converted;
        }

        if (sourceType == typeof(long) && underlyingTargetType == typeof(bool))
        {
            var notEqualZero = Expression.NotEqual(value, Expression.Constant(0L));
            return targetType != underlyingTargetType ? Expression.Convert(notEqualZero, targetType) : notEqualZero;
        }

        // Fallback: TypeCoercionHelper.Coerce(object, fieldType, targetType)
        var boxedValue = Expression.Convert(value, typeof(object));
        var coerceCall = Expression.Call(
            CoerceMethod,
            boxedValue,
            Expression.Constant(sourceType),
            Expression.Constant(targetType),
            Expression.Constant(null, typeof(TypeCoercionOptions)));

        return Expression.Convert(coerceCall, targetType);
    }

    private static bool IsNumericType(Type type)
    {
        var typeCode = Type.GetTypeCode(type);
        return typeCode switch
        {
            TypeCode.Byte => true,
            TypeCode.SByte => true,
            TypeCode.Int16 => true,
            TypeCode.UInt16 => true,
            TypeCode.Int32 => true,
            TypeCode.UInt32 => true,
            TypeCode.Int64 => true,
            TypeCode.UInt64 => true,
            TypeCode.Single => true,
            TypeCode.Double => true,
            TypeCode.Decimal => true,
            _ => false
        };
    }
}

/// <summary>
/// Global cache for enum parsing and validation to avoid reflection in the hot path.
/// </summary>
internal static class EnumMappingCache
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _stringMappers = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _numericValidators = new();

    public static TEnum GetEnumFromString<TEnum>(string value) where TEnum : struct, Enum
    {
        var mapper = (System.Collections.Concurrent.ConcurrentDictionary<string, TEnum>)_stringMappers.GetOrAdd(
            typeof(TEnum), _ => new System.Collections.Concurrent.ConcurrentDictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase));
        
        return mapper.GetOrAdd(value, v => (TEnum)Enum.Parse(typeof(TEnum), v, true));
    }

    public static TEnum ValidateEnumValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var validator = (System.Collections.Concurrent.ConcurrentDictionary<TEnum, bool>)_numericValidators.GetOrAdd(
            typeof(TEnum), _ => new System.Collections.Concurrent.ConcurrentDictionary<TEnum, bool>());

        if (validator.GetOrAdd(value, v => Enum.IsDefined(typeof(TEnum), v)))
        {
            return value;
        }

        throw new ArgumentException($"Invalid enum value '{value}' for type {typeof(TEnum).Name}");
    }
}
