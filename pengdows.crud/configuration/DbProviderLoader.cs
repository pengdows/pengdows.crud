#region

using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace pengdows.crud.configuration;

public class DbProviderLoader : IDbProviderLoader
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, Assembly> _loadedAssemblies = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbProviderLoader> _logger;

    public DbProviderLoader(IConfiguration configuration, ILogger<DbProviderLoader> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void LoadAndRegisterProviders(IServiceCollection services)
    {
        var providers = new Dictionary<string, DatabaseProviderConfig>();
        _configuration.GetSection("DatabaseProviders").Bind(providers);

        foreach (var kvp in providers)
        {
            var providerKey = kvp.Key;

            if (string.IsNullOrEmpty(kvp.Value.ProviderName))
                throw new InvalidOperationException($"ProviderName is missing for provider '{providerKey}'.");

            _logger.LogInformation("Loading DbProviderFactory for provider '{ProviderKey}'", providerKey);

            var factory = LoadProviderFactory(providerKey, kvp.Value);

            // Register with DI container
            services.AddKeyedSingleton<DbProviderFactory>(providerKey, factory);

            // Register with DbProviderFactories for legacy compatibility
            try
            {
                DbProviderFactories.RegisterFactory(kvp.Value.ProviderName, factory);
                _logger.LogInformation("Registered provider '{ProviderKey}' with DbProviderFactories", providerKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register provider '{ProviderKey}' with DbProviderFactories",
                    providerKey);
                throw new InvalidOperationException(
                    $"Failed to register DbProviderFactory for provider '{kvp.Key}' with provider name  '{kvp.Value.ProviderName}'.",
                    ex
                );
            }
        }
    }

    private DbProviderFactory LoadProviderFactory(string providerKey, DatabaseProviderConfig config)
    {
        // Step 1: Load assembly if specified
        Assembly providerAssembly = null;
        if (!string.IsNullOrEmpty(config.AssemblyPath))
        {
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.AssemblyPath);
            if (!File.Exists(fullPath))
            {
                _logger.LogError("Assembly file '{FullPath}' for provider '{ProviderKey}' does not exist", fullPath,
                    providerKey);
                throw new InvalidOperationException(
                    $"Assembly file '{fullPath}' for provider '{providerKey}' does not exist."
                );
            }

            lock (_lock)
            {
                if (!_loadedAssemblies.TryGetValue(fullPath, out providerAssembly))
                    try
                    {
                        providerAssembly = Assembly.LoadFrom(fullPath);
                        _loadedAssemblies[fullPath] = providerAssembly;
                        _logger.LogInformation("Loaded assembly from '{FullPath}' for provider '{ProviderKey}'",
                            fullPath, providerKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load assembly '{FullPath}' for provider '{ProviderKey}'",
                            fullPath, providerKey);
                        throw new InvalidOperationException(
                            $"Failed to load assembly '{fullPath}' for provider '{providerKey}'.",
                            ex
                        );
                    }
            }
        }
        else if (!string.IsNullOrEmpty(config.AssemblyName))
        {
            lock (_lock)
            {
                if (!_loadedAssemblies.TryGetValue(config.AssemblyName, out providerAssembly))
                    try
                    {
                        providerAssembly = Assembly.Load(config.AssemblyName);
                        _loadedAssemblies[config.AssemblyName] = providerAssembly;
                        _logger.LogInformation("Loaded assembly '{AssemblyName}' for provider '{ProviderKey}'",
                            config.AssemblyName, providerKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load assembly '{AssemblyName}' for provider '{ProviderKey}'",
                            config.AssemblyName, providerKey);
                        throw new InvalidOperationException(
                            $"Failed to load assembly '{config.AssemblyName}' for provider '{providerKey}'.",
                            ex
                        );
                    }
            }
        }

        // Step 2: Resolve DbProviderFactory
        if (!string.IsNullOrEmpty(config.FactoryType) && providerAssembly != null)
        {
            var type = providerAssembly.GetType(config.FactoryType);
            if (type == null)
            {
                _logger.LogError(
                    "Could not find DbProviderFactory type '{FactoryTypeName}' in assembly for provider '{ProviderKey}'",
                    config.FactoryType, providerKey
                );
                throw new InvalidOperationException(
                    $"Could not find DbProviderFactory type '{config.FactoryType}' in assembly for provider '{providerKey}'."
                );
            }

            var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty == null)
            {
                _logger.LogError(
                    "DbProviderFactory type '{FactoryTypeName}' for provider '{ProviderKey}' does not have a static  Instance property",
                    config.FactoryType, providerKey
                );
                throw new InvalidOperationException(
                    $"DbProviderFactory type '{config.FactoryType}' for provider '{providerKey}' does not have a static  Instance property."
                );
            }

            var factory = instanceProperty.GetValue(null) as DbProviderFactory;
            if (factory == null)
            {
                _logger.LogError(
                    "Failed to get DbProviderFactory instance from type '{FactoryTypeName}' for provider '{ProviderKey}'",
                    config.FactoryType, providerKey
                );
                throw new InvalidOperationException(
                    $"Failed to get DbProviderFactory instance from type '{config.FactoryType}' for provider  '{providerKey}'."
                );
            }

            return factory;
        }

        // Step 3: Fall back to DbProviderFactories for compiled-in providers
        try
        {
            var factory = DbProviderFactories.GetFactory(config.ProviderName);
            _logger.LogInformation(
                "Loaded DbProviderFactory for provider '{ProviderKey}' using provider name '{ProviderName}'",
                providerKey, config.ProviderName
            );
            return factory;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load DbProviderFactory for provider '{ProviderKey}' with provider name '{ProviderName}'",
                providerKey, config.ProviderName
            );
            throw new InvalidOperationException(
                $"Failed to load DbProviderFactory for provider '{providerKey}' with provider name '{config.ProviderName}'.  Ensure the provider is registered or the assembly is correctly specified.",
                ex
            );
        }
    }
}