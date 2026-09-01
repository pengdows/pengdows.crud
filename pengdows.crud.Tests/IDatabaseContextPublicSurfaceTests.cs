using System;
using System.Linq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Guards against re-exposing the raw provider DbDataSource on the public IDatabaseContext
/// contract. Normal execution APIs (ISqlContainer, ITrackedReader) are unleakable by
/// accident; a public DataSource property lets any caller bypass governor accounting,
/// session settings, and disposal tracking via DataSource.CreateConnection(). See
/// docs/positioning/product-thesis.md principle 5.
/// </summary>
public class IDatabaseContextPublicSurfaceTests
{
    // Type.GetProperty(name) on an interface only searches members declared directly on
    // that interface, not inherited ones — so ITransactionContext : IDatabaseContext must
    // be checked across its full interface closure, not just its own declared members.
    private static bool ExposesDataSourceProperty(Type interfaceType)
    {
        return new[] { interfaceType }.Concat(interfaceType.GetInterfaces())
            .Any(t => t.GetProperty("DataSource") != null);
    }

    [Fact]
    public void IDatabaseContext_RetainsDataSourceCompatibility()
    {
        Assert.True(ExposesDataSourceProperty(typeof(IDatabaseContext)));
    }

    [Fact]
    public void ITransactionContext_RetainsDataSourceCompatibility()
    {
        Assert.True(ExposesDataSourceProperty(typeof(ITransactionContext)));
    }
}
