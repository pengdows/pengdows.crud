// =============================================================================
// FILE: ProviderTypedConversions.cs
// PURPOSE: A provider's own value type read into the library's portable value as one typed expression.
//
// AI SUMMARY:
// - Npgsql returns NpgsqlRange<T>, NpgsqlCidr, PhysicalAddress and the hstore Dictionary as themselves. Reading one
//   through a boxed GetValue, the coercion registry and several delegates cost about 120 ns a value.
// - TryBuild(fieldType, targetType, record, ordinal) returns the whole read as one expression: the provider value read as
//   itself (GetFieldValue<T>, no boxing) and the portable value built from its members. A shape it does not know returns
//   null and the caller keeps the general conversion, so no rule or failure mode is lost.
// - fieldType is the provider's CLR type, known when a plan is built, so the decision is made once and not per row.
// - Used by DataReaderMapper (plan) and CompiledMapperFactory (gateway).
// =============================================================================

using System.Linq.Expressions;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.@internal;

internal static class ProviderTypedConversions
{
    public static Expression? TryBuild(Type fieldType, Type targetType, Expression record, int ordinal)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition().FullName == "NpgsqlTypes.NpgsqlRange`1" &&
            target.IsGenericType && target.GetGenericTypeDefinition() == typeof(Range<>))
        {
            return BuildRange(fieldType, target, record, ordinal);
        }

        if (fieldType.FullName == "NpgsqlTypes.NpgsqlCidr" && target == typeof(Cidr))
        {
            return BuildCidr(fieldType, record, ordinal);
        }

        if (fieldType == typeof(PhysicalAddress) && target == typeof(MacAddress))
        {
            return Expression.New(typeof(MacAddress).GetConstructor(new[] { typeof(PhysicalAddress) })!,
                Read(typeof(PhysicalAddress), record, ordinal));
        }

        if (fieldType == typeof(Dictionary<string, string?>) && target == typeof(HStore))
        {
            return Expression.New(typeof(HStore).GetConstructor(new[] { typeof(Dictionary<string, string?>) })!,
                Read(typeof(Dictionary<string, string?>), record, ordinal));
        }

        return null;
    }

    // The provider's value read as itself: GetFieldValue<T> on the provider's own reader, with no boxing.
    private static Expression Read(Type providerType, Expression record, int ordinal) =>
        Expression.Call(Expression.Call(NpgsqlTypedFieldReader.UnwrapMethod, record),
            ReaderGetters.GetFieldValueOf(providerType), Expression.Constant(ordinal));

    private static Expression? BuildRange(Type rangeType, Type target, Expression record, int ordinal)
    {
        var element = rangeType.GetGenericArguments()[0];
        var portable = target.GetGenericArguments()[0];

        // A bound whose element differs from the portable one (tstzrange as NpgsqlRange<DateTime>) needs a typed
        // coercion; without one the general conversion decides.
        MethodInfo? coercion = null;
        if (element != portable)
        {
            coercion = TypedCoercions.Find(element, portable);
            if (coercion == null)
            {
                return null;
            }
        }

        PropertyInfo? Member(string name) => rangeType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        var isEmpty = Member("IsEmpty");
        var lowerInfinite = Member("LowerBoundInfinite");
        var upperInfinite = Member("UpperBoundInfinite");
        var lowerInclusive = Member("LowerBoundIsInclusive");
        var upperInclusive = Member("UpperBoundIsInclusive");
        var lowerBound = Member("LowerBound");
        var upperBound = Member("UpperBound");
        var emptyRange = target.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
        var constructor = target.GetConstructor(new[]
            { typeof(Nullable<>).MakeGenericType(portable), typeof(Nullable<>).MakeGenericType(portable), typeof(bool), typeof(bool) });
        if (isEmpty == null || lowerInfinite == null || upperInfinite == null || lowerInclusive == null ||
            upperInclusive == null || lowerBound == null || upperBound == null || emptyRange == null || constructor == null)
        {
            return null;
        }

        var value = Expression.Variable(rangeType, "range");
        var nullable = typeof(Nullable<>).MakeGenericType(portable);

        Expression Bound(PropertyInfo infinite, PropertyInfo bound)
        {
            Expression read = Expression.Property(value, bound);
            if (coercion != null)
            {
                // The static property, not a constant: a closure constant is an array load and a cast on every read.
                read = Expression.Call(coercion, read,
                    Expression.Property(null, typeof(TypeCoercionOptions).GetProperty(nameof(TypeCoercionOptions.Default))!));
            }

            return Expression.Condition(Expression.Property(value, infinite),
                Expression.Constant(null, nullable), Expression.Convert(read, nullable));
        }

        var built = Expression.New(constructor,
            Bound(lowerInfinite, lowerBound), Bound(upperInfinite, upperBound),
            Expression.Property(value, lowerInclusive), Expression.Property(value, upperInclusive));

        return Expression.Block(target, new[] { value },
            Expression.Assign(value, Read(rangeType, record, ordinal)),
            Expression.Condition(Expression.Property(value, isEmpty), Expression.Property(null, emptyRange), built));
    }

    private static Expression? BuildCidr(Type cidrType, Expression record, int ordinal)
    {
        var address = cidrType.GetProperty("Address", BindingFlags.Public | BindingFlags.Instance);
        var netmask = cidrType.GetProperty("Netmask", BindingFlags.Public | BindingFlags.Instance);
        var constructor = typeof(Cidr).GetMethod("FromProvider", BindingFlags.NonPublic | BindingFlags.Static,
            new[] { typeof(IPAddress), typeof(byte) });
        if (address?.PropertyType != typeof(IPAddress) || netmask == null || constructor == null)
        {
            return null;
        }

        var value = Expression.Variable(cidrType, "cidr");
        return Expression.Block(typeof(Cidr), new[] { value },
            Expression.Assign(value, Read(cidrType, record, ordinal)),
            Expression.Call(constructor, Expression.Property(value, address),
                Expression.ConvertChecked(Expression.Property(value, netmask), typeof(byte))));
    }
}
