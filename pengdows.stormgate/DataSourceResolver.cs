using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace pengdows.stormgate;

internal sealed class DataSourceResolver
{
    private static readonly ConcurrentDictionary<Type, ProviderCreateDataSourceSupport> ProbeCache = new();

    private readonly ILogger _logger;

    public DataSourceResolver(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public DbDataSource CreateDataSource(DbProviderFactory factory, string rawConnectionString)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var connectionString = SanitizeConnectionString(factory, rawConnectionString);

        var native = TryCreateProviderDataSource(factory, connectionString);
        if (native is not null)
        {
            _logger.LogDebug(
                "Using native DbDataSource from provider factory {FactoryType}.",
                factory.GetType().FullName);

            return native;
        }

        _logger.LogDebug(
            "Using GenericDbDataSource fallback for provider factory {FactoryType}.",
            factory.GetType().FullName);

        return new GenericDbDataSource(factory, connectionString);
    }

    private DbDataSource? TryCreateProviderDataSource(
        DbProviderFactory factory,
        string connectionString)
    {
        var factoryType = factory.GetType();
        var support = ProbeCache.GetOrAdd(factoryType, ProbeProviderCreateDataSourceSupport);

        if (!support.HasAnySupportedOverload)
            return null;

        try
        {
            if (support.StringOverload is not null)
            {
                if (support.StringOverload.Invoke(factory, new object?[] { connectionString }) is DbDataSource ds)
                    return ds;
            }

            if (support.BuilderOverload is not null)
            {
                var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
                builder.ConnectionString = connectionString;

                if (support.BuilderOverload.Invoke(factory, new object?[] { builder }) is DbDataSource ds)
                    return ds;
            }

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException)
        {
            _logger.LogDebug(
                "Provider {FactoryType} explicitly does not support DbDataSource.",
                factoryType.FullName);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed probing DbDataSource support for provider factory {FactoryType}.",
                factoryType.FullName);

            return null;
        }
    }

    private static ProviderCreateDataSourceSupport ProbeProviderCreateDataSourceSupport(Type factoryType)
    {
        var stringOverload = FindProviderCreateDataSourceMethod(factoryType, typeof(string));
        var builderOverload = FindProviderCreateDataSourceMethod(factoryType, typeof(DbConnectionStringBuilder));

        return new ProviderCreateDataSourceSupport(stringOverload, builderOverload);
    }

    private static MethodInfo? FindProviderCreateDataSourceMethod(Type factoryType, Type parameterType)
    {
        var method = factoryType.GetMethod(
            "CreateDataSource",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { parameterType },
            modifiers: null);

        if (method is null)
            return null;

        return method.DeclaringType == typeof(DbProviderFactory) ? null : method;
    }

    private string SanitizeConnectionString(DbProviderFactory factory, string rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(rawConnectionString));

        var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
        builder.ConnectionString = rawConnectionString;

        var sanitized = builder.ConnectionString;

        // Note: DbConnectionStringBuilder normalization can sometimes reorder keys or strip 
        // provider-specific attributes in edge cases. This is a trade-off for consistent 
        // pooling behavior.
        if (!string.Equals(rawConnectionString, sanitized, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Connection string was normalized by {BuilderType}.",
                builder.GetType().FullName);
        }

        return sanitized;
    }

    private sealed record ProviderCreateDataSourceSupport(
        MethodInfo? StringOverload,
        MethodInfo? BuilderOverload)
    {
        public bool HasAnySupportedOverload => StringOverload is not null || BuilderOverload is not null;
    }
}
