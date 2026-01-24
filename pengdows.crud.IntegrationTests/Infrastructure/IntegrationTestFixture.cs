using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud.enums;
using testbed;

namespace pengdows.crud.IntegrationTests.Infrastructure;

internal static class IntegrationTestConfiguration
{
    public static IReadOnlyList<SupportedDatabase> BaseProviders { get; } = new[]
    {
        SupportedDatabase.Sqlite,
        SupportedDatabase.PostgreSql,
        SupportedDatabase.SqlServer,
        SupportedDatabase.MySql,
        SupportedDatabase.MariaDb,
        SupportedDatabase.Firebird,
        SupportedDatabase.CockroachDb,
        SupportedDatabase.DuckDB
    };

    public static IReadOnlyList<SupportedDatabase> EnabledProviders =>
        ShouldIncludeOracle
            ? BaseProviders.Concat(new[] { SupportedDatabase.Oracle }).ToArray()
            : BaseProviders;

    public static bool ShouldIncludeOracle =>
        string.Equals(Environment.GetEnvironmentVariable("INCLUDE_ORACLE"), "true", StringComparison.OrdinalIgnoreCase);
}

public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly IHost _host;
    private readonly Dictionary<SupportedDatabase, ITestContainer> _containers = new();
    private readonly Dictionary<SupportedDatabase, IDatabaseContext> _contexts = new();
    private readonly object _contextLock = new();

    public IntegrationTestFixture()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        _host = builder.Build();
    }

    public IServiceProvider Services => _host.Services;

    private IReadOnlyDictionary<SupportedDatabase, ITestContainer> Containers => _containers;

    public async Task InitializeAsync()
    {
        await _host.StartAsync();

        var orchestrator = new ParallelTestOrchestrator(_host.Services, IntegrationTestConfiguration.ShouldIncludeOracle);

        foreach (var provider in IntegrationTestConfiguration.EnabledProviders)
        {
            try
            {
                var container = await orchestrator.CreateContainerAsync(provider);

                if (container is not null)
                {
                    _containers[provider] = container;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to initialize {provider} container: {ex.Message}");
            }
        }

        if (_containers.Count == 0)
        {
            throw new InvalidOperationException("No database providers could be initialized for integration tests.");
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts.Values)
        {
            context.Dispose();
        }

        _contexts.Clear();

        foreach (var container in _containers.Values)
        {
            await container.DisposeAsync();
        }

        await _host.StopAsync();
        _host.Dispose();
    }

    public Task<IDatabaseContext> CreateDatabaseContextAsync(SupportedDatabase provider)
    {
        if (!_containers.TryGetValue(provider, out var container))
        {
            throw new InvalidOperationException($"Provider {provider} is not available for testing.");
        }

        lock (_contextLock)
        {
            if (_contexts.TryGetValue(provider, out var existing))
            {
                return Task.FromResult(existing);
            }
        }

        return CreateAndCacheContextAsync(provider, container);
    }

    public Task<IDatabaseContext> CreateAdditionalContextAsync(SupportedDatabase provider)
    {
        if (!_containers.TryGetValue(provider, out var container))
        {
            throw new InvalidOperationException($"Provider {provider} is not available for testing.");
        }

        return container.GetDatabaseContextAsync(_host.Services);
    }

    private async Task<IDatabaseContext> CreateAndCacheContextAsync(SupportedDatabase provider, ITestContainer container)
    {
        var context = await container.GetDatabaseContextAsync(_host.Services);
        lock (_contextLock)
        {
            if (_contexts.TryGetValue(provider, out var existing))
            {
                context.Dispose();
                return existing;
            }

            _contexts[provider] = context;
        }

        return context;
    }
}
