using System;
using System.Reflection;
using System.Reflection.Emit;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.types.converters;

public static class PostgreSqlRangeConverterTests
{
    private static readonly PostgreSqlRangeConverter<int> Converter = new();

    [Fact]
    public static void ConvertToProvider_FormatsCanonicalRange()
    {
        var range = new Range<int>(1, 10, isLowerInclusive: false, isUpperInclusive: true);
        var formatted = Converter.ToProviderValue(range, SupportedDatabase.PostgreSql);

        Assert.Equal("(1,10]", formatted);
    }

    [Fact]
    public static void TryConvertFromProvider_ParsesCanonicalText()
    {
        Assert.True(Converter.TryConvertFromProvider("[5,)", SupportedDatabase.PostgreSql, out var parsed));
        Assert.True(parsed.IsLowerInclusive);
        Assert.False(parsed.HasUpperBound);
        Assert.Equal(5, parsed.Lower);
    }

    [Fact]
    public static void TryConvertFromProvider_UsesTuple()
    {
        var tuple = Tuple.Create<int?, int?>(3, 7);
        Assert.True(Converter.TryConvertFromProvider(tuple, SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(3, parsed.Lower);
        Assert.Equal(7, parsed.Upper);
    }

    [Fact]
    public static void TryConvertFromProvider_UsesNpgsqlShim()
    {
        var shim = CreateNpgsqlRangeShim(
            lower: 1,
            upper: 5,
            lowerInclusive: true,
            upperInclusive: false,
            lowerInfinite: false,
            upperInfinite: true);

        Assert.True(Converter.TryConvertFromProvider(shim, SupportedDatabase.PostgreSql, out var parsed));
        Assert.Equal(1, parsed.Lower);
        Assert.Null(parsed.Upper);
        Assert.True(parsed.IsLowerInclusive);
        Assert.False(parsed.IsUpperInclusive);
    }

    private static readonly Lazy<Type> NpgsqlRangeShimType = new(CreateNpgsqlRangeShimType);

    private static object CreateNpgsqlRangeShim(
        int? lower,
        int? upper,
        bool lowerInclusive,
        bool upperInclusive,
        bool lowerInfinite,
        bool upperInfinite)
    {
        var type = NpgsqlRangeShimType.Value;
        var instance = Activator.CreateInstance(type)!;
        type.GetProperty("LowerBound")!.SetValue(instance, lower);
        type.GetProperty("UpperBound")!.SetValue(instance, upper);
        type.GetProperty("LowerBoundIsInclusive")!.SetValue(instance, lowerInclusive);
        type.GetProperty("UpperBoundIsInclusive")!.SetValue(instance, upperInclusive);
        type.GetProperty("LowerBoundInfinite")!.SetValue(instance, lowerInfinite);
        type.GetProperty("UpperBoundInfinite")!.SetValue(instance, upperInfinite);
        return instance;
    }

    private static Type CreateNpgsqlRangeShimType()
    {
        var assemblyName = new AssemblyName("NpgsqlRangeShimAssembly");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("Main");
        var typeBuilder = moduleBuilder.DefineType(
            "NpgsqlTypes.NpgsqlRange",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

        DefineAutoProperty(typeBuilder, "LowerBound", typeof(int?));
        DefineAutoProperty(typeBuilder, "UpperBound", typeof(int?));
        DefineAutoProperty(typeBuilder, "LowerBoundIsInclusive", typeof(bool));
        DefineAutoProperty(typeBuilder, "UpperBoundIsInclusive", typeof(bool));
        DefineAutoProperty(typeBuilder, "LowerBoundInfinite", typeof(bool));
        DefineAutoProperty(typeBuilder, "UpperBoundInfinite", typeof(bool));

        return typeBuilder.CreateTypeInfo()!.AsType();
    }

    private static void DefineAutoProperty(TypeBuilder typeBuilder, string name, Type propertyType)
    {
        var field = typeBuilder.DefineField($"_{name}", propertyType, FieldAttributes.Private);
        var property = typeBuilder.DefineProperty(name, PropertyAttributes.None, propertyType, null);
        var methodAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = typeBuilder.DefineMethod($"get_{name}", methodAttributes, propertyType, Type.EmptyTypes);
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, field);
        getterIl.Emit(OpCodes.Ret);

        var setter = typeBuilder.DefineMethod($"set_{name}", methodAttributes, null, new[] { propertyType });
        var setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, field);
        setterIl.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
    }

}
