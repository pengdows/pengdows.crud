#region

using System;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;

#endregion

namespace pengdows.crud.tenant;

internal sealed class DefaultDatabaseContextFactory : IDatabaseContextFactory
{
    public IDatabaseContext Create(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
        ILoggerFactory loggerFactory)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return new DatabaseContext(configuration, factory, loggerFactory);
    }

    public async Task<IDatabaseContext> CreateAsync(IDatabaseContextConfiguration configuration, DbProviderFactory factory,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken = default)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return await DatabaseContext.CreateAsync(configuration, factory, loggerFactory, cancellationToken).ConfigureAwait(false);
    }
}