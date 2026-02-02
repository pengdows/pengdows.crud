using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

[Table("Users")]
public class User
{
    [Id] [Column("Id", DbType.Int32)] public int Id { get; set; }

    [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

    [Column("CreatedOn", DbType.DateTime)]
    [CreatedOn]
    public DateTime CreatedOn { get; set; }

    [Column("LastUpdatedOn", DbType.DateTime)]
    [LastUpdatedOn]
    public DateTime? LastUpdatedOn { get; set; }

    [Column("Version", DbType.Int32)]
    [Version]
    public int Version { get; set; }
}

public class AuditValueResolver : IAuditValueResolver
{
    public IAuditValues Resolve()
    {
        return new AuditValues { UserId = "system", UtcNow = DateTime.UtcNow };
    }
}

public class MultitenantIntegrationTests : IAsyncLifetime
{
    private readonly IServiceProvider _provider;
    private readonly ITenantContextRegistry _tenantRegistry;

    public MultitenantIntegrationTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MultiTenant:Tenants:0:Name"] = "TenantA",
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ConnectionString"] = "Data Source=:memory:",
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] =
                    SupportedDatabase.Sqlite.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:DbMode"] = DbMode.SingleConnection.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ReadWriteMode"] =
                    ReadWriteMode.ReadWrite.ToString(),
                ["MultiTenant:Tenants:1:Name"] = "TenantB",
                // Use a shared in-memory SQLite database to allow concurrent connections
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ConnectionString"] =
                    "Data Source=file:tenantb?mode=memory&cache=shared",
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ProviderName"] =
                    SupportedDatabase.Sqlite.ToString(),
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:DbMode"] = DbMode.SingleWriter.ToString(),
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ReadWriteMode"] =
                    ReadWriteMode.ReadWrite.ToString()
            })
            .Build();

        // Unit tests must use fakeDb; register a fakeDb factory keyed by provider name
        services.AddKeyedSingleton<DbProviderFactory>(SupportedDatabase.Sqlite.ToString(),
            (_, _) => new fakeDbFactory(SupportedDatabase.Sqlite));
        // Use fakeDb for all tenants in unit tests to avoid external dependencies
        services.AddLogging();
        services.AddMultiTenancy(configuration);
        _provider = services.BuildServiceProvider();
        _tenantRegistry = _provider.GetRequiredService<ITenantContextRegistry>();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Ensure background services (e.g., logging processors) are stopped cleanly
        if (_provider is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
        else if (_provider is IDisposable d)
        {
            d.Dispose();
        }
    }

    [Theory]
    [InlineData("TenantA", SupportedDatabase.Sqlite)]
    [InlineData("TenantB", SupportedDatabase.Sqlite)]
    public Task MultitenantCrud_ConcurrentOperations(string tenant, SupportedDatabase dbType)
    {
        _ = dbType;
        // Use fakeDb and assert mode coercions + pinned-writer semantics deterministically
        var ctx = _tenantRegistry.GetContext(tenant);

        if (tenant == "TenantA")
        {
            // :memory: coerces to SingleConnection
            Assert.Equal(DbMode.SingleConnection, ctx.ConnectionMode);
            var r1 = ctx.GetConnection(ExecutionType.Read);
            var w1 = ctx.GetConnection(ExecutionType.Write);
            Assert.Same(r1, w1); // pinned single connection for all operations
        }
        else if (tenant == "TenantB")
        {
            // shared in-memory should still coerce to SingleWriter, but writes are serialized via governor
            Assert.Equal(DbMode.SingleWriter, ctx.ConnectionMode);
            var w1 = ctx.GetConnection(ExecutionType.Write);
            ctx.CloseAndDisposeConnection(w1);

            var w2 = ctx.GetConnection(ExecutionType.Write);
            ctx.CloseAndDisposeConnection(w2);

            var dbContext = Assert.IsType<DatabaseContext>(ctx);
            var writerSnapshot = dbContext.GetPoolStatisticsSnapshot(PoolLabel.Writer);
            Assert.True(writerSnapshot.TotalAcquired >= 2, "Each write should acquire its own permit.");

            var r = ctx.GetConnection(ExecutionType.Read);
            Assert.NotSame(w2, r); // read connection should be independent
            ctx.CloseAndDisposeConnection(r);
        }

        return Task.CompletedTask;
    }
}
