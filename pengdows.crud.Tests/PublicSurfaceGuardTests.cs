using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.dialects;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Public is permanent: adding an API later costs a minor version and removing one costs a major, so nothing is public
/// unless a consumer needs it. These pin what 2.0.6 pulled back before it shipped, and keep raw ADO.NET objects from
/// spreading further through the public surface than the three properties that already shipped in 2.0.5.
/// </summary>
public class PublicSurfaceGuardTests
{
    private static readonly Assembly Core = typeof(DatabaseContext).Assembly;
    private static readonly Assembly Abstractions = typeof(ISqlDialect).Assembly;
    private static readonly Assembly FakeDb = typeof(fakeDbFactory).Assembly;

    // The provider objects whose lifetime the library governs. Matched by exact type: the tracked wrappers that stand
    // in for them are the governed alternative, not a leak.
    private static readonly HashSet<Type> RawAdoNet = new()
    {
        typeof(DbConnection), typeof(DbCommand), typeof(DbDataReader), typeof(DbTransaction), typeof(DbDataSource),
        typeof(DbBatch), typeof(IDbConnection), typeof(IDbCommand), typeof(IDataReader), typeof(IDbTransaction)
    };

    public static TheoryData<string> ProviderShapedDoubles => new()
    {
        "fakeDbNpgsqlParameter",
        "fakeDbOracleParameter",
        "fakeDbInformixParameter",
        "fakeDbInterBaseParameter",
        "fakeNpgsqlDbType",
        "fakeOracleDbType",
        "fakeIfxType",
        "fakeIBDbType",
        "fakeDbProviderDecimal"
    };

    public static TheoryData<string> DialectPlumbing => new()
    {
        "IsEmbeddedSingleWriterEngine",
        "DetectInMemoryKind",
        "GetReleaseSavepointSql",
        "CoerceConnectionMode",
        "IsClientServerDatabase",
        "SupportsPaging",
        "SupportsSemicolonStatementSeparator",
        "JoinParenthesization"
    };

    // fakeDb imitates provider parameters so the library's reflection-based code has something of the right shape to
    // set. That is how fakeDb works, not something a consumer needs; what a test inspects is CapturedParameter, which
    // reports the provider type by name.
    [Theory]
    [MemberData(nameof(ProviderShapedDoubles))]
    public void FakeDb_ProviderShapedDoubles_ExistButAreNotPublic(string name)
    {
        var type = FakeDb.GetTypes().SingleOrDefault(t => t.Name == name);

        Assert.NotNull(type);
        Assert.False(type!.IsVisible, $"{name} is part of the public API of pengdows.crud.fakeDb.");
    }

    // How the context and its dialects divide the work between them is the library's own business.
    [Theory]
    [MemberData(nameof(DialectPlumbing))]
    public void ISqlDialect_DoesNotExposeTheContextsOwnPlumbing(string member)
    {
        Assert.Empty(typeof(ISqlDialect).GetMember(member));
    }

    [Theory]
    [InlineData("InMemoryKind")]
    [InlineData("JoinParenthesization")]
    public void TheEnumsBehindThatPlumbing_AreNotPublic(string name)
    {
        var visible = new[] { Core, Abstractions, FakeDb }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsVisible && t.Name == name)
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(visible);
    }

    // A nested protected type is API for every subclass of the gateway.
    [Fact]
    public void TheGatewaysAuditSnapshot_IsNotVisibleToSubclasses()
    {
        var nested = typeof(BaseTableGateway<>)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(t => t.Name == "AuditFieldSnapshot");

        Assert.True(nested == null || nested.IsNestedPrivate || nested.IsNestedAssembly,
            "AuditFieldSnapshot is visible to subclasses of BaseTableGateway.");
    }

    // The constructor that takes a DbDataSource shipped in 2.0.5. A second way in is not needed, and a data source is
    // exactly the object that lets a caller bypass the governor.
    [Fact]
    public void DatabaseContext_HasNoNewPublicFactoryThatTakesADataSource()
    {
        var factories = typeof(DatabaseContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "CreateAsync" && m.GetParameters().Any(p => p.ParameterType == typeof(DbDataSource)))
            .Select(m => m.ToString())
            .ToList();

        Assert.Empty(factories);
    }

    // Pool governance (no leaked connections, readers or transactions) depends on callers never holding a raw
    // ADO.NET object. Three DataSource properties shipped in 2.0.5 and can only go in 3.0 (docs/FUTURE_WORK.md,
    // API-001); nothing may be added to them.
    [Fact]
    public void NoPublicOutputExposesARawAdoNetObject_BeyondTheThreeThatShippedIn205()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in new[] { Core, Abstractions }.SelectMany(a => a.GetTypes()).Where(IsReachableFromOutside))
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMembers(all))
            {
                if (OutputTypeOf(member) is { } output && IsRaw(output))
                {
                    found.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.Equal(
            new[]
            {
                "pengdows.crud.DatabaseContext.DataSource",
                "pengdows.crud.IDatabaseContext.DataSource",
                "pengdows.crud.TransactionContext.DataSource"
            },
            found.ToArray());
    }

    private static bool IsReachableFromOutside(Type type) =>
        type.IsVisible || (type.IsNested && (type.IsNestedFamily || type.IsNestedFamORAssem));

    // What a member hands back: a property's or field's type, an event's handler, a method's return type.
    private static Type? OutputTypeOf(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo p when ReachableAccessor(p.GetMethod) || ReachableAccessor(p.SetMethod):
                return p.PropertyType;
            case FieldInfo f when f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly:
                return f.FieldType;
            case EventInfo e when ReachableAccessor(e.AddMethod):
                return e.EventHandlerType;
            case MethodInfo m when !m.IsSpecialName && ReachableAccessor(m):
                return m.ReturnType;
            default:
                return null;
        }
    }

    private static bool ReachableAccessor(MethodInfo? method) =>
        method != null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    private static bool IsRaw(Type type)
    {
        if (RawAdoNet.Contains(type))
        {
            return true;
        }

        if (type.IsArray)
        {
            return IsRaw(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsRaw);
    }
}
