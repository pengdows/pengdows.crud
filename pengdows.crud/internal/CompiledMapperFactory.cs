using pengdows.crud.dialects;
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

    private static readonly MethodInfo NullabilityRulesRejectNullOnFailureMethod = typeof(NullabilityRules).GetMethod(
        nameof(NullabilityRules.RejectNullOnFailure), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo NullIntoNonNullableMethod = typeof(NullabilityRules).GetMethod(
        nameof(NullabilityRules.NullIntoNonNullableException), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    // The one NULL decision for a gateway read: a present value is assigned; a NULL into a non-nullable property throws the
    // mapper's DataMappingException (naming the column); a NULL into a nullable property leaves it alone.
    private static Expression NullGuard(Expression isPresent, Expression assignment, ParameterExpression readerParam,
        Expression ordinal, PropertyInfo property, Type targetType)
    {
        if (!NullabilityRules.IsNonNullable(property))
        {
            return Expression.IfThen(isPresent, assignment);
        }

        return Expression.IfThenElse(isPresent, assignment, Expression.Throw(Expression.Call(NullIntoNonNullableMethod,
            readerParam, ordinal, Expression.Constant(property.Name), Expression.Constant(targetType))));
    }

    public static Func<TReader, TEntity> Create<TReader>(
        TReader reader,
        IReadOnlyDictionary<string, IColumnInfo> columnsByName,
        EnumParseFailureMode enumMode,
        string[] fieldNames,
        Type[] fieldTypes,
        Func<string, string>? namePolicy = null,
        bool strict = false,
        TypeCoercionOptions? coercionOptions = null) where TReader : IDataRecord
    {
        var readerParam = Expression.Parameter(typeof(TReader), "reader");
        var entityVar = Expression.Variable(typeof(TEntity), "entity");
        // The ordinal being read, so a conversion failure names its column without re-reading the
        // row (DEC-013: a sequential-access reader can't go back). A local store per column.
        var ordinalVar = Expression.Variable(typeof(int), "ordinal");

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

            // Build the value-read expression; only the lenient-enum branch wraps it in its own try-catch
            // (conversion failures are caught once, below, and tagged with the column).
            Expression valueReadExpr;
            // Set to true in the else branch for non-nullable value types to skip the
            // IsDBNull guard — see comment near the end of the else block below.
            var skipNullGuard = false;

            if (column.IsJsonType)
            {
                var getString = typeof(IDataRecord).GetMethod(nameof(IDataRecord.GetString))!;
                // Blank text is the JSON literal null (COR-007).
                var jsonStr = Expression.Call(
                    typeof(pengdows.crud.types.coercion.JsonCoercion).GetMethod(nameof(pengdows.crud.types.coercion.JsonCoercion.JsonTextOrNullLiteral),
                        BindingFlags.NonPublic | BindingFlags.Static)!,
                    Expression.Call(readerParam, getString, ordinalExpr));

                var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (underlying == typeof(types.valueobjects.JsonValue))
                {
                    // A JsonValue holds the JSON text itself. Deserializing into the struct (it has no
                    // settable properties) returned an empty default (TYPE-019).
                    var ctor = typeof(types.valueobjects.JsonValue).GetConstructor(new[] { typeof(string) })!;
                    valueReadExpr = Expression.Convert(Expression.New(ctor, jsonStr), targetType);
                }
                else
                {
                    // Use JsonSerializer.Deserialize<T>(string, JsonSerializerOptions)
                    var deserializeMethod = ResolveJsonDeserializeMethod(targetType);
                    valueReadExpr = Expression.Call(deserializeMethod, jsonStr, Expression.Constant(column.JsonSerializerOptions));
                }

                // Invalid JSON is not swallowed (DEC-008): the JsonException reaches the gateway,
                // which reports a DataMappingException naming the column (TYPE-008).

                if (!targetType.IsValueType)
                {
                    // A reference-type target (JsonDocument, a [Json] POCO) keeps its null guard: read the text once, its
                    // DBNull is the null check, and the text is the one GetValue result (a string or anything convertible).
                    var jsonStored = Expression.Variable(typeof(object), "value");
                    var jsonText = Expression.Call(
                        typeof(pengdows.crud.types.coercion.JsonCoercion).GetMethod(nameof(pengdows.crud.types.coercion.JsonCoercion.JsonTextOrNullLiteral),
                            BindingFlags.NonPublic | BindingFlags.Static)!,
                        Expression.Call(SingleReadColumns.JsonTextFromValueMethod, jsonStored,
                            Expression.Constant(coercionOptions, typeof(TypeCoercionOptions))));
                    var jsonValue = (Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(types.valueobjects.JsonValue)
                        ? (Expression)Expression.Convert(Expression.New(
                            typeof(types.valueobjects.JsonValue).GetConstructor(new[] { typeof(string) })!, jsonText), targetType)
                        : Expression.Call(ResolveJsonDeserializeMethod(targetType), jsonText,
                            Expression.Constant(column.JsonSerializerOptions));
                    expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
                    expressions.Add(Expression.Block(
                        new[] { jsonStored },
                        Expression.Assign(jsonStored, Expression.Call(readerParam, GetValueMethod, ordinalExpr)),
                        Expression.IfThen(
                            Expression.Not(Expression.TypeIs(jsonStored, typeof(DBNull))),
                            Expression.Assign(Expression.Property(entityVar, property), jsonValue))));
                    continue;
                }
            }
            else if (ColumnReadRules.TryBuild(readerParam, i, fieldType, targetType, coercionOptions,
                         ColumnQuirkReads.NpgsqlTypedFor(reader, i, targetType), column.NativeType,
                         Expression.Property(entityVar, property), out var selfGuarded) is { } primary)
            {
                // The shared primary rules (ColumnReadRules). A read that checks NULL itself is used as it is. A read that does
                // not is guarded here: a NULL into a non-nullable property throws (the same error the mapper raises, naming the
                // column), and a nullable property leaves itself alone.
                expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
                // A non-nullable value type has no guard: its typed read throws on NULL (one reader call per column, which
                // the shape cost tests hold the gateway to), and the gateway tags that failure with the column.
                var nonNullableValue = targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null;
                expressions.Add(selfGuarded || nonNullableValue
                    ? primary
                    : NullGuard(notDbNull, primary, readerParam, ordinalExpr, property, targetType));
                continue;
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
                    // Typed like any other column: an unsigned column read through GetValue was unboxed as the
                    // enum's underlying type and failed (REV-072, as COR-008 for other properties).
                    var rawValue = ReadColumn(readerParam, fieldType, ordinalExpr);
                    var convertedValue = ColumnConversion.Build(rawValue, fieldType, underlyingTarget, coercionOptions);

                    var mapperMethod = typeof(EnumMappingCache).GetMethod(nameof(EnumMappingCache.ValidateEnumValue))!.MakeGenericMethod(underlyingTarget);
                    enumReadExpr = Expression.Call(mapperMethod, convertedValue);
                }

                Expression finalEnumExpr = targetType != underlyingTarget
                    ? Expression.Convert(enumReadExpr, targetType)
                    : enumReadExpr;

                // A non-nullable enum is a value type like any other: no IsDBNull guard, so a NULL fails loudly
                // (the typed getter throws) instead of silently leaving the property at its default. A nullable
                // enum keeps the guard and takes NULL as null.
                skipNullGuard = targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null;

                if (enumMode == EnumParseFailureMode.Throw)
                {
                    valueReadExpr = finalEnumExpr;
                }
                else
                {
                    // The property's default (null for a nullable enum); SetNullAndLog also logs, as
                    // TypeCoercionHelper does on the other read paths (DRY-007).
                    Expression fallback = Expression.Default(targetType);
                    if (enumMode == EnumParseFailureMode.SetNullAndLog)
                    {
                        fallback = Expression.Block(
                            Expression.Call(typeof(pengdows.crud.types.coercion.EnumCoercion).GetMethod(nameof(pengdows.crud.types.coercion.EnumCoercion.LogEnumFailure),
                                BindingFlags.NonPublic | BindingFlags.Static)!, Expression.Constant(underlyingTarget)),
                            fallback);
                    }

                    // A non-nullable enum's typed read throws on NULL too, and that throw lands here. Only this failure path checks
                    // for NULL, so the success path keeps its single read and a NULL is never a default.
                    if (skipNullGuard)
                    {
                        var failureParam = Expression.Parameter(typeof(Exception), "failure");
                        var rejectNull = Expression.Call(NullabilityRulesRejectNullOnFailureMethod, readerParam, ordinalExpr,
                            Expression.Constant(property.Name), Expression.Constant(targetType), failureParam);
                        valueReadExpr = Expression.TryCatch(finalEnumExpr,
                            Expression.Catch(failureParam, Expression.Block(rejectNull, fallback)));
                    }
                    else
                    {
                        valueReadExpr = Expression.TryCatch(finalEnumExpr, Expression.Catch(typeof(Exception), fallback));
                    }
                }
            }
            else
            {
                // OPTIMIZATION: For most common primitive types where source and target match,
                // bypass BuildConversionExpression's potential boxing/Coerce paths.
                var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

                // The value conversion for whatever read produced <raw>.
                Expression ConvertRead(Expression raw)
                {
                    if (underlyingTarget == typeof(Stream) && typeof(Stream).IsAssignableFrom(fieldType))
                    {
                        // DuckDB returns BLOBs as UnmanagedMemoryStream instances backed by the
                        // active reader. Do not let that reader-bound stream escape the mapper;
                        // copy it while it is valid.
                        return Expression.Convert(
                            Expression.Call(
                                typeof(ProviderStreamMaterializer).GetMethod(nameof(ProviderStreamMaterializer.Materialize))!,
                                Expression.Convert(raw, typeof(object))),
                            targetType);
                    }

                    if (fieldType == underlyingTarget && raw.Type == fieldType)
                    {
                        // Fast path: reader returned the native type — no unboxing needed.
                        Expression native = underlyingTarget == typeof(DateTime)
                            ? Expression.Call(ColumnConversion.NormalizeDateTime, raw) // Inlined NormalizeDateTime
                            : raw;
                        return targetType != underlyingTarget ? Expression.Convert(native, targetType) : native;
                    }

                    // Either the type doesn't match the target (needs coercion), or GetReaderMethod fell back to
                    // GetValue() which returns System.Object (e.g. DateTimeOffset — no IDataRecord.GetDateTimeOffset
                    // exists). BuildConversionExpression handles the unboxing + any type conversion.
                    return ColumnConversion.Build(raw, fieldType, targetType, coercionOptions);
                }

                if (!(targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null) && !fieldType.IsValueType)
                {
                    // A reference-type column read into a target that keeps its null guard (BitArray, char[], a spatial
                    // value, text into an array or a TextReader): one GetValue, whose DBNull is the null check.
                    var stored = Expression.Variable(typeof(object), "value");
                    Expression raw = fieldType == typeof(string)
                        ? Expression.Call(SingleReadColumns.StringReadFor(coercionOptions), stored,
                            Expression.Constant(coercionOptions, typeof(TypeCoercionOptions)))
                        : fieldType == typeof(object) ? stored : Expression.Convert(stored, fieldType);
                    expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
                    var present = Expression.Assign(Expression.Property(entityVar, property), ConvertRead(raw));
                    var isPresent = Expression.Not(Expression.TypeIs(stored, typeof(DBNull)));
                    expressions.Add(Expression.Block(
                        new[] { stored },
                        Expression.Assign(stored, Expression.Call(readerParam, GetValueMethod, ordinalExpr)),
                        NullGuard(isPresent, present, readerParam, ordinalExpr, property, targetType)));
                    continue;
                }

                // Typed for any target: a uint column into a long property converts the uint; the boxed
                // GetValue result was unboxed as long and threw (COR-008).
                valueReadExpr = ConvertRead(ReadColumn(readerParam, fieldType, ordinalExpr));

                // Non-nullable value types: skip the IsDBNull guard. The caller's schema
                // should guarantee no NULLs for these columns; if the DB does return NULL,
                // the typed getter will throw — which is the correct, loud failure mode
                // rather than silently leaving the property at default(T).
                skipNullGuard = targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null;
            }

            // A non-nullable value type has no IsDBNull guard whatever read it takes: a NULL fails loudly (the typed
            // read throws) instead of silently leaving the property at its default.
            skipNullGuard |= targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null;

            // No per-column try-catch: the whole body's catch tags a conversion failure with this column's
            // ordinal (ColumnReadException); any other exception passes through unchanged.
            var propertyAccess = Expression.Property(entityVar, property);
            var assignment = Expression.Assign(propertyAccess, valueReadExpr);

            expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
            expressions.Add(skipNullGuard
                ? assignment
                : NullGuard(notDbNull, assignment, readerParam, ordinalExpr, property, targetType));
        }

        // Conversion failures leave tagged with the ordinal; anything else (cancellation, provider
        // errors) passes through unchanged.
        var failure = Expression.Parameter(typeof(Exception), "failure");
        var body = Expression.TryCatch(
            Expression.Block(typeof(void), expressions),
            Expression.Catch(
                failure,
                Expression.Throw(Expression.New(ColumnReadExceptionCtor, ordinalVar, failure)),
                Expression.Call(IsConversionFailureMethod, failure)));

        var block = Expression.Block(new[] { entityVar, ordinalVar }, body, entityVar);
        return Expression.Lambda<Func<TReader, TEntity>>(block, readerParam).Compile();
    }

    private static readonly MethodInfo GetValueMethod = ReaderGetters.GetValue;

    private static readonly ConstructorInfo ColumnReadExceptionCtor =
        typeof(ColumnReadException).GetConstructor(new[] { typeof(int), typeof(Exception) })!;

    private static readonly MethodInfo IsConversionFailureMethod =
        typeof(CompiledMapperFactory<TEntity>).GetMethod(nameof(IsConversionFailure),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    // The failures a stored value that doesn't fit its property produces (TYPE-008).
    private static bool IsConversionFailure(Exception failure) =>
        failure is OverflowException or InvalidCastException or FormatException or JsonException
            or exceptions.InvalidValueException or EnumValueException;

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

    // The column read as its own type: a typed getter, GetFieldValue<T> for the value types IDataRecord has
    // no getter for (TypedFieldReader, COR-008), otherwise GetValue.
    private static Expression ReadColumn(Expression readerParam, Type fieldType, Expression ordinalExpr) =>
        TypedFieldReader.Handles(fieldType)
            ? Expression.Call(
                typeof(TypedFieldReader).GetMethod(nameof(TypedFieldReader.Read))!.MakeGenericMethod(fieldType),
                Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr)
            : Expression.Call(readerParam, GetReaderMethod(fieldType), ordinalExpr);

    private static MethodInfo GetReaderMethod(Type fieldType) =>
        ReaderGetters.TypedGetter(typeof(IDataRecord), fieldType) ?? ReaderGetters.GetValue;

}

/// <summary>
/// Global cache for enum parsing and validation to avoid reflection in the hot path.
/// </summary>
internal static class EnumMappingCache
{
    // One static holder per enum type: a single dictionary lookup per value, where a type-keyed
    // dictionary of dictionaries cost two (PERF-015).
    private static class Cache<TEnum> where TEnum : struct, Enum
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TEnum> FromString =
            new(StringComparer.OrdinalIgnoreCase);

        // Only valid values are cached, so bad stored data can't grow these without bound (COR-005).
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<TEnum, bool> Defined = new();

        public static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), false);

        // Every bit some defined member uses, for [Flags] combinations; computed only for [Flags] enums.
        public static readonly ulong AllFlags =
            IsFlags ? Enum.GetValues<TEnum>().Aggregate(0UL, (bits, v) => bits | ToBits(v)) : 0UL;

        public static ulong ToBits(TEnum value) => Bits(value);
    }

    // A defined value, or for a [Flags] enum any combination of defined flags (COR-005).
    private static bool IsValid<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Enum.IsDefined(value) || (Cache<TEnum>.IsFlags && (Cache<TEnum>.ToBits(value) & ~Cache<TEnum>.AllFlags) == 0);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (bool IsFlags, ulong AllFlags)> FlagsByType = new();

    /// <summary>
    /// The same rule for an enum known only at run time (<c>TypeCoercionHelper.CoerceEnum</c>), so every read
    /// path accepts the same values (DRY-003).
    /// </summary>
    internal static bool IsValid(Type enumType, object value)
    {
        if (Enum.IsDefined(enumType, value))
        {
            return true;
        }

        var (isFlags, allFlags) = FlagsByType.GetOrAdd(enumType, static t => t.IsDefined(typeof(FlagsAttribute), false)
            ? (true, Enum.GetValues(t).Cast<object>().Aggregate(0UL, (bits, v) => bits | Bits(v)))
            : (false, 0UL));
        return isFlags && ((Bits(value) & ~allFlags) == 0);
    }

    // An enum value's bits whatever its underlying type: Convert.ToInt64 overflowed for a ulong member
    // above long.MaxValue, which failed the whole enum (REV-070).
    private static ulong Bits(object value)
    {
        var underlying = Type.GetTypeCode(value.GetType().IsEnum ? Enum.GetUnderlyingType(value.GetType()) : value.GetType());
        if (underlying is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64)
        {
            return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    public static TEnum GetEnumFromString<TEnum>(string value) where TEnum : struct, Enum
    {
        if (Cache<TEnum>.FromString.TryGetValue(value, out var cached))
        {
            return cached;
        }

        // Parsing accepts a number ("999") as an undefined value, so it is validated like the numeric path
        // (COR-005); TryParse, so an unknown name fails as EnumValueException, reported naming the column (REV-071).
        if (!Enum.TryParse<TEnum>(value, true, out var parsed) || !IsValid(parsed))
        {
            throw new EnumValueException($"Invalid enum value '{value}' for type {typeof(TEnum).Name}");
        }

        Cache<TEnum>.FromString.TryAdd(value, parsed);
        return parsed;
    }

    public static TEnum ValidateEnumValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        // A defined member is the usual case: no dictionary lookup.
        if (EnumFastPath.IsDefined(value))
        {
            return value;
        }

        if (Cache<TEnum>.Defined.ContainsKey(value))
        {
            return value;
        }

        if (IsValid(value))
        {
            Cache<TEnum>.Defined.TryAdd(value, true);
            return value;
        }

        throw new EnumValueException($"Invalid enum value '{value}' for type {typeof(TEnum).Name}");
    }
}
