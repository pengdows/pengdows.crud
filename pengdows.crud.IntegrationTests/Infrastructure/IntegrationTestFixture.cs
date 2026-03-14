using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
        FilterIntegrationOnly(
            GetEnabledProviders(ShouldIncludeOracle, ShouldIncludeSnowflake),
            Environment.GetEnvironmentVariable("INTEGRATION_ONLY"));

    public static bool ShouldIncludeOracle =>
        string.Equals(Environment.GetEnvironmentVariable("INCLUDE_ORACLE"), "true",
            StringComparison.OrdinalIgnoreCase);

    public static bool ShouldIncludeSnowflake =>
        string.Equals(Environment.GetEnvironmentVariable("INCLUDE_SNOWFLAKE"), "true",
            StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<SupportedDatabase> GetEnabledProviders(bool includeOracle, bool includeSnowflake)
    {
        var providers = BaseProviders.ToList();

        if (includeOracle)
        {
            providers.Add(SupportedDatabase.Oracle);
        }

        if (includeSnowflake)
        {
            providers.Add(SupportedDatabase.Snowflake);
        }

        return providers;
    }

    internal static IReadOnlyList<SupportedDatabase> FilterIntegrationOnly(
        IReadOnlyList<SupportedDatabase> providers,
        string? only)
    {
        if (string.IsNullOrWhiteSpace(only))
        {
            return providers;
        }

        var filtered = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => Enum.TryParse<SupportedDatabase>(token, true, out var parsed)
                ? parsed
                : (SupportedDatabase?)null)
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value)
            .ToList();

        if (filtered.Count == 0)
        {
            throw new InvalidOperationException(
                $"INTEGRATION_ONLY did not match any SupportedDatabase values: '{only}'.");
        }

        var matched = providers.Where(filtered.Contains).ToArray();
        if (matched.Length == 0)
        {
            throw new InvalidOperationException(
                $"INTEGRATION_ONLY did not match any SupportedDatabase values: '{only}'.");
        }

        return matched;
    }
}

public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly IHost _host;
    private readonly Dictionary<SupportedDatabase, ITestContainer> _containers = new();
    private readonly Dictionary<SupportedDatabase, IDatabaseContext> _contexts = new();
    private readonly Dictionary<SupportedDatabase, string> _startupFailures = new();
    private readonly object _contextLock = new();

    public IntegrationTestFixture()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IAuditValueResolver, StringAuditContextProvider>();
        _host = builder.Build();
    }

    public IServiceProvider Services => _host.Services;

    private IReadOnlyDictionary<SupportedDatabase, ITestContainer> Containers => _containers;

    public async Task InitializeAsync()
    {
        await _host.StartAsync();

        var orchestrator = new ParallelTestOrchestrator(
            _host.Services,
            IntegrationTestConfiguration.ShouldIncludeOracle,
            IntegrationTestConfiguration.ShouldIncludeSnowflake);

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
                _startupFailures[provider] = ex.ToString();
                Console.WriteLine($"Warning: Failed to initialize {provider} container: {ex.Message}");
            }
        }

        if (_containers.Count == 0)
        {
            Console.WriteLine(
                "Warning: No database providers could be initialized. All integration tests will be skipped.");
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
            if (_startupFailures.TryGetValue(provider, out var reason))
            {
                throw new InvalidOperationException(
                    $"Provider {provider} container failed to start during fixture initialization: {reason}");
            }

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

    private async Task<IDatabaseContext> CreateAndCacheContextAsync(SupportedDatabase provider,
        ITestContainer container)
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
