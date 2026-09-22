using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace pengdows.stormgate;

internal sealed class DataSourceResolver
{
    // P2: ProbeCache is intentionally static even though DataSourceResolver is instance-scoped.
    // Provider DbDataSource support is determined by factory *type*, never by instance state or
    // the logger. The first probe for a given factory type wins for the process lifetime, which
    // is correct — provider behavior is fixed per type. The owning logger does not influence the
    // cache contents, only diagnostic output during the probe itself.
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
        {
            return null;
        }

        try
        {
            if (support.StringOverload is not null)
            {
                if (support.StringOverload.Invoke(factory, new object?[] { connectionString }) is DbDataSource ds)
                {
                    return ds;
                }
            }

            if (support.BuilderOverload is not null)
            {
                var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
                builder.ConnectionString = connectionString;

                if (support.BuilderOverload.Invoke(factory, new object?[] { builder }) is DbDataSource ds)
                {
                    return ds;
                }
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
        {
            return null;
        }

        return method.DeclaringType == typeof(DbProviderFactory) ? null : method;
    }

    private string SanitizeConnectionString(DbProviderFactory factory, string rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(rawConnectionString));
        }

        var builder = factory.CreateConnectionStringBuilder() ?? new DbConnectionStringBuilder();
        builder.ConnectionString = rawConnectionString;

        var sanitized = builder.ConnectionString;

        if (!string.Equals(rawConnectionString, sanitized, StringComparison.Ordinal))
        {
            // P1: Detect keys silently removed by the provider's builder. A stripped
            // Encrypt=True or SslMode=Required is a silent security regression that will
            // be invisible in connection logs. Parse the raw string through the generic
            // builder (which accepts all keys) to find what the provider builder dropped.
            var rawBuilder = new DbConnectionStringBuilder { ConnectionString = rawConnectionString };
            var sanitizedKeys = new HashSet<string>(
                builder.Keys.Cast<string>(),
                StringComparer.OrdinalIgnoreCase);

            // A key missing from sanitizedKeys by NAME isn't necessarily dropped — the provider's
            // builder may have canonicalized it to a different key name while preserving the
            // value (e.g. Npgsql's Server -> Host). Comparing by value across every sanitized key
            // catches that rename case; only a key whose value doesn't survive under ANY
            // sanitized key is a genuine drop worth warning about.
            var sanitizedValues = builder.Keys.Cast<string>()
                .Select(k => builder[k]?.ToString())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var removedKeys = rawBuilder.Keys.Cast<string>()
                .Where(k => !sanitizedKeys.Contains(k))
                .Where(k =>
                {
                    var rawValue = rawBuilder[k]?.ToString();
                    return string.IsNullOrEmpty(rawValue) || !sanitizedValues.Contains(rawValue);
                })
                .ToList();

            if (removedKeys.Count > 0)
            {
                _logger.LogWarning(
                    "Connection string normalization by {BuilderType} silently removed keys: [{RemovedKeys}]. " +
                    "Verify these settings are intentionally excluded (e.g. Encrypt, SslMode).",
                    builder.GetType().FullName,
                    string.Join(", ", removedKeys));
            }
            else
            {
                _logger.LogDebug(
                    "Connection string was normalized by {BuilderType}.",
                    builder.GetType().FullName);
            }
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
