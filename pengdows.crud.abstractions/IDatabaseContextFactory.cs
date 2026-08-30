using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;

namespace pengdows.crud;

/// <summary>
/// Creates <see cref="IDatabaseContext"/> instances for the requested tenant configuration.
/// </summary>
public interface IDatabaseContextFactory
{
    /// <summary>
    /// Builds a new database context for the provided configuration and provider factory.
    /// </summary>
    /// <param name="configuration">Tenant-scoped configuration.</param>
    /// <param name="factory">Provider factory that creates connections.</param>
    /// <param name="loggerFactory">Logger factory used by the context.</param>
    /// <returns>A fresh <see cref="IDatabaseContext"/>.</returns>
    IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
        ILoggerFactory loggerFactory);

    /// <summary>
    /// Asynchronously builds a new database context for the provided configuration and provider factory.
    /// </summary>
    /// <param name="configuration">Tenant-scoped configuration.</param>
    /// <param name="factory">Provider factory that creates connections.</param>
    /// <param name="loggerFactory">Logger factory used by the context.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task returning a fresh <see cref="IDatabaseContext"/>.</returns>
    Task<IDatabaseContext> CreateAsync(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Create(configuration, factory, loggerFactory));
    }
}