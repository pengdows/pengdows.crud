using System;
using System.Data.SqlTypes;
using System.Reflection;
using System.Reflection.Emit;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class SpatialConverterSqlServerBranchTests
{
    private static readonly object Sync = new();
    private static bool _loaded;
    private static Assembly? _sqlServerTypesAssembly;

    [Fact]
    public void TryConvertFromProvider_UnknownObject_ReturnsFalseAndNull()
    {
        var converter = new TestSpatialConverter();

        var success = converter.TryConvertFromProvider(new object(), SupportedDatabase.Sqlite, out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Wkb_UsesDynamicSqlGeometryType()
    {
        EnsureSqlServerTypesLoaded();

        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownBinary(new byte[] { 1, 2, 3, 4 }, 4326);

        var providerValue = converter.ToProviderValue(value, SupportedDatabase.SqlServer);

        Assert.NotNull(providerValue);
        Assert.Equal("Microsoft.SqlServer.Types.SqlGeometry", providerValue!.GetType().FullName);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Wkt_UsesTextFactoryPath()
    {
        EnsureSqlServerTypesLoaded();

        var converter = new GeometryConverter();
        var value = Geometry.FromWellKnownText("POINT(1 2)", 4326);

        var providerValue = converter.ToProviderValue(value, SupportedDatabase.SqlServer);

        Assert.NotNull(providerValue);
        Assert.Equal("Microsoft.SqlServer.Types.SqlGeometry", providerValue!.GetType().FullName);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_Geography_UsesGeographyFactoryPath()
    {
        EnsureSqlServerTypesLoaded();

        var converter = new GeographyConverter();
        var value = Geography.FromWellKnownText("POINT(3 4)", 4326);

        var providerValue = converter.ToProviderValue(value, SupportedDatabase.SqlServer);

        Assert.NotNull(providerValue);
        Assert.Equal("Microsoft.SqlServer.Types.SqlGeography", providerValue!.GetType().FullName);
    }

    [Fact]
    public void ConvertToProvider_SqlServer_NoTextOrBinary_ThrowsInvalidOperationException()
    {
        EnsureSqlServerTypesLoaded();

        var converter = new GeometryConverter();
        var ctor = typeof(Geometry).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            new[]
            {
                typeof(int),
                typeof(SpatialFormat),
                typeof(ReadOnlyMemory<byte>),
                typeof(string),
                typeof(string),
                typeof(object)
            },
            modifiers: null) ?? throw new InvalidOperationException("Geometry constructor was not found.");

        var invalid = (Geometry)ctor.Invoke(new object?[]
        {
            4326,
            SpatialFormat.WellKnownText,
            ReadOnlyMemory<byte>.Empty,
            null,
            null,
            null
        });

        var ex = Assert.Throws<InvalidOperationException>(() => converter.ToProviderValue(invalid, SupportedDatabase.SqlServer));
        Assert.Contains("must contain WKB or WKT", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSqlServerTypesLoaded()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            if (Type.GetType("Microsoft.SqlServer.Types.SqlGeometry, Microsoft.SqlServer.Types") != null)
            {
                _loaded = true;
                return;
            }

            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Microsoft.SqlServer.Types"),
                AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule("Microsoft.SqlServer.Types");

            var sqlBytesType = DefineWrapperType(module, "Microsoft.SqlServer.Types.SqlBytes", typeof(byte[]));
            var sqlCharsType = DefineWrapperType(module, "Microsoft.SqlServer.Types.SqlChars", typeof(char[]));

            DefineSpatialType(module, "Microsoft.SqlServer.Types.SqlGeometry", sqlBytesType, sqlCharsType);
            DefineSpatialType(module, "Microsoft.SqlServer.Types.SqlGeography", sqlBytesType, sqlCharsType);

            _sqlServerTypesAssembly = assembly;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSqlServerTypes;

            _loaded = true;
        }
    }

    private static Assembly? ResolveSqlServerTypes(object? sender, ResolveEventArgs args)
    {
        return args.Name.StartsWith("Microsoft.SqlServer.Types", StringComparison.Ordinal)
            ? _sqlServerTypesAssembly
            : null;
    }

    private static Type DefineWrapperType(ModuleBuilder module, string typeName, Type argumentType)
    {
        var builder = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
        var ctor = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { argumentType });

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ret);

        return builder.CreateType()!;
    }

    private static void DefineSpatialType(ModuleBuilder module, string typeName, Type sqlBytesType, Type sqlCharsType)
    {
        var builder = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);

        var defaultCtor = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var ctorIl = defaultCtor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ret);

        DefineFactoryMethod(builder, "STGeomFromWKB", sqlBytesType, defaultCtor, typeof(SqlInt32));
        DefineFactoryMethod(builder, "STGeomFromText", sqlCharsType, defaultCtor, typeof(SqlInt32));

        _ = builder.CreateType();
    }

    private static void DefineFactoryMethod(TypeBuilder owner, string name, Type firstArg, ConstructorBuilder ctor,
        Type secondArg)
    {
        var method = owner.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            owner,
            new[] { firstArg, secondArg });

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
    }

    private sealed class TestSpatialConverter : SpatialConverter<Geometry>
    {
        protected override Geometry FromBinary(ReadOnlySpan<byte> wkb, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownBinary(wkb, 4326);
        }

        protected override Geometry FromTextInternal(string text, SupportedDatabase provider)
        {
            return Geometry.FromWellKnownText(text, 4326);
        }

        protected override Geometry FromGeoJsonInternal(string json, SupportedDatabase provider)
        {
            return Geometry.FromGeoJson(json, 4326);
        }

        protected override Geometry WrapWithProvider(Geometry spatial, object providerValue)
        {
            return spatial.WithProviderValue(providerValue);
        }
    }
}
