using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;

namespace pengdows.crud.Tests.@internal;

/// <summary>
/// Stand-ins for Npgsql's value types. The library finds them with <c>Type.GetType("NpgsqlTypes.X, Npgsql")</c>, so the
/// types are emitted into a dynamic assembly named <c>Npgsql</c>. That assembly is resolved only while a scope from
/// <see cref="Resolve"/> is open, so the test process never gains a real Npgsql dependency.
/// </summary>
internal static class NpgsqlProviderStandIns
{
    private static readonly object Gate = new();
    private static AssemblyBuilder? _assembly;
    private static ModuleBuilder? _module;
    private static readonly Dictionary<string, Type> Built = new(StringComparer.Ordinal);

    public static Type Interval => Define("NpgsqlTypes.NpgsqlInterval",
        ("Months", typeof(int)), ("Days", typeof(int)), ("Time", typeof(long)));

    public static Type Inet => Define("NpgsqlTypes.NpgsqlInet",
        ("Address", typeof(IPAddress)), ("Netmask", typeof(int)));

    /// <summary>Makes <c>Type.GetType("... , Npgsql")</c> resolve to the stand-in assembly until disposed.</summary>
    public static IDisposable Resolve()
    {
        EnsureAssembly();
        ResolveEventHandler handler = (_, args) =>
            args.Name?.Split(',')[0] == "Npgsql" ? _assembly : null;
        AppDomain.CurrentDomain.AssemblyResolve += handler;
        return new Scope(handler);
    }

    public static object NewInterval(Type type, int months, int days, long time)
    {
        var value = Activator.CreateInstance(type)!;
        type.GetProperty("Months")!.SetValue(value, months);
        type.GetProperty("Days")!.SetValue(value, days);
        type.GetProperty("Time")!.SetValue(value, time);
        return value;
    }

    public static object NewInet(Type type, IPAddress address, int netmask)
    {
        var value = Activator.CreateInstance(type)!;
        type.GetProperty("Address")!.SetValue(value, address);
        type.GetProperty("Netmask")!.SetValue(value, netmask);
        return value;
    }

    private static Type Define(string fullName, params (string Name, Type Type)[] properties)
    {
        lock (Gate)
        {
            if (Built.TryGetValue(fullName, out var existing))
            {
                return existing;
            }

            EnsureAssemblyLocked();
            var builder = _module!.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                typeof(ValueType));
            foreach (var (name, type) in properties)
            {
                EmitProperty(builder, name, type);
            }

            var created = builder.CreateType()!;
            Built[fullName] = created;
            return created;
        }
    }

    private static void EmitProperty(TypeBuilder builder, string name, Type type)
    {
        var field = builder.DefineField("_" + name, type, FieldAttributes.Private);
        var property = builder.DefineProperty(name, PropertyAttributes.None, type, null);

        var getter = builder.DefineMethod("get_" + name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, type, Type.EmptyTypes);
        var get = getter.GetILGenerator();
        get.Emit(OpCodes.Ldarg_0);
        get.Emit(OpCodes.Ldfld, field);
        get.Emit(OpCodes.Ret);

        var setter = builder.DefineMethod("set_" + name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, new[] { type });
        var set = setter.GetILGenerator();
        set.Emit(OpCodes.Ldarg_0);
        set.Emit(OpCodes.Ldarg_1);
        set.Emit(OpCodes.Stfld, field);
        set.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
    }

    private static void EnsureAssembly()
    {
        lock (Gate)
        {
            EnsureAssemblyLocked();
        }
    }

    private static void EnsureAssemblyLocked()
    {
        if (_assembly != null)
        {
            return;
        }

        _assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Npgsql"), AssemblyBuilderAccess.Run);
        _module = _assembly.DefineDynamicModule("Npgsql");
    }

    private sealed class Scope : IDisposable
    {
        private readonly ResolveEventHandler _handler;

        public Scope(ResolveEventHandler handler) => _handler = handler;

        public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= _handler;
    }
}
