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
                    typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.JsonTextOrNullLiteral),
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
            }
            else if (fieldType == typeof(byte[]))
            {
                if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                {
                    // In the dialect's Guid byte order (SqlDialect.StoresGuidBytesBigEndian, TYPE-002).
                    var readGuidMethod = typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.ReadGuidFromBytes),
                        new[] { typeof(IDataRecord), typeof(int), typeof(bool) })!;
                    valueReadExpr = Expression.Call(readGuidMethod, readerParam, ordinalExpr,
                        Expression.Constant(coercionOptions?.GuidBytesBigEndian ?? false));
                    if (targetType == typeof(Guid?))
                    {
                        valueReadExpr = Expression.Convert(valueReadExpr, typeof(Guid?));
                    }
                }
                else
                {
                    var readBytesMethod = typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.ReadBytes))!;
                    var call = Expression.Call(readBytesMethod, readerParam, ordinalExpr);
                    valueReadExpr = BuildConversionExpression(call, fieldType, targetType, coercionOptions);
                }

                // No catch-all here (COR-004): a value that isn't a Guid (InvalidValueException) is a
                // conversion failure the gateway reports naming the column; anything else surfaces as itself.
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
                    var convertedValue = BuildConversionExpression(rawValue, fieldType, underlyingTarget, coercionOptions);

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
                    // The property's default (null for a nullable enum); SetNullAndLog also logs, as
                    // TypeCoercionHelper does on the other read paths (DRY-007).
                    Expression fallback = Expression.Default(targetType);
                    if (enumMode == EnumParseFailureMode.SetNullAndLog)
                    {
                        fallback = Expression.Block(
                            Expression.Call(typeof(TypeCoercionHelper).GetMethod(nameof(TypeCoercionHelper.LogEnumFailure),
                                BindingFlags.NonPublic | BindingFlags.Static)!, Expression.Constant(underlyingTarget)),
                            fallback);
                    }

                    valueReadExpr = Expression.TryCatch(finalEnumExpr, Expression.Catch(typeof(Exception), fallback));
                }
            }
            else if ((Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(types.valueobjects.Inet))
            {
                // Npgsql's GetValue() returns IPAddress for inet columns, dropping the netmask; read
                // NpgsqlInet instead.
                var readInet = typeof(NpgsqlTypedFieldReader).GetMethod(nameof(NpgsqlTypedFieldReader.Read))!;
                var rawValue = Expression.Call(Expression.Constant(NpgsqlTypedFieldReader.Inet), readInet, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else if (fieldType == typeof(DateTime) && (Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(DateTimeOffset)
                     && coercionOptions?.ReadsOffsetTimestampsFromValue == true)
            {
                // Snowflake.Data reports TIMESTAMP_LTZ/TZ as DateTime but GetValue returns the exact
                // DateTimeOffset, while GetDateTime gives local wall time or throws (TYPE-002): read the
                // value, which converts as before when it is a DateTime.
                var getValue = GetValueMethod;
                var rawValue = Expression.Call(Expression.Convert(readerParam, typeof(IDataRecord)), getValue, ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else if (fieldType == typeof(object) && (Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(Guid))
            {
                // A column the provider can't return (Npgsql on Spanner's uuid) is read from its bytes.
                // One reader per plan column learns the refusal once (PERF-010).
                var guidReader = new ProviderValueFieldReader.GuidColumnReader(coercionOptions?.GuidBytesBigEndian ?? false);
                var rawValue = Expression.Call(Expression.Constant(guidReader), ProviderValueFieldReader.GuidColumnReader.ReadMethod,
                    Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else if (fieldType == typeof(string) && ProviderValueFieldReader.IsReadableArray(targetType) &&
                     (coercionOptions?.ReadsCollectionLiterals ?? false))
            {
                // Informix returns LIST/SET/MULTISET as literal text; parse it, typed.
                var readLiteral = ProviderValueFieldReader.ReadCollectionLiteralDefinition.MakeGenericMethod(targetType.GetElementType()!);
                valueReadExpr = Expression.Call(readLiteral, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
            }
            else if (ProviderValueFieldReader.IsReadableArray(targetType) && ProviderValueFieldReader.IsArrayColumn(fieldType))
            {
                // Typed: Npgsql refuses some arrays as non-nullable elements (Spanner reports System.Array)
                // and InterBase returns its declared bounds (Int32[*]).
                valueReadExpr = ProviderValueFieldReader.BindArrayRead(targetType.GetElementType()!,
                    Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
            }
            else if (fieldType == typeof(decimal) && NumericFieldReader.IsFloatingPoint(targetType))
            {
                // A NUMBER beyond decimal (ODP.NET) reads with GetDouble (TYPE-002).
                var readDouble = typeof(NumericFieldReader).GetMethod(nameof(NumericFieldReader.ReadDouble))!;
                var rawValue = Expression.Call(readDouble, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(double), targetType, coercionOptions);
            }
            else if (fieldType == typeof(string) && (Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(byte[]))
            {
                // FirebirdClient reports BINARY/VARBINARY as string but returns byte[] (TYPE-002): read
                // the value, which converts as before when it really is text.
                var getValue = GetValueMethod;
                var rawValue = Expression.Call(Expression.Convert(readerParam, typeof(IDataRecord)), getValue, ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else if (fieldType == typeof(long) &&
                     (Nullable.GetUnderlyingType(targetType) ?? targetType) is var wideTarget &&
                     (wideTarget == typeof(decimal) || wideTarget == typeof(double)))
            {
                // A column reported as Int64 may hold more (Snowflake.Data, see below); typed for the
                // common decimal/double properties so an ordinary BIGINT doesn't box.
                var readTyped = typeof(WideIntegerFieldReader).GetMethod(wideTarget == typeof(decimal)
                    ? nameof(WideIntegerFieldReader.ReadDecimal)
                    : nameof(WideIntegerFieldReader.ReadDouble))!;
                var rawValue = Expression.Call(readTyped, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, wideTarget, targetType, coercionOptions);
            }
            else if (fieldType == typeof(long) && WideIntegerFieldReader.IsWiderThanInt64(targetType))
            {
                // A column reported as Int64 may hold more (Snowflake.Data reports every scale-0
                // NUMBER as Int64 and overflows beyond it); read it as BigInteger when it does.
                var readWide = typeof(WideIntegerFieldReader).GetMethod(nameof(WideIntegerFieldReader.Read))!;
                var rawValue = Expression.Call(readWide, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else if (fieldType == typeof(string) && targetType == typeof(string))
            {
                // One GetValue per string column; its DBNull result is the null check. IsDBNull and
                // then GetString cost a second round of driver work per column on some providers
                // (Npgsql: ~0.12 µs each, measured 2026-10-04); never slower on any provider measured.
                var valueObj = Expression.Variable(typeof(object), "value");
                var stringAssignment = Expression.Block(
                    new[] { valueObj },
                    Expression.Assign(valueObj, Expression.Call(readerParam, GetValueMethod, ordinalExpr)),
                    Expression.IfThen(
                        Expression.Not(Expression.TypeIs(valueObj, typeof(DBNull))),
                        Expression.Assign(Expression.Property(entityVar, property),
                            Expression.Call(StringFromValueMethod, valueObj,
                                Expression.Constant(coercionOptions, typeof(TypeCoercionOptions))))));
                expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
                expressions.Add(stringAssignment);
                continue;
            }
            else if ((Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(types.valueobjects.PostgreSqlInterval))
            {
                // Npgsql's GetValue() returns TimeSpan for interval columns, which cannot hold months
                // and renormalizes days/time; read the full-fidelity NpgsqlInterval instead.
                var readInterval = typeof(NpgsqlTypedFieldReader).GetMethod(nameof(NpgsqlTypedFieldReader.Read))!;
                var rawValue = Expression.Call(Expression.Constant(NpgsqlTypedFieldReader.Interval), readInterval, Expression.Convert(readerParam, typeof(IDataRecord)), ordinalExpr);
                valueReadExpr = BuildConversionExpression(rawValue, typeof(object), targetType, coercionOptions);
            }
            else
            {
                // OPTIMIZATION: For most common primitive types where source and target match,
                // bypass BuildConversionExpression's potential boxing/Coerce paths.
                var underlyingTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                // Typed for any target: a uint column into a long property converts the uint; the boxed
                // GetValue result was unboxed as long and threw (COR-008).
                var rawValue = ReadColumn(readerParam, fieldType, ordinalExpr);
                if (underlyingTarget == typeof(Stream) && typeof(Stream).IsAssignableFrom(fieldType))
                {
                    // DuckDB returns BLOBs as UnmanagedMemoryStream instances backed by the
                    // active reader. Do not let that reader-bound stream escape the mapper;
                    // copy it while it is valid.
                    valueReadExpr = Expression.Convert(
                        Expression.Call(
                            typeof(ProviderStreamMaterializer).GetMethod(nameof(ProviderStreamMaterializer.Materialize))!,
                            Expression.Convert(rawValue, typeof(object))),
                        targetType);
                }
                else if (fieldType == underlyingTarget && rawValue.Type == fieldType)
                {
                    // Fast path: reader returned the native type — no unboxing needed.
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
                    // Either the type doesn't match the target (needs coercion), or
                    // GetReaderMethod fell back to GetValue() which returns System.Object
                    // (e.g. DateTimeOffset — no IDataRecord.GetDateTimeOffset exists).
                    // BuildConversionExpression handles the unboxing + any type conversion.
                    valueReadExpr = BuildConversionExpression(rawValue, fieldType, targetType, coercionOptions);
                }

                // Non-nullable value types: skip the IsDBNull guard. The caller's schema
                // should guarantee no NULLs for these columns; if the DB does return NULL,
                // the typed getter will throw — which is the correct, loud failure mode
                // rather than silently leaving the property at default(T).
                skipNullGuard = targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null;
            }

            // No per-column try-catch: the whole body's catch tags a conversion failure with this column's
            // ordinal (ColumnReadException); any other exception passes through unchanged.
            var propertyAccess = Expression.Property(entityVar, property);
            var assignment = Expression.Assign(propertyAccess, valueReadExpr);

            expressions.Add(Expression.Assign(ordinalVar, ordinalExpr));
            expressions.Add(skipNullGuard
                ? assignment
                : Expression.IfThen(notDbNull, assignment));
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

    private static readonly MethodInfo StringFromValueMethod =
        typeof(CompiledMapperFactory<TEntity>).GetMethod(nameof(StringFromValue), BindingFlags.NonPublic | BindingFlags.Static)!;

    // A string column's value: the string itself, or anything else a provider returns converted the
    // usual way (never dropped).
    private static string StringFromValue(object value, TypeCoercionOptions? options) =>
        value as string ?? (string)TypeCoercionHelper.Coerce(value, value.GetType(), typeof(string), options)!;

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

    private static Expression BuildConversionExpression(Expression value, Type sourceType, Type targetType,
        TypeCoercionOptions? coercionOptions)
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

        // Numeric columns into numeric, enum and bool properties, as DataReaderMapper reads them.
        if (NumericExpressions.Handles(sourceType, underlyingTargetType))
        {
            var converted = NumericExpressions.Convert(value, sourceType, underlyingTargetType);
            return targetType != underlyingTargetType ? Expression.Convert(converted, targetType) : converted;
        }

        // Date and time conversions TypeCoercionHelper.Coerce would make, without boxing the value or
        // its result (PERF-021): DateTime into DateOnly, TimeSpan into TimeOnly, timestamp text (SQLite)
        // into DateTime/DateTimeOffset/DateOnly.
        if (TypedCoercions.Find(sourceType, underlyingTargetType) is { } typedCoercion)
        {
            var typedSource = value.Type != sourceType ? Expression.Convert(value, sourceType) : value;
            var converted = Expression.Call(typedCoercion, typedSource,
                Expression.Constant(coercionOptions ?? TypeCoercionOptions.Default, typeof(TypeCoercionOptions)));
            return targetType != underlyingTargetType ? Expression.Convert(converted, targetType) : converted;
        }

        // Fallback: TypeCoercionHelper.Coerce(object, fieldType, targetType)
        var boxedValue = Expression.Convert(value, typeof(object));
        var coerceCall = Expression.Call(
            CoerceMethod,
            boxedValue,
            Expression.Constant(sourceType),
            Expression.Constant(targetType),
            Expression.Constant(coercionOptions, typeof(TypeCoercionOptions)));

        return Expression.Convert(coerceCall, targetType);
    }

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
