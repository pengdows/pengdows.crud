using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.isolation;
using pengdows.crud.tenant;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

[Table("Users")]
public class User
{
    [Id]
    public int Id { get; set; }

    [Column("Name", DbType.String)]
    public string Name { get; set; } = string.Empty;

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

public class TestAuditValueResolver : IAuditValueResolver
{
    public IAuditValues Resolve() => new AuditValues { UserId = "system", UtcNow = DateTime.UtcNow };
}

public class MultitenantIntegrationTests
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
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] = SupportedDatabase.Sqlite.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:DbMode"] = DbMode.SingleConnection.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ReadWriteMode"] = ReadWriteMode.ReadWrite.ToString(),
                ["MultiTenant:Tenants:1:Name"] = "TenantB",
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=TenantB;Trusted_Connection=True;",
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ProviderName"] = SupportedDatabase.SqlServer.ToString(),
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:DbMode"] = DbMode.KeepAlive.ToString(),
                ["MultiTenant:Tenants:1:DatabaseContextConfiguration:ReadWriteMode"] = ReadWriteMode.ReadWrite.ToString()
            })
            .Build();

        services.AddKeyedSingleton<DbProviderFactory>(SupportedDatabase.Sqlite.ToString(), SqliteFactory.Instance);
        services.AddKeyedSingleton<DbProviderFactory>(SupportedDatabase.SqlServer.ToString(), SqlClientFactory.Instance);
        services.AddLogging();
        services.AddMultiTenancy(configuration);
        _provider = services.BuildServiceProvider();
        _tenantRegistry = _provider.GetRequiredService<ITenantContextRegistry>();
    }

    [Theory]
    [InlineData("TenantA", SupportedDatabase.Sqlite)]
    [InlineData("TenantB", SupportedDatabase.SqlServer)]
    public async Task MultitenantCrud_ConcurrentOperations(string tenant, SupportedDatabase dbType)
    {
        var context = _tenantRegistry.GetContext(tenant);
        await new SqlContainer(context)
            .AppendQuery($"CREATE TABLE Users (Id INTEGER PRIMARY KEY{(dbType == SupportedDatabase.Sqlite ? " AUTOINCREMENT" : string.Empty)}, Name VARCHAR(50), CreatedOn DATETIME, LastUpdatedOn DATETIME, Version INTEGER)")
            .ExecuteNonQueryAsync();

        async Task PerformCrud(EntityHelper<User, int> helper, ITransactionContext transaction)
        {
            var user = new User { Name = $"User_{tenant}_{Guid.NewGuid()}" };
            var createSc = helper.BuildCreate(user);
            await createSc.ExecuteNonQueryAsync();
            var id = dbType == SupportedDatabase.Sqlite
                ? (int)(await new SqlContainer(transaction)
                    .AppendQuery("SELECT last_insert_rowid()")
                    .ExecuteScalarAsync<int>())
                : user.Id;

            var retrieveSc = helper.BuildRetrieve(new[] { id });
            var retrievedUser = await helper.LoadSingleAsync(retrieveSc);
            Assert.Equal(user.Name, retrievedUser.Name);
            Assert.Equal(1, retrievedUser.Version);

            retrievedUser.Name = $"Updated_{retrievedUser.Name}";
            var updateSc = await helper.BuildUpdateAsync(retrievedUser, true);
            await updateSc.ExecuteNonQueryAsync();

            var deleteSc = helper.BuildDelete(id);
            await deleteSc.ExecuteNonQueryAsync();
        }

        var tasks = new Task[10];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                using var transaction = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
                try
                {
                    var helper = new EntityHelper<User, int>(context, new TestAuditValueResolver());
                    await PerformCrud(helper, transaction);
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception($"Tenant {tenant} failed: {ex.Message}", ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        var countSc = new SqlContainer(context)
            .AppendQuery("SELECT COUNT(*) FROM Users");
        var count = await countSc.ExecuteScalarAsync<long>();
        Assert.Equal(0L, count);
    }
}
