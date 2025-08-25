#region

using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using testbed;
using Xunit;
using System.Threading.Tasks;
using System;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.Tests;

public class DuckDbTestContainerTests
{
    [Fact]
    public async Task GetDatabaseContextAsync_ReturnsContext_WhenStarted()
    {
        await using var container = new DuckDbTestContainer();
        await container.StartAsync();
        var services = new ServiceCollection()
            .AddSingleton<ITypeMapRegistry, TypeMapRegistry>()
            .BuildServiceProvider();

        await using var ctx = await container.GetDatabaseContextAsync(services);
        Assert.Equal(SupportedDatabase.DuckDB, ctx.Product);
    }

    [Fact]
    public async Task GetDatabaseContextAsync_WithoutStart_Throws()
    {
        await using var container = new DuckDbTestContainer();
        var services = new ServiceCollection()
            .AddSingleton<ITypeMapRegistry, TypeMapRegistry>()
            .BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await container.GetDatabaseContextAsync(services));
    }
}
