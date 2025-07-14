#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.tenant;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

[Table("Users")]
public class User
{
    [Id(false)]
    [Column("Id", DbType.Int32)]
    public int Id { get; set; }

    [PrimaryKey]
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
    public IAuditValues Resolve()
    {
        return new AuditValues { UserId = "system", UtcNow = DateTime.UtcNow };
    }
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
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ProviderName"] =
                    SupportedDatabase.Sqlite.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:DbMode"] = DbMode.SingleConnection.ToString(),
                ["MultiTenant:Tenants:0:DatabaseContextConfiguration:ReadWriteMode"] =
                    ReadWriteMode.ReadWrite.ToString()
            })
            .Build();

        services.AddKeyedSingleton<DbProviderFactory>(SupportedDatabase.Sqlite.ToString(), SqliteFactory.Instance);
        services.AddLogging();
        services.AddMultiTenancy(configuration);
        _provider = services.BuildServiceProvider();
        _tenantRegistry = _provider.GetRequiredService<ITenantContextRegistry>();
    }

    [Fact]
    public async Task MultitenantCrud_SequentialOperations()
    {
        const string tenant = "TenantA";
        var context = _tenantRegistry.GetContext(tenant);
        var dbType = SupportedDatabase.Sqlite;
        var auditValueResolver = new TestAuditValueResolver();
        var tableSc = context.CreateSqlContainer();
        tableSc.Query.AppendFormat(@"CREATE TABLE {0}Users{1} 
                            ({0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT, 
                            {0}Name{1} VARCHAR(50), 
                            {0}CreatedOn{1} DATETIME, 
                            {0}LastUpdatedOn{1} DATETIME,
                            {0}Version{1} INTEGER)", context.QuotePrefix, context.QuoteSuffix);
        await tableSc.ExecuteNonQueryAsync();

        async Task PerformCrud(IEntityHelper<User, int> helper, ITransactionContext transaction)
        {
            var user = new User { Name = $"User_{tenant}_{Guid.NewGuid()}" };
            var createSc = helper.BuildCreate(user);
            await createSc.ExecuteNonQueryAsync();
            var retrievedUser = await helper.RetrieveOneAsync(user, transaction);

            Assert.Equal(user.Name, retrievedUser.Name);
            Assert.Equal(1, retrievedUser.Version);

            retrievedUser.Name = $"Updated_{retrievedUser.Name}";
            var updateSc = await helper.BuildUpdateAsync(retrievedUser, true);
            await updateSc.ExecuteNonQueryAsync();

            var deleteSc = helper.BuildDelete(retrievedUser.Id);
            await deleteSc.ExecuteNonQueryAsync();
        }

        await using var transaction = context.BeginTransaction(IsolationProfile.SafeNonBlockingReads);
        var helper = new EntityHelper<User, int>(context, auditValueResolver);
        await PerformCrud(helper, transaction);
        transaction.Commit();

        var countSc = context.CreateSqlContainer();
        countSc.Query.AppendFormat("SELECT COUNT(*) FROM {0}Users{1}", context.QuotePrefix, context.QuoteSuffix);
        var count = await countSc.ExecuteScalarAsync<long>();
        Assert.Equal(0L, count);
    }
}