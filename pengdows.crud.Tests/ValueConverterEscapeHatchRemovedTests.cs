using pengdows.crud;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// The library owns every database type it can map, and users never write hooks for them (maintainer goal,
/// 2026-09-29). The application converter escape hatch (IProviderValueConverter&lt;T&gt; and
/// ProviderValueConverters.Register) was added in 2.0.6 and is unshipped, so it is removed before release rather than
/// deprecated: it has no 2.0.5 users to break.
/// </summary>
public class ValueConverterEscapeHatchRemovedTests
{
    [Fact]
    public void ProviderValueConverters_IsNotInTheCoreAssembly()
    {
        Assert.Null(typeof(DatabaseContext).Assembly.GetType("pengdows.crud.ProviderValueConverters"));
    }

    [Fact]
    public void IProviderValueConverter_IsNotInTheAbstractionsAssembly()
    {
        Assert.Null(typeof(IDatabaseContext).Assembly.GetType("pengdows.crud.IProviderValueConverter`1"));
    }
}
