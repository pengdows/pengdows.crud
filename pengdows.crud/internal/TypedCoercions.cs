using System.Reflection;
using pengdows.crud.types.coercion;

namespace pengdows.crud.@internal;

/// <summary>
/// Date, time and Guid conversions both mappers make without boxing the value or its result
/// (PERF-021). Each gives what <see cref="TypeCoercionHelper.Coerce(object?, Type, Type, TypeCoercionOptions?)"/>
/// gives for the same value, and fails the same way: a value these don't handle goes through Coerce.
/// </summary>
internal static class TypedCoercions
{
    private static readonly Dictionary<(Type Source, Type Target), MethodInfo> Methods = new()
    {
        [(typeof(DateTime), typeof(DateOnly))] = Method(nameof(DateOnlyFromDateTime)),
        [(typeof(DateTime), typeof(DateTimeOffset))] = Method(nameof(DateTimeOffsetFromDateTime)),
        [(typeof(string), typeof(Guid))] = Method(nameof(GuidFromText)),
        [(typeof(TimeSpan), typeof(TimeOnly))] = Method(nameof(TimeOnlyFromTimeSpan)),
        [(typeof(string), typeof(DateTime))] = Method(nameof(DateTimeFromText)),
        [(typeof(string), typeof(DateTimeOffset))] = Method(nameof(DateTimeOffsetFromText)),
        [(typeof(string), typeof(DateOnly))] = Method(nameof(DateOnlyFromText)),
        [(typeof(string), typeof(TimeOnly))] = Method(nameof(TimeOnlyFromText))
    };

    public static MethodInfo? Find(Type source, Type target) => Methods.GetValueOrDefault((source, target));

    private static MethodInfo Method(string name) =>
        typeof(TypedCoercions).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    // DateOnlyCoercion's DateTime case.
    private static DateOnly DateOnlyFromDateTime(DateTime value, TypeCoercionOptions options) =>
        DateOnly.FromDateTime(value);

    private static DateTimeOffset DateTimeOffsetFromDateTime(DateTime value, TypeCoercionOptions options) =>
        TypeCoercionHelper.DateTimeOffsetFromDateTime(value, options);

    private static Guid GuidFromText(string value, TypeCoercionOptions options) =>
        string.IsNullOrWhiteSpace(value) ? throw TypeCoercionHelper.BlankText(typeof(Guid)) : Guid.Parse(value);

    // TimeOnlyCoercion's TimeSpan case; a span outside one day fails through Coerce.
    private static TimeOnly TimeOnlyFromTimeSpan(TimeSpan value, TypeCoercionOptions options) =>
        value >= TimeSpan.Zero && value.Ticks < TimeSpan.TicksPerDay
            ? TimeOnly.FromTimeSpan(value)
            : (TimeOnly)TypeCoercionHelper.Coerce(value, typeof(TimeSpan), typeof(TimeOnly), options)!;

    private static DateTime DateTimeFromText(string value, TypeCoercionOptions options) =>
        string.IsNullOrWhiteSpace(value)
            ? throw TypeCoercionHelper.BlankText(typeof(DateTime))
            : TypeCoercionHelper.CoerceDateTimeFromString(value);

    private static DateTimeOffset DateTimeOffsetFromText(string value, TypeCoercionOptions options) =>
        string.IsNullOrWhiteSpace(value)
            ? throw TypeCoercionHelper.BlankText(typeof(DateTimeOffset))
            : TypeCoercionHelper.CoerceDateTimeOffsetFromString(value);

    private static DateOnly DateOnlyFromText(string value, TypeCoercionOptions options) =>
        FromText<DateOnly>(value, options);

    private static TimeOnly TimeOnlyFromText(string value, TypeCoercionOptions options) =>
        FromText<TimeOnly>(value, options);

    // The registry's coercion for the provider, read typed; text it can't read fails through Coerce.
    private static T FromText<T>(string value, TypeCoercionOptions options) where T : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw TypeCoercionHelper.BlankText(typeof(T));
        }

        return CoercionRegistry.Shared.GetCoercion(typeof(T), options.Provider) is IDbCoercion<T> coercion &&
               coercion.TryRead(new DbValue(value, typeof(string)), out var read)
            ? read
            : (T)TypeCoercionHelper.Coerce(value, typeof(string), typeof(T), options)!;
    }
}
