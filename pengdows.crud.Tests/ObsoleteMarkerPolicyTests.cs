using System;
using System.Reflection;
using pengdows.crud.dialects;
using pengdows.crud.isolation;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Members that are undocumented, unused outside the core, or never thrown are deprecated on 2.0.x with a warning
/// (IsError = false) and a message that says they are removed in 3.0. The API audit register (docs/FUTURE_WORK.md,
/// API-004..API-008) and the 2.0.6 release notes (Deprecations) list the same set.
/// </summary>
public class ObsoleteMarkerPolicyTests
{
    public static TheoryData<Type, string> DeprecatedMembers => new()
    {
        { typeof(ITrackedConnection), nameof(ITrackedConnection.LocalState) },
        { typeof(IDataSourceInformation), nameof(IDataSourceInformation.DefaultPrepareStatements) },
        { typeof(IDataSourceInformation), nameof(IDataSourceInformation.IsUsingFallbackDialect) },
        { typeof(IDatabaseProductInfo), nameof(IDatabaseProductInfo.ProductName) },
        { typeof(IDatabaseProductInfo), nameof(IDatabaseProductInfo.ProductVersion) },
        { typeof(IIsolationResolver), nameof(IIsolationResolver.ResolveWithDetail) },
        { typeof(IIsolationResolver), nameof(IIsolationResolver.GetSupportedLevels) },
        { typeof(ISqlDialect), nameof(ISqlDialect.ParameterMarkerAt) },
        { typeof(pengdows.crud.configuration.DbProviderLoader), nameof(pengdows.crud.configuration.DbProviderLoader.LoadAndRegisterProviders) },
    };

    [Fact]
    public void DbProviderLoader_IsDeprecated_BecauseItsConstructorIsInternal()
    {
        var obsolete = typeof(pengdows.crud.configuration.DbProviderLoader).GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
        Assert.Contains("AddDbProviderLoading", obsolete.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DeprecatedMembers))]
    public void DeprecatedMember_IsWarningOnly_AndSaysRemovedIn3_0(Type declaringType, string memberName)
    {
        var member = (MemberInfo?)declaringType.GetProperty(memberName) ?? declaringType.GetMethod(memberName);
        Assert.NotNull(member);

        var obsolete = member!.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError);
        Assert.Contains("3.0", obsolete.Message, StringComparison.Ordinal);
    }
}
