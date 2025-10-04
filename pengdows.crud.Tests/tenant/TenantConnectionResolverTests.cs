using System;
using System.Collections.Generic;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

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
                ProviderName = "fake-a",
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
                ProviderName = "fake-b",
                DbMode = DbMode.Standard,
                ReadWriteMode = ReadWriteMode.ReadWrite
            }
        };

        var list = new[] { tenantA, tenantB };

        // Act
        var resolver = new TenantConnectionResolver();
        resolver.Register(list);
        var resultA = resolver.GetDatabaseContextConfiguration("a");
        var resultB = resolver.GetDatabaseContextConfiguration("b");

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
            ProviderName = "fake-a",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        // Act
        var resolver = new TenantConnectionResolver();
        resolver.Register(tenantId, config);
        var result = resolver.GetDatabaseContextConfiguration(tenantId);

        // Assert
        Assert.Same(config, result);
    }

    [Fact]
    public void GetConfiguration_UnregisteredTenant_ShouldThrow()
    {
        // Arrange
        var unknownTenant = "nonexistent";

        // Act & Assert
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            resolver.GetDatabaseContextConfiguration(unknownTenant));

        Assert.Contains(unknownTenant, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_InvalidTenant_ShouldThrow(string invalidTenant)
    {
        var config = new DatabaseContextConfiguration();

        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            resolver.Register(invalidTenant, config));

        Assert.Equal("tenant", ex.ParamName);
    }

    [Fact]
    public void Register_NullConfiguration_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            resolver.Register("tenant-x", null!));

        Assert.Equal("configuration", ex.ParamName);
    }

    [Fact]
    public void GetConfiguration_NullTenant_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            resolver.GetDatabaseContextConfiguration(null!));

        Assert.Equal("tenant", ex.ParamName);
    }

    [Fact]
    public void Register_WithOptions_ShouldStoreConfigurations()
    {
        var options = new MultiTenantOptions
        {
            Tenants = new List<TenantConfiguration>
            {
                new TenantConfiguration
                {
                    Name = "opts-a",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=OptA;",
                        ProviderName = "fake-a",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                },
                new TenantConfiguration
                {
                    Name = "opts-b",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=OptB;",
                        ProviderName = "fake-b",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        resolver.Register(options);

        Assert.Equal("Server=OptA;", resolver.GetDatabaseContextConfiguration("opts-a").ConnectionString);
        Assert.Equal("Server=OptB;", resolver.GetDatabaseContextConfiguration("opts-b").ConnectionString);
    }

    [Fact]
    public void Register_NullOptions_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() => resolver.Register((MultiTenantOptions)null!));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Clear_RemovesAllRegistrations()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-clear", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=Clear;",
            ProviderName = "fake-clear"
        });

        resolver.Clear();

        Assert.Throws<InvalidOperationException>(() => resolver.GetDatabaseContextConfiguration("tenant-clear"));
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
