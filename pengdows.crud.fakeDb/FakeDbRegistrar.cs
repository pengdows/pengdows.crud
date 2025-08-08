using System;
using System.Collections.Generic;
using System.Data.Common;

namespace pengdows.crud.FakeDb;

public class FakeDbRegistrar
{
    private readonly DbProviderFactory _factory;

    public FakeDbRegistrar(DbProviderFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Register(string providerInvariantName, DbProviderFactory providerFactory)
    {
        var key = string.IsNullOrEmpty(providerInvariantName)
            ? "pengdows.crud.FakeDb"
            : providerInvariantName;
        DbProviderFactories.RegisterFactory(key, providerFactory);
    }

    public void RegisterAll(Dictionary<string, string> providerFactories)
    {
        if (providerFactories == null)
        {
            throw new ArgumentNullException(nameof(providerFactories));
        }

        foreach (var kvp in providerFactories)
        {
            DbProviderFactories.RegisterFactory(kvp.Key, new FakeDbFactory(kvp.Value));
        }
    }
}