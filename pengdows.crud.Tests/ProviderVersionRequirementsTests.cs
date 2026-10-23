using System;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// MySqlConnector 2.3.0 through 2.6.0 fail a prepared INSERT ... RETURNING executed a second time on
/// MariaDB ("Expected result set to have 0 columns, but it contains 1 columns") and close the connection;
/// 2.6.1 fixed it (confirmed live on MariaDB 11.4.13, 2026-10-22). pengdows.crud refuses MySqlConnector older
/// than 2.6.1. Every 2.x release reports assembly version 2.0.0.0, so the file version is what is read.
/// </summary>
public sealed class ProviderVersionRequirementsTests
{
    [Theory]
    [InlineData("2.0.0.0")]
    [InlineData("2.3.0.0")]
    [InlineData("2.4.0.0")]
    [InlineData("2.6.0.0")]
    public void MySqlConnector_BelowMinimum_Throws(string fileVersion)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ProviderVersionRequirements.EnsureSupported("MySqlConnector", Version.Parse(fileVersion)));

        Assert.Contains("2.6.1", ex.Message);
        Assert.Contains(fileVersion, ex.Message);
    }

    [Theory]
    [InlineData("2.6.1.0")]
    [InlineData("2.6.2.0")]
    [InlineData("3.0.0.0")]
    public void MySqlConnector_AtOrAboveMinimum_IsAccepted(string fileVersion)
    {
        ProviderVersionRequirements.EnsureSupported("MySqlConnector", Version.Parse(fileVersion));
    }

    [Theory]
    [InlineData("Npgsql", "2.4.0.0")]
    [InlineData("Microsoft.Data.SqlClient", "5.2.0.0")]
    public void OtherProviders_AreNotAffected(string assemblyName, string fileVersion)
    {
        ProviderVersionRequirements.EnsureSupported(assemblyName, Version.Parse(fileVersion));
    }

    // MySql.Data is refused at every version (decision 2026-10-22): it fails the ADO.NET contract that pengdows.crud
    // relies on (connection state events, reader state, parameter defaults), with the fixes not yet released.
    [Theory]
    [InlineData("8.4.0.0")]
    [InlineData("9.3.0.0")]
    [InlineData("9.7.0.0")]
    [InlineData("26.10.0.0")]
    public void MySqlData_IsRefused(string fileVersion)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ProviderVersionRequirements.EnsureSupported("MySql.Data", Version.Parse(fileVersion)));

        Assert.Contains("MySql.Data", ex.Message);
        Assert.Contains("MySqlConnector", ex.Message);
    }

    [Fact]
    public void MySqlData_WithNoFileVersion_IsRefused()
    {
        Assert.Throws<NotSupportedException>(() => ProviderVersionRequirements.EnsureSupported("MySql.Data", (Version?)null));
    }

    [Fact]
    public void DatabaseContext_WithMySqlData_ThrowsBeforeConnecting()
    {
        var factory = FactoryFromAssembly("MySql.Data", "9.7.0.0");

        var ex = Assert.Throws<NotSupportedException>(() =>
            new DatabaseContext("Server=unreachable.invalid;Database=x", factory));

        Assert.Contains("MySqlConnector", ex.Message);
    }

    // pengdows.flatfile previews all report file version 0.2.1.0, so the informational version is compared, with
    // prerelease ordering. 0.2.1-preview.5 cannot filter a DATE or TIME column (pengdows/pengdows.flatfile#1).
    [Theory]
    [InlineData("0.2.1-preview.2+9b7a2bab")]
    [InlineData("0.2.1-preview.5+c0b13e67")]
    [InlineData("0.2.0")]
    public void FlatFile_BeforePreview6_Throws(string informationalVersion)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ProviderVersionRequirements.EnsureSupported("pengdows.flatfile", informationalVersion));

        Assert.Contains("0.2.1-preview.6", ex.Message);
    }

    [Theory]
    [InlineData("0.2.1-preview.6+7e2dec6")]
    [InlineData("0.2.1-preview.10")]
    [InlineData("0.2.1")]
    [InlineData("0.3.0-alpha.1")]
    public void FlatFile_Preview6OrLater_IsAccepted(string informationalVersion)
    {
        ProviderVersionRequirements.EnsureSupported("pengdows.flatfile", informationalVersion);
    }

    [Fact]
    public void DatabaseContext_WithOldFlatFile_ThrowsBeforeConnecting()
    {
        var factory = FactoryFromAssembly("pengdows.flatfile", "0.2.1.0", "0.2.1-preview.5+c0b13e67");

        var ex = Assert.Throws<NotSupportedException>(() => new DatabaseContext("path=unused", factory));

        Assert.Contains("0.2.1-preview.6", ex.Message);
    }

    [Fact]
    public void MySqlConnector_WithNoFileVersion_IsAccepted()
    {
        // Nothing to compare against; refusing would block a driver we cannot identify.
        ProviderVersionRequirements.EnsureSupported("MySqlConnector", (Version?)null);
    }

    [Fact]
    public void DatabaseContext_WithOldMySqlConnector_ThrowsBeforeConnecting()
    {
        var factory = FactoryFromAssembly("MySqlConnector", "2.4.0.0");

        var ex = Assert.Throws<NotSupportedException>(() =>
            new DatabaseContext("Server=unreachable.invalid;Database=x", factory));

        Assert.Contains("2.6.1", ex.Message);
    }

    // A DbProviderFactory from an in-memory assembly carrying the given name and file version, so the
    // check runs against what the real driver reports without loading an old driver.
    private static DbProviderFactory FactoryFromAssembly(string assemblyName, string fileVersion, string? informationalVersion = null)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyFileVersionAttribute).GetConstructor(new[] { typeof(string) })!,
            new object[] { fileVersion }));
        if (informationalVersion != null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor(new[] { typeof(string) })!,
                new object[] { informationalVersion }));
        }
        var type = builder.DefineDynamicModule(assemblyName)
            .DefineType(assemblyName + ".FakeFactory", TypeAttributes.Public | TypeAttributes.Sealed, typeof(DbProviderFactory));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return (DbProviderFactory)Activator.CreateInstance(type.CreateType()!)!;
    }
}
