#region

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.tenant;
using Xunit;

#endregion

namespace pengdows.crud.Tests.tenant;

public class TenantServiceCollectionExtensionsNullTests
{
    [Fact]
    public void AddMultiTenancy_NullServices_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => TenantServiceCollectionExtensions.AddMultiTenancy(null!, config));
    }

    [Fact]
    public void AddMultiTenancy_NullConfiguration_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => TenantServiceCollectionExtensions.AddMultiTenancy(services, null!));
    }
}