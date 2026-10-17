using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// End to end, with real SQLite and DuckDB engines and no container:
/// - drivers are loaded from configuration only (DatabaseProviders section, assembly by name);
/// - each tenant is its own database and reports its own detected product;
/// - the registry hands out exactly one governed context per tenant under concurrency;
/// - a lease keeps a context alive across Invalidate; MaxTenantCount caps distinct tenants;
/// - tenants are isolated from each other.
/// Self-contained: needs no IntegrationTestFixture.
/// </summary>
public sealed class DynamicLoadingAndTenantTests : IAsyncLifetime
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pengdows-tenants-").FullName;
    private ServiceProvider? _provider;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_provider != null)
        {
            await _provider.DisposeAsync();
        }

        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // Best effort: a driver may still hold a file handle briefly.
        }
    }

    private static readonly (string Name, string Provider, string File)[] TwoEngines =
    {
        ("sq-a", "Microsoft.Data.Sqlite", "a.sqlite"),
        ("sq-b", "Microsoft.Data.Sqlite", "b.sqlite"),
        ("dk-a", "DuckDB.NET.Data", "a.duckdb")
    };

    private ITenantContextRegistry Build(int? maxTenants, params (string Name, string Provider, string File)[] tenants)
    {
        var values = new Dictionary<string, string?>
        {
            ["DatabaseProviders:sqlite:ProviderName"] = "Microsoft.Data.Sqlite",
            ["DatabaseProviders:sqlite:FactoryType"] = "Microsoft.Data.Sqlite.SqliteFactory",
            ["DatabaseProviders:sqlite:AssemblyName"] = "Microsoft.Data.Sqlite",
            ["DatabaseProviders:duckdb:ProviderName"] = "DuckDB.NET.Data",
            ["DatabaseProviders:duckdb:FactoryType"] = "DuckDB.NET.Data.DuckDBClientFactory",
            ["DatabaseProviders:duckdb:AssemblyName"] = "DuckDB.NET.Data",
            ["MultiTenant:ApplicationName"] = "mt-test"
        };

        if (maxTenants.HasValue)
        {
            values["MultiTenant:MaxTenantCount"] = maxTenants.Value.ToString();
        }

        for (var i = 0; i < tenants.Length; i++)
        {
            var (name, provider, file) = tenants[i];
            values[$"MultiTenant:Tenants:{i}:Name"] = name;
            values[$"MultiTenant:Tenants:{i}:DatabaseContextConfiguration:ProviderName"] = provider;
            values[$"MultiTenant:Tenants:{i}:DatabaseContextConfiguration:ConnectionString"] =
                $"Data Source={Path.Combine(_dir, file)}";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbProviderLoading(configuration);
        services.AddMultiTenancy(configuration);
        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<ITenantContextRegistry>();
    }

    private static async Task<long> ScalarAsync(IDatabaseContext ctx, string sql)
    {
        await using var sc = ctx.CreateSqlContainer(sql);
        return Convert.ToInt64(await sc.ExecuteScalarRequiredAsync<long>());
    }

    private static async Task ExecAsync(IDatabaseContext ctx, string sql)
    {
        await using var sc = ctx.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DriversLoadedFromConfigOnly_EachTenantDetectsItsOwnEngine()
    {
        var registry = Build(null, TwoEngines);

        var sqA = await registry.GetContextAsync("sq-a");
        var sqB = await registry.GetContextAsync("sq-b");
        var dkA = await registry.GetContextAsync("dk-a");

        Assert.Equal(SupportedDatabase.Sqlite, sqA.Product);
        Assert.Equal(SupportedDatabase.Sqlite, sqB.Product);
        Assert.Equal(SupportedDatabase.DuckDB, dkA.Product);
        Assert.NotSame(sqA, sqB);
    }

    [Fact]
    public async Task Tenants_AreIsolatedFromEachOther_AcrossEngines()
    {
        var registry = Build(null, TwoEngines);
        var names = new[] { "sq-a", "sq-b", "dk-a" };

        foreach (var tenant in names)
        {
            var ctx = await registry.GetContextAsync(tenant);
            await ExecAsync(ctx, "CREATE TABLE t (id BIGINT NOT NULL PRIMARY KEY, n BIGINT NOT NULL)");
            await ExecAsync(ctx, $"INSERT INTO t (id, n) VALUES (1, {Array.IndexOf(names, tenant) + 100})");
        }

        foreach (var tenant in names)
        {
            var ctx = await registry.GetContextAsync(tenant);
            Assert.Equal(1L, await ScalarAsync(ctx, "SELECT COUNT(*) FROM t"));
            Assert.Equal(Array.IndexOf(names, tenant) + 100, await ScalarAsync(ctx, "SELECT n FROM t WHERE id = 1"));
        }
    }

    [Fact]
    public async Task ConcurrentCallers_GetExactlyOneContextPerTenant()
    {
        var registry = Build(null, TwoEngines);
        var created = new ConcurrentBag<IDatabaseContext>();
        ((TenantContextRegistry)registry).ContextCreated += created.Add;
        var tenants = new[] { "sq-a", "sq-b", "dk-a" };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 96).Select(async i =>
        {
            var tenant = tenants[i % tenants.Length];
            await start.Task;
            return (Tenant: tenant, Context: await registry.GetContextAsync(tenant));
        }).ToList();

        start.SetResult();
        var results = await Task.WhenAll(tasks);

        foreach (var group in results.GroupBy(r => r.Tenant))
        {
            Assert.Single(group.Select(r => r.Context).Distinct());
        }

        Assert.Equal(tenants.Length, created.Count);
    }

    [Fact]
    public async Task Lease_KeepsContextUsable_AcrossInvalidate()
    {
        var registry = Build(null, TwoEngines);
        var lease = await registry.AcquireLeaseAsync("sq-a");
        var before = lease.Context;

        registry.Invalidate("sq-a");

        Assert.Equal(1L, await ScalarAsync(before, "SELECT 1"));

        lease.Dispose();

        var after = await registry.GetContextAsync("sq-a");
        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task MaxTenantCount_CapsDistinctTenants_AndInvalidateFreesASlot()
    {
        var registry = Build(2, TwoEngines);

        await registry.GetContextAsync("sq-a");
        await registry.GetContextAsync("sq-b");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await registry.GetContextAsync("dk-a"));

        registry.Invalidate("sq-b");
        var third = await registry.GetContextAsync("dk-a");
        Assert.Equal(SupportedDatabase.DuckDB, third.Product);
    }

    [Fact]
    public async Task UnknownTenant_IsRejected()
    {
        var registry = Build(null, TwoEngines);

        await Assert.ThrowsAnyAsync<Exception>(async () => await registry.GetContextAsync("nope"));
    }

    [Fact]
    public async Task DriverLoadedByFilePath_ResolvesAndDetects()
    {
        var values = new Dictionary<string, string?>
        {
            ["DatabaseProviders:sqlite:ProviderName"] = "Microsoft.Data.Sqlite",
            ["DatabaseProviders:sqlite:FactoryType"] = "Microsoft.Data.Sqlite.SqliteFactory",
            ["DatabaseProviders:sqlite:AssemblyPath"] = "Microsoft.Data.Sqlite.dll",
            ["MultiTenant:ApplicationName"] = "mt-path",
            ["MultiTenant:Tenants:0:Name"] = "p",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] = "Microsoft.Data.Sqlite",
            ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ConnectionString"] =
                $"Data Source={Path.Combine(_dir, "p.sqlite")}"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbProviderLoading(configuration);
        services.AddMultiTenancy(configuration);
        _provider = services.BuildServiceProvider();

        var ctx = await _provider.GetRequiredService<ITenantContextRegistry>().GetContextAsync("p");

        Assert.Equal(SupportedDatabase.Sqlite, ctx.Product);
    }

    [Fact]
    public async Task DriverConfiguredWithMissingFile_FailsLoudly()
    {
        var values = new Dictionary<string, string?>
        {
            ["DatabaseProviders:x:ProviderName"] = "Missing.Provider",
            ["DatabaseProviders:x:FactoryType"] = "Missing.Factory",
            ["DatabaseProviders:x:AssemblyPath"] = "does-not-exist.dll"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddDbProviderLoading(configuration));

        Assert.Contains("does not exist", ex.Message);
        await Task.CompletedTask;
    }
}
