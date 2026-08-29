using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.strategies.connection;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class FrameworkInfrastructureEdgeCaseTests
{
    [Fact]
    public void EnumLiteralAttribute_StoresLiteral_AndRejectsNull()
    {
        var attr = new EnumLiteralAttribute("x");
        Assert.Equal("x", attr.Literal);
        Assert.Throws<ArgumentNullException>(() => new EnumLiteralAttribute(null!));
    }

    [Fact]
    public void ConnectionStrategyFactory_ThrowsForUnsupportedMode()
    {
        using var ctx = new DatabaseContext("Data Source=:memory:", new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.Throws<NotSupportedException>(() => ConnectionStrategyFactory.Create(ctx, (DbMode)999));
    }

    [Fact]
    public void ConnectionStringHelper_WhenPreferredBuilderApplyFails_UsesFallbackBuilder()
    {
        var throwingBuilder = new ThrowOnIndexerSetBuilder();

        var result = ConnectionStringHelper.Create(throwingBuilder, "Data Source=fallback.db");

        Assert.NotSame(throwingBuilder, result);
        Assert.Contains("Data Source", result.ConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntervalYearMonthConverter_CoversInvalidAndWhitespaceStringBranches()
    {
        var converter = new IntervalYearMonthConverter();

        var whitespaceParsed =
            converter.TryConvertFromProvider("   ", SupportedDatabase.PostgreSql, out var whitespaceResult);
        Assert.True(whitespaceParsed);
        Assert.Equal(0, whitespaceResult.Years);
        Assert.Equal(0, whitespaceResult.Months);

        var invalidParsed =
            converter.TryConvertFromProvider("P999999999999999999999Y", SupportedDatabase.PostgreSql, out _);
        Assert.False(invalidParsed);
    }

    [Fact]
    public void PostgreSqlRangeConverter_CoversFastPathUnsupportedAndEmptyFormattingBranches()
    {
        var converter = new PostgreSqlRangeConverter<int>();

        var sourceRange = new Range<int>(1, 3, true, false);
        Assert.True(converter.TryConvertFromProvider(sourceRange, SupportedDatabase.PostgreSql, out var rangeResult));
        Assert.Equal(sourceRange, rangeResult);

        Assert.False(converter.TryConvertFromProvider(new object(), SupportedDatabase.PostgreSql, out _));

        Assert.True(converter.TryConvertFromProvider("", SupportedDatabase.PostgreSql, out var emptyResult));
        Assert.Equal(Range<int>.Empty, emptyResult);

        var openLower = new Range<int>(null, 5, true, true);
        var formatted = converter.ToProviderValue(openLower, SupportedDatabase.PostgreSql);
        Assert.Equal("[,5]", formatted);
    }

    [Fact]
    public void HStore_CoversAdditionalEqualityEnumerationAndHashBranches()
    {
        var nonEmpty = new HStore(new Dictionary<string, string?> { ["a"] = "1" });
        var differentCount = new HStore(new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2" });

        Assert.False(default(HStore).Equals(nonEmpty));
        Assert.False(nonEmpty.Equals(default(HStore)));
        Assert.False(nonEmpty.Equals(differentCount));
        Assert.False(nonEmpty.Equals((object?)"not-hstore"));

        IEnumerable nonGeneric = nonEmpty;
        var enumerator = nonGeneric.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        Assert.True(nonEmpty == new HStore(new Dictionary<string, string?> { ["a"] = "1" }));
        Assert.NotEqual(0, nonEmpty.GetHashCode());
    }

    [Fact]
    public void HStore_UnescapeHStoreValue_EmptyString_ReturnsEmptyString()
    {
        var method = typeof(HStore).GetMethod("UnescapeHStoreValue", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, new object[] { string.Empty })!;
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DbProviderLoader_RegistersProviderUsingAssemblyResolvedFactory()
    {
        var assemblyName = typeof(LoaderFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:loader:ProviderName"] = "Coverage94.LoaderProvider",
                ["DatabaseProviders:loader:FactoryType"] = typeof(LoaderFactory).FullName,
                ["DatabaseProviders:loader:AssemblyName"] = assemblyName
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<DbProviderFactory>("loader");
        Assert.Same(LoaderFactory.Instance, resolved);
    }

    // CORE-006: DbProviderLoader registers its keyed DI service under the DatabaseProviders
    // configuration SECTION KEY (confirmed by the test immediately above — "loader", not the
    // ProviderName field "Coverage94.LoaderProvider"), while TenantContextRegistry resolves via
    // _serviceProvider.GetKeyedService<DbProviderFactory>(config.ProviderName) — the TENANT
    // configuration's ProviderName field. For these to connect, a tenant's ProviderName must
    // equal the DatabaseProviders section key, NOT the ADO.NET invariant name that same field's
    // own name suggests. A caller who (reasonably) sets tenant ProviderName to the invariant name
    // gets a confusing "No factory registered" error with no indication of the actual
    // requirement. This test locks in the current, intentional identity (section key wins) and
    // proves the error message is now actionable rather than a bare failure.
    [Fact]
    public void TenantContextRegistry_WhenTenantProviderNameIsInvariantNameNotSectionKey_ThrowsActionableError()
    {
        var assemblyName = typeof(LoaderFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:loader:ProviderName"] = "Coverage94.LoaderProvider",
                ["DatabaseProviders:loader:FactoryType"] = typeof(LoaderFactory).FullName,
                ["DatabaseProviders:loader:AssemblyName"] = assemblyName
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();
        loader.LoadAndRegisterProviders(services);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();

        // A tenant configured with ProviderName set to the ADO.NET invariant name — the natural
        // (but, for this loader-based DI resolution path, wrong) value to put there.
        var tenantConfig = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test",
            ProviderName = "Coverage94.LoaderProvider"
        };

        using var registry = new pengdows.crud.tenant.TenantContextRegistry(
            provider,
            new SingleTenantResolver(tenantConfig),
            new PassthroughContextFactory(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetContext("tenant-x"));

        Assert.Contains("Coverage94.LoaderProvider", ex.Message);
        Assert.Contains("DatabaseProviders", ex.Message);
        Assert.Contains("section key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SingleTenantResolver : pengdows.crud.tenant.ITenantConnectionResolver
    {
        private readonly IDatabaseContextConfiguration _config;

        public SingleTenantResolver(IDatabaseContextConfiguration config)
        {
            _config = config;
        }

        public IDatabaseContextConfiguration GetDatabaseContextConfiguration(string tenant) => _config;
    }

    private sealed class PassthroughContextFactory : IDatabaseContextFactory
    {
        public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
            Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
        {
            return new DatabaseContext(configuration, factory, loggerFactory);
        }
    }

    [Fact]
    public void ModeContentionSnapshot_ExposesAllConstructorValues()
    {
        var snapshot = new ModeContentionSnapshot(
            CurrentWaiters: 1,
            PeakWaiters: 2,
            TotalWaits: 3,
            TotalTimeouts: 4,
            TotalWaitTimeTicks: 5,
            AverageWaitTimeTicks: 6);

        Assert.Equal(1, snapshot.CurrentWaiters);
        Assert.Equal(2, snapshot.PeakWaiters);
        Assert.Equal(3, snapshot.TotalWaits);
        Assert.Equal(4, snapshot.TotalTimeouts);
        Assert.Equal(5, snapshot.TotalWaitTimeTicks);
        Assert.Equal(6, snapshot.AverageWaitTimeTicks);
    }

    [Fact]
    public void ParameterBindingRules_BooleanForMySql_UsesByte()
    {
        var parameter = new fakeDbParameter();

        var handled = ParameterBindingRules.ApplyBindingRules(
            parameter, typeof(bool), true, SupportedDatabase.MySql);

        Assert.True(handled);
        Assert.Equal((byte)1, parameter.Value);
        Assert.Equal(DbType.Byte, parameter.DbType);
    }

    [Fact]
    public void ParameterBindingRules_NullEnum_UsesDbNullAndStringType()
    {
        var parameter = new fakeDbParameter();

        var handled = ParameterBindingRules.ApplyBindingRules(
            parameter, typeof(TestEnum), null, SupportedDatabase.PostgreSql);

        Assert.True(handled);
        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    private sealed class ThrowOnIndexerSetBuilder : DbConnectionStringBuilder
    {
        [AllowNull]
        public override object this[string keyword]
        {
            get => base[keyword];
            set => throw new InvalidOperationException("set failed");
        }
    }

    private sealed class LoaderFactory : DbProviderFactory
    {
        public static LoaderFactory Instance { get; } = new();
    }

    // CORE-014: real ADO.NET providers commonly expose `Instance` as a public static READONLY
    // FIELD, not a property — e.g. System.Data.SqlClient.SqlClientFactory.Instance and
    // MySql.Data's MySqlClientFactory.Instance are both fields; Npgsql's NpgsqlFactory.Instance
    // is a property. DbProviderLoader.LoadProviderFactory only ever looked for a property, so a
    // perfectly valid field-based provider assembly would load successfully but then fail
    // factory discovery with a misleading "does not have a static Instance property" error.
    private sealed class FieldInstanceLoaderFactory : DbProviderFactory
    {
        public static readonly FieldInstanceLoaderFactory Instance = new();
    }

    [Fact]
    public void DbProviderLoader_RegistersProviderUsingFieldBasedInstanceConvention()
    {
        var assemblyName = typeof(FieldInstanceLoaderFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:fieldloader:ProviderName"] = "Coverage94.FieldLoaderProvider",
                ["DatabaseProviders:fieldloader:FactoryType"] = typeof(FieldInstanceLoaderFactory).FullName,
                ["DatabaseProviders:fieldloader:AssemblyName"] = assemblyName
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<DbProviderFactory>("fieldloader");
        Assert.Same(FieldInstanceLoaderFactory.Instance, resolved);
    }

    // TEST-011: partial multi-provider failure. LoadAndRegisterProviders has no try/catch inside
    // its foreach — a failure loading any one provider throws immediately and aborts the whole
    // call. This proves the concrete consequence of that for the CALLER's `services` collection:
    // providers configured BEFORE the failing one are already registered by the time the
    // exception propagates (services.AddKeyedSingleton already ran for them), while providers
    // configured AFTER it never get a chance to load at all. A caller who assumes "if this
    // throws, nothing got registered" would be wrong.
    [Fact]
    public void LoadAndRegisterProviders_OneProviderFails_RegistersProvidersBeforeItButNotAfter()
    {
        var assemblyName = typeof(LoaderFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:p1-before:ProviderName"] = "Coverage94.P1Provider",
                ["DatabaseProviders:p1-before:FactoryType"] = typeof(LoaderFactory).FullName,
                ["DatabaseProviders:p1-before:AssemblyName"] = assemblyName,

                // No AssemblyPath/AssemblyName/FactoryType at all — falls through to
                // DbProviderFactories.GetFactory(ProviderName), which throws for an invariant
                // name nothing has ever registered.
                ["DatabaseProviders:p2-fails:ProviderName"] = "Coverage94.NeverRegisteredProvider",

                ["DatabaseProviders:p3-after:ProviderName"] = "Coverage94.P3Provider",
                ["DatabaseProviders:p3-after:FactoryType"] = typeof(LoaderFactory).FullName,
                ["DatabaseProviders:p3-after:AssemblyName"] = assemblyName
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => loader.LoadAndRegisterProviders(services));

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetKeyedService<DbProviderFactory>("p1-before"));
        Assert.Null(provider.GetKeyedService<DbProviderFactory>("p3-after"));
    }

    // TEST-011: duplicate registration. Calling LoadAndRegisterProviders twice for the same
    // section key — e.g. AddDbProviderLoading invoked twice during composition by accident, or
    // two configuration sources both defining the same provider — must not throw or corrupt
    // resolution. AddKeyedSingleton allows multiple registrations for one key; DI resolves the
    // LAST one registered. This proves that behavior explicitly for this call site rather than
    // leaving it as an unverified assumption about generic DI semantics.
    [Fact]
    public void LoadAndRegisterProviders_CalledTwiceForSameKey_DoesNotThrow_LastRegistrationWins()
    {
        var assemblyName = typeof(LoaderFactory).Assembly.GetName().Name!;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProviders:dup:ProviderName"] = "Coverage94.DupProvider",
                ["DatabaseProviders:dup:FactoryType"] = typeof(LoaderFactory).FullName,
                ["DatabaseProviders:dup:AssemblyName"] = assemblyName
            })
            .Build();

        var loader = new DbProviderLoader(config, NullLogger<DbProviderLoader>.Instance);
        var services = new ServiceCollection();

        loader.LoadAndRegisterProviders(services);
        loader.LoadAndRegisterProviders(services);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<DbProviderFactory>("dup");
        Assert.Same(LoaderFactory.Instance, resolved);
    }

    private enum TestEnum
    {
        One = 1
    }
}
