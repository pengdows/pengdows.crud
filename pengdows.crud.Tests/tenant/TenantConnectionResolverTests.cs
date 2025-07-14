#region

using System;
using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

#endregion

namespace pengdows.crud.Tests.tenant;

public class TenantConnectionResolverTests
{
    [Fact]
    public void Register_MultipleTenants_Should_StoreAllConfigurations()
    {
        // Arrange
        var tenantA = new TenantConfiguration
        {
            Name = "a",
            DatabaseContextConfiguration = new DatabaseContextConfiguration
            {
                ConnectionString = "Server=A;",
                DbMode = DbMode.Standard,
                ReadWriteMode = ReadWriteMode.ReadWrite
            }
        };

        var tenantB = new TenantConfiguration
        {
            Name = "b",
            DatabaseContextConfiguration = new DatabaseContextConfiguration
            {
                ConnectionString = "Server=B;",
                DbMode = DbMode.Standard,
                ReadWriteMode = ReadWriteMode.ReadWrite
            }
        };

        var list = new[] { tenantA, tenantB };

        // Act
        TenantConnectionResolver.Register(list);
        var resultA = TenantConnectionResolver.Instance.GetDatabaseContextConfiguration("a");
        var resultB = TenantConnectionResolver.Instance.GetDatabaseContextConfiguration("b");

        // Assert
        Assert.Same(tenantA.DatabaseContextConfiguration, resultA);
        Assert.Same(tenantB.DatabaseContextConfiguration, resultB);
    }

    [Fact]
    public void GetTenantInfo_ReturnsExpectedTenantInformation()
    {
        // Arrange
        ITenantConnectionResolver resolver = new TestTenantConnectionResolver();
        var tenant = "acme";

        // Act
        var info = resolver.GetDatabaseContextConfiguration(tenant);

        // Assert
        Assert.Equal("Microsoft.Data.Sqlite", info.ProviderName);
        Assert.Equal("Server=db;Database=acme;", info.ConnectionString);
    }

    [Fact]
    public void Register_And_GetConfiguration_Should_ReturnSameInstance()
    {
        // Arrange
        var tenantId = "tenant-a";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        // Act
        TenantConnectionResolver.Register(tenantId, config);
        var result = TenantConnectionResolver.Instance.GetDatabaseContextConfiguration(tenantId);

        // Assert
        Assert.Same(config, result);
    }

    [Fact]
    public void GetConfiguration_UnregisteredTenant_ShouldThrow()
    {
        // Arrange
        var unknownTenant = "nonexistent";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TenantConnectionResolver.Instance.GetDatabaseContextConfiguration(unknownTenant));

        Assert.Contains(unknownTenant, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_InvalidTenant_ShouldThrow(string invalidTenant)
    {
        var config = new DatabaseContextConfiguration();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            TenantConnectionResolver.Register(invalidTenant, config));

        Assert.Equal("tenant", ex.ParamName);
    }

    [Fact]
    public void Register_NullConfiguration_ShouldThrow()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            TenantConnectionResolver.Register("tenant-x", null!));

        Assert.Equal("configuration", ex.ParamName);
    }

    [Fact]
    public void GetConfiguration_NullTenant_ShouldThrow()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            TenantConnectionResolver.Instance.GetDatabaseContextConfiguration(null!));

        Assert.Equal("tenant", ex.ParamName);
    }

    [Fact]
    public void Register_WithOptions_ShouldStoreConfigurations()
    {
        var options = new MultiTenantOptions
        {
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = "opts-a",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=OptA;",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                },
                new()
                {
                    Name = "opts-b",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=OptB;",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        TenantConnectionResolver.Register(options);

        Assert.Equal("Server=OptA;",
            TenantConnectionResolver.Instance.GetDatabaseContextConfiguration("opts-a").ConnectionString);
        Assert.Equal("Server=OptB;",
            TenantConnectionResolver.Instance.GetDatabaseContextConfiguration("opts-b").ConnectionString);
    }

    [Fact]
    public void Register_NullOptions_ShouldThrow()
    {
        var ex =
            Assert.Throws<ArgumentNullException>(() => TenantConnectionResolver.Register((MultiTenantOptions)null!));
        Assert.Equal("options", ex.ParamName);
    }

    private class TestTenantConnectionResolver : ITenantConnectionResolver
    {
        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant)
        {
            return new DatabaseContextConfiguration
            {
                ConnectionString = $"Server=db;Database={tenant};",
                ProviderName = "Microsoft.Data.Sqlite",
                DbMode = DbMode.SingleConnection,
                ReadWriteMode = ReadWriteMode.ReadWrite
            };
        }
    }
}