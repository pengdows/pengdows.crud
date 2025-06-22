#region

using System.Data.Common;

#endregion

namespace pengdows.crud.FakeDb.pengdows.crud.enums;

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
        foreach (var kvp in providerFactories)
            DbProviderFactories.RegisterFactory(kvp.Key, new FakeDbFactory(kvp.Value));
    }
}