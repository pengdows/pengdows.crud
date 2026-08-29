using System;
using System.Collections.Generic;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
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
                ProviderName = "Microsoft.Data.Sqlite",
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
                ProviderName = "Microsoft.Data.Sqlite",
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

        // Assert — values equal; stored config is a clone so references differ.
        Assert.Equal(tenantA.DatabaseContextConfiguration.ConnectionString, resultA.ConnectionString);
        Assert.Equal(tenantA.DatabaseContextConfiguration.ProviderName, resultA.ProviderName);
        Assert.Equal(tenantB.DatabaseContextConfiguration.ConnectionString, resultB.ConnectionString);
        Assert.Equal(tenantB.DatabaseContextConfiguration.ProviderName, resultB.ProviderName);
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
    public void Register_And_GetConfiguration_Should_ReturnEquivalentConfig()
    {
        // Arrange
        var tenantId = "tenant-a";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "Microsoft.Data.Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        // Act
        var resolver = new TenantConnectionResolver();
        resolver.Register(tenantId, config);
        var result = resolver.GetDatabaseContextConfiguration(tenantId);

        // Assert — stored config is a clone so references differ; values must match.
        Assert.Equal(config.ConnectionString, result.ConnectionString);
        Assert.Equal(config.ProviderName, result.ProviderName);
        Assert.Equal(config.DbMode, result.DbMode);
        Assert.Equal(config.ReadWriteMode, result.ReadWriteMode);
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
    public void Register_InvalidTenant_ShouldThrow(string? invalidTenant)
    {
        var config = new DatabaseContextConfiguration();

        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() =>
            resolver.Register(invalidTenant!, config));

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
    public void Register_MissingProviderName_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "   "
        };

        var ex = Assert.Throws<ArgumentException>(() => resolver.Register("tenant-x", config));

        Assert.Equal("configuration", ex.ParamName);
        Assert.Contains("ProviderName", ex.Message);
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
                new()
                {
                    Name = "opts-a",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=OptA;",
                        ProviderName = "Microsoft.Data.Sqlite",
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
                        ProviderName = "Microsoft.Data.Sqlite",
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
    public void Register_WithOptions_ComposesApplicationNameWhenMissing()
    {
        var options = new MultiTenantOptions
        {
            ApplicationName = "core-app",
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = "east",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=East;",
                        ProviderName = "Microsoft.Data.Sqlite",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        resolver.Register(options);

        var config = resolver.GetDatabaseContextConfiguration("east");
        Assert.Equal("core-app:east", config.ApplicationName);
    }

    [Fact]
    public void Register_WithOptions_DoesNotOverrideExplicitApplicationName()
    {
        var options = new MultiTenantOptions
        {
            ApplicationName = "core-app",
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = "west",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ApplicationName = "override-app",
                        ConnectionString = "Server=West;",
                        ProviderName = "Microsoft.Data.Sqlite",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        resolver.Register(options);

        var config = resolver.GetDatabaseContextConfiguration("west");
        Assert.Equal("override-app", config.ApplicationName);
    }

    [Fact]
    public void Register_WithOptions_EmptyBaseApp_DoesNotCompose()
    {
        var options = new MultiTenantOptions
        {
            ApplicationName = "  ",
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = "north",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=North;",
                        ProviderName = "Microsoft.Data.Sqlite",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        resolver.Register(options);

        var config = resolver.GetDatabaseContextConfiguration("north");
        Assert.Equal(string.Empty, config.ApplicationName);
    }

    [Fact]
    public void Register_WithOptions_EmptyBaseApp_MissingTenantNameThrows()
    {
        var options = new MultiTenantOptions
        {
            ApplicationName = string.Empty,
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = "",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=Bad;",
                        ProviderName = "Microsoft.Data.Sqlite",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() => resolver.Register(options));

        Assert.Equal("tenant", ex.ParamName);
    }

    [Fact]
    public void Register_WithOptions_MissingTenantName_Throws()
    {
        var options = new MultiTenantOptions
        {
            ApplicationName = "core-app",
            Tenants = new List<TenantConfiguration>
            {
                new()
                {
                    Name = " ",
                    DatabaseContextConfiguration = new DatabaseContextConfiguration
                    {
                        ConnectionString = "Server=Bad;",
                        ProviderName = "Microsoft.Data.Sqlite",
                        DbMode = DbMode.Standard,
                        ReadWriteMode = ReadWriteMode.ReadWrite
                    }
                }
            }
        };

        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentException>(() => resolver.Register(options));

        Assert.Contains("non-empty Name", ex.Message);
    }

    [Fact]
    public void Register_NullOptions_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() => resolver.Register((MultiTenantOptions)null!));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Register_NullTenantEnumerable_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var ex = Assert.Throws<ArgumentNullException>(() => resolver.Register((IEnumerable<TenantConfiguration>)null!));

        Assert.Equal("tenants", ex.ParamName);
    }

    [Fact]
    public void Register_Enumerable_WithMissingProviderName_ShouldThrow()
    {
        var resolver = new TenantConnectionResolver();
        var tenants = new[]
        {
            new TenantConfiguration
            {
                Name = "bad-provider",
                DatabaseContextConfiguration = new DatabaseContextConfiguration
                {
                    ConnectionString = "Server=A;",
                    ProviderName = ""
                }
            }
        };

        var ex = Assert.Throws<ArgumentException>(() => resolver.Register(tenants));
        Assert.Contains("ProviderName", ex.Message);
    }

    [Fact]
    public void Clear_RemovesAllRegistrations()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-clear", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=Clear;",
            ProviderName = "Microsoft.Data.Sqlite"
        });

        resolver.Clear();

        Assert.Throws<InvalidOperationException>(() => resolver.GetDatabaseContextConfiguration("tenant-clear"));
    }

    [Fact]
    public void Register_MutatingConfigAfterRegistration_DoesNotAffectStoredConfig()
    {
        var resolver = new TenantConnectionResolver();
        var original = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=original;",
            ProviderName = "FakeProvider"
        };

        resolver.Register("tenant1", original);

        // Mutate the object the caller still holds.
        original.ConnectionString = "Server=mutated;";

        var stored = resolver.GetDatabaseContextConfiguration("tenant1");

        // The stored config must reflect the value at registration time, not the mutation.
        Assert.Equal("Server=original;", stored.ConnectionString);
    }

    [Fact]
    public void Register_And_GetConfiguration_PreservesEveryConfigurationProperty()
    {
        // CORE-001: CloneConfiguration previously omitted ReaderPlanCacheSize,
        // SessionInitializationFailureMode, MaxQueuedWrites, MaxQueuedReads, and
        // EnforceUniqueConnectionString, so tenant configurations silently reverted those
        // values to their defaults on registration. This test is reflection-based over the
        // full IDatabaseContextConfiguration contract specifically so a future property added
        // to the interface without a matching clone-field fails this test immediately, instead
        // of silently reproducing the same bug.
        var metricsOptions = new MetricsOptions
        {
            LongConnectionThreshold = TimeSpan.FromSeconds(17),
            EnableApproxPercentiles = true,
            PercentileWindowSize = 4096
        };

        var source = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=Primary;",
            ReadOnlyConnectionString = "Server=Replica;",
            ProviderName = "Fake.Provider",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadOnly,
            PrepareMode = CommandPrepareMode.Always,
            ReaderPlanCacheSize = 42,
            EnableMetrics = true,
            MetricsOptions = metricsOptions,
            MaxConcurrentWrites = 7,
            MaxConcurrentReads = 9,
            PoolAcquireTimeout = TimeSpan.FromSeconds(11),
            ModeLockTimeout = TimeSpan.FromSeconds(61),
            ApplicationName = "app-under-test",
            EnableSingleWriterFairness = false,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed,
            MaxQueuedWrites = 3,
            MaxQueuedReads = 4,
            EnforceUniqueConnectionString = true
        };

        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-full-config", source);
        var cloned = resolver.GetDatabaseContextConfiguration("tenant-full-config");

        foreach (var property in typeof(IDatabaseContextConfiguration).GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            var expected = property.GetValue(source);
            var actual = property.GetValue(cloned);
            Assert.True(Equals(expected, actual),
                $"Property '{property.Name}' was not preserved by tenant registration cloning. " +
                $"Expected '{expected}', got '{actual}'.");
        }
    }

    [Fact]
    public void Register_And_GetConfiguration_PreservesReaderPlanCacheSize()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-cache-size", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "Fake.Provider",
            ReaderPlanCacheSize = 128
        });

        var config = resolver.GetDatabaseContextConfiguration("tenant-cache-size");

        Assert.Equal(128, config.ReaderPlanCacheSize);
    }

    [Fact]
    public void Register_And_GetConfiguration_PreservesQueueCaps()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-queue-caps", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "Fake.Provider",
            MaxQueuedReads = 5,
            MaxQueuedWrites = 2
        });

        var config = resolver.GetDatabaseContextConfiguration("tenant-queue-caps");

        Assert.Equal(5, config.MaxQueuedReads);
        Assert.Equal(2, config.MaxQueuedWrites);
    }

    [Fact]
    public void Register_And_GetConfiguration_PreservesFailClosedSessionInitialization()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-fail-closed", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "Fake.Provider",
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed
        });

        var config = resolver.GetDatabaseContextConfiguration("tenant-fail-closed");

        Assert.Equal(SessionInitializationFailureMode.FailClosed, config.SessionInitializationFailureMode);
    }

    [Fact]
    public void Register_And_GetConfiguration_PreservesEnforceUniqueConnectionString()
    {
        var resolver = new TenantConnectionResolver();
        resolver.Register("tenant-enforce-unique", new DatabaseContextConfiguration
        {
            ConnectionString = "Server=A;",
            ProviderName = "Fake.Provider",
            EnforceUniqueConnectionString = true
        });

        var config = resolver.GetDatabaseContextConfiguration("tenant-enforce-unique");

        Assert.True(config.EnforceUniqueConnectionString);
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