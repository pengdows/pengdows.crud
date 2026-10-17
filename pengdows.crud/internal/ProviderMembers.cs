// =============================================================================
// FILE: ProviderMembers.cs
// PURPOSE: Reads and builds provider types (Npgsql's NpgsqlRange, NpgsqlInet, ...) without per-value reflection.
//
// AI SUMMARY:
// - pengdows.crud takes no dependency on Npgsql, so its types are found by name. Reading a member with
//   PropertyInfo.GetValue or building a value with ConstructorInfo.Invoke(object[]) on every value cost about a
//   kilobyte (the lookup, the boxing, the argument array).
// - Getter<TResult>(type, name) compiles `(object o) => ((Owner)o).Member` once per (type, member, result type); a
//   numeric member is converted checked, as Convert.ToXxx was. Null when the member is absent or can't convert.
// - Constructor<...>(type) compiles `(a, b, ...) => (object)new Type(a, b, ...)` once per type, with one static
//   cache per argument-type combination so a lookup allocates nothing.
// - Used by the PostgreSQL-family value converters (range, interval, inet, cidr, MAC address).
// =============================================================================

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace pengdows.crud.@internal;

internal static class ProviderMembers
{
    private static readonly ConcurrentDictionary<(Type Owner, string Name, Type Result), Delegate?> Getters = new();

    /// <summary>A getter for a public instance property or field, converted (checked) to <typeparamref name="TResult"/>.</summary>
    public static Func<object, TResult>? Getter<TResult>(Type owner, string name) =>
        (Func<object, TResult>?)Getters.GetOrAdd((owner, name, typeof(TResult)),
            static key => BuildGetter(key.Owner, key.Name, key.Result));

    private static Delegate? BuildGetter(Type owner, string name, Type result)
    {
        MemberInfo? member;
        try
        {
            member = (MemberInfo?)owner.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                     ?? owner.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }

        if (member == null)
        {
            return null;
        }

        try
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            Expression access = Expression.MakeMemberAccess(Expression.Convert(instance, owner), member);
            if (access.Type != result)
            {
                access = Expression.ConvertChecked(access, result);
            }

            return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(object), result), access, instance).Compile();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static class Ctor<T1, T2>
    {
        public static readonly ConcurrentDictionary<Type, Func<T1, T2, object>?> Cache = new();
    }

    private static class Ctor<T1, T2, T3>
    {
        public static readonly ConcurrentDictionary<Type, Func<T1, T2, T3, object>?> Cache = new();
    }

    private static class Ctor<T1, T2, T3, T4, T5, T6>
    {
        public static readonly ConcurrentDictionary<Type, Func<T1, T2, T3, T4, T5, T6, object>?> Cache = new();
    }

    public static Func<T1, T2, object>? Constructor<T1, T2>(Type type) =>
        Ctor<T1, T2>.Cache.GetOrAdd(type, static t => Build<Func<T1, T2, object>>(t, typeof(T1), typeof(T2)));

    public static Func<T1, T2, T3, object>? Constructor<T1, T2, T3>(Type type) =>
        Ctor<T1, T2, T3>.Cache.GetOrAdd(type, static t => Build<Func<T1, T2, T3, object>>(t, typeof(T1), typeof(T2), typeof(T3)));

    public static Func<T1, T2, T3, T4, T5, T6, object>? Constructor<T1, T2, T3, T4, T5, T6>(Type type) =>
        Ctor<T1, T2, T3, T4, T5, T6>.Cache.GetOrAdd(type, static t => Build<Func<T1, T2, T3, T4, T5, T6, object>>(
            t, typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6)));

    private static TDelegate? Build<TDelegate>(Type type, params Type[] arguments) where TDelegate : class
    {
        var constructor = type.GetConstructor(arguments);
        if (constructor == null)
        {
            return null;
        }

        var parameters = arguments.Select((t, i) => Expression.Parameter(t, "a" + i)).ToArray();
        return Expression.Lambda<TDelegate>(Expression.Convert(Expression.New(constructor, parameters), typeof(object)),
            parameters).Compile();
    }
}

/// <summary>
/// A provider type shaped like NpgsqlInet / NpgsqlCidr: an Address (IPAddress) and an optional Netmask, matched by
/// shape and not by name (a provider's own type, and Npgsql 8's NpgsqlCidr, have it). Compiled once per type.
/// </summary>
internal sealed class ProviderAddress
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ProviderAddress?> Shapes = new();

    private ProviderAddress(Func<object, System.Net.IPAddress?> address, Func<object, byte?>? prefix)
    {
        Address = address;
        Prefix = prefix;
    }

    public Func<object, System.Net.IPAddress?> Address { get; }

    /// <summary>Null when the type has no Netmask member.</summary>
    public Func<object, byte?>? Prefix { get; }

    public static ProviderAddress? Of(Type type) => Shapes.GetOrAdd(type, static t =>
    {
        var address = ProviderMembers.Getter<System.Net.IPAddress?>(t, "Address");
        return address == null ? null : new ProviderAddress(address, ProviderMembers.Getter<byte?>(t, "Netmask"));
    });
}
