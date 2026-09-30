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
    /// Asynchronously builds a new database context for the provided configuration and provider
    /// factory, without blocking the calling thread on connection opening and dialect detection.
    /// </summary>
    /// <remarks>
    /// The default implementation calls <see cref="Create"/> (blocking), which keeps factories
    /// written against 2.0.5 compatible; the built-in factory overrides it with a genuinely
    /// asynchronous construction path (<c>DatabaseContext.CreateAsync</c>).
    /// </remarks>
    /// <param name="configuration">Tenant-scoped configuration.</param>
    /// <param name="factory">Provider factory that creates connections.</param>
    /// <param name="loggerFactory">Logger factory used by the context.</param>
    /// <param name="cancellationToken">Cancels construction.</param>
    /// <returns>A fresh <see cref="IDatabaseContext"/>.</returns>
    Task<IDatabaseContext> CreateAsync(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Create(configuration, factory, loggerFactory));
    }
}