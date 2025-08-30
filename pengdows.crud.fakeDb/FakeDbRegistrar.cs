using System.Data.Common;

namespace pengdows.crud.fakeDb;

public class fakeDbRegistrar
{
    private readonly DbProviderFactory _factory;

    public fakeDbRegistrar(DbProviderFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Register(string providerInvariantName, DbProviderFactory providerFactory)
    {
        var key = string.IsNullOrEmpty(providerInvariantName)
            ? "pengdows.crud.fakeDb"
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
            DbProviderFactories.RegisterFactory(kvp.Key, new fakeDbFactory(kvp.Value));
        }
    }
}
