using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

/// <summary>
/// The reads a provider needs special handling for, each one typed expression from the column to the property's type:
/// Npgsql's inet and interval, an offset timestamp, a Guid the provider can't return, a NUMBER beyond decimal, an Int64
/// that may be wider, and a provider's own value type into a portable one. Shared by the gateway's compiled mapper and
/// the plan mapper, which used to carry one copy each. Null when the column is none of these and the caller's ordinary
/// read applies.
/// </summary>
internal static class ColumnQuirkReads
{
    /// <summary>
    /// Whether the column is an Npgsql inet or interval read as Npgsql's own type, which the schema says once, when a
    /// plan is built, and not on every row.
    /// </summary>
    public static bool NpgsqlTypedFor(IDataRecord schema, int ordinal, Type targetType) =>
        (Nullable.GetUnderlyingType(targetType) ?? targetType) switch
        {
            var t when t == typeof(Inet) => NpgsqlTypedFieldReader.Inet.AppliesTo(schema, ordinal),
            var t when t == typeof(PostgreSqlInterval) => NpgsqlTypedFieldReader.Interval.AppliesTo(schema, ordinal),
            _ => false
        };

    /// <param name="record">The reader as an <see cref="IDataRecord"/>.</param>
    /// <param name="npgsqlTyped"><see cref="NpgsqlTypedFor"/> for this column.</param>
    public static Expression? TryBuild(Expression record, int ordinal, Type fieldType, Type targetType,
        TypeCoercionOptions? options, bool npgsqlTyped)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var ordinalExpr = Expression.Constant(ordinal);

        if (underlying == typeof(Inet))
        {
            return Npgsql(NpgsqlTypedFieldReader.Inet, record, ordinalExpr, ordinal, targetType, options, npgsqlTyped);
        }

        if (fieldType == typeof(DateTime) && underlying == typeof(DateTimeOffset) &&
            options?.ReadsOffsetTimestampsSpecially == true)
        {
            // Snowflake.Data reports TIMESTAMP_LTZ/TZ as DateTime but GetValue returns the exact DateTimeOffset, while
            // GetDateTime gives local wall time or throws (TYPE-002); ODP.NET 21 returns only the wall time of a
            // TIMESTAMP WITH TIME ZONE, read with its GetDateTimeOffset. A value that is a DateTime converts as before.
            var raw = Expression.Call(OffsetTimestampFieldReader.ReadMethod, record, ordinalExpr,
                Expression.Constant(options.OffsetTimestampDataTypeName, typeof(string)));
            return ColumnConversion.Build(raw, typeof(object), targetType, options);
        }

        if (fieldType == typeof(object) && underlying == typeof(Guid))
        {
            // A column the provider can't return (Npgsql on Spanner's uuid) is read from its bytes. One reader per plan
            // column learns the refusal once (PERF-010).
            var guidReader = new ProviderValueFieldReader.GuidColumnReader(options?.GuidBytesBigEndian ?? false);
            var raw = Expression.Call(Expression.Constant(guidReader), ProviderValueFieldReader.GuidColumnReader.ReadMethod,
                record, ordinalExpr);
            return ColumnConversion.Build(raw, typeof(object), targetType, options);
        }

        if (fieldType == typeof(decimal) && NumericFieldReader.IsFloatingPoint(targetType))
        {
            // A NUMBER beyond decimal (ODP.NET) reads with GetDouble (TYPE-002).
            var raw = Expression.Call(typeof(NumericFieldReader).GetMethod(nameof(NumericFieldReader.ReadDouble))!,
                record, ordinalExpr);
            return ColumnConversion.Build(raw, typeof(double), targetType, options);
        }

        if (fieldType == typeof(string) && underlying == typeof(byte[]))
        {
            // FirebirdClient reports BINARY/VARBINARY as string but returns byte[] (TYPE-002): read the value, which
            // converts as before when it really is text.
            var raw = Expression.Call(record, ReaderGetters.GetValue, ordinalExpr);
            return ColumnConversion.Build(raw, typeof(object), targetType, options);
        }

        if (fieldType == typeof(long) && (underlying == typeof(decimal) || underlying == typeof(double)))
        {
            // A column reported as Int64 may hold more (Snowflake.Data, below); typed for the common decimal/double
            // properties so an ordinary BIGINT doesn't box.
            var raw = Expression.Call(typeof(WideIntegerFieldReader).GetMethod(underlying == typeof(decimal)
                ? nameof(WideIntegerFieldReader.ReadDecimal)
                : nameof(WideIntegerFieldReader.ReadDouble))!, record, ordinalExpr);
            return ColumnConversion.Build(raw, underlying, targetType, options);
        }

        if (fieldType == typeof(long) && WideIntegerFieldReader.IsWiderThanInt64(targetType))
        {
            // Snowflake.Data reports every scale-0 NUMBER as Int64 and overflows beyond it; read it as BigInteger when
            // it does.
            var raw = Expression.Call(typeof(WideIntegerFieldReader).GetMethod(nameof(WideIntegerFieldReader.Read))!,
                record, ordinalExpr);
            return ColumnConversion.Build(raw, typeof(object), targetType, options);
        }

        if (underlying == typeof(PostgreSqlInterval))
        {
            // Npgsql's GetValue() returns TimeSpan for interval columns, which cannot hold months and renormalizes
            // days/time; read the full-fidelity NpgsqlInterval instead.
            return Npgsql(NpgsqlTypedFieldReader.Interval, record, ordinalExpr, ordinal, targetType, options, npgsqlTyped);
        }

        // The provider's own value type into the portable value, one typed expression (no boxing, no coercer).
        if (ProviderTypedConversions.TryBuild(fieldType, targetType, record, ordinal) is { } providerTyped)
        {
            return providerTyped.Type == targetType ? providerTyped : Expression.Convert(providerTyped, targetType);
        }

        return null;
    }

    // Npgsql's inet (which GetValue returns as an IPAddress, dropping the netmask) and interval (as a TimeSpan, which
    // cannot hold months): the typed struct when the column is that Npgsql type and the shape is known, otherwise the
    // plain read and the general conversion.
    private static Expression Npgsql(NpgsqlTypedFieldReader typed, Expression record, Expression ordinalExpr, int ordinal,
        Type targetType, TypeCoercionOptions? options, bool npgsqlTyped)
    {
        if (npgsqlTyped && typed.BuildTypedValue(record, ordinal) is { } value)
        {
            return value.Type == targetType ? value : Expression.Convert(value, targetType);
        }

        Expression raw = npgsqlTyped
            ? Expression.Call(Expression.Constant(typed), NpgsqlTypedFieldReader.ReadTypedMethod, record, ordinalExpr)
            : Expression.Call(record, ReaderGetters.GetValue, ordinalExpr);
        return ColumnConversion.Build(raw, typeof(object), targetType, options);
    }
}
