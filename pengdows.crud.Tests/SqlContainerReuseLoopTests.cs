#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Xunit;

using Xunit.Abstractions;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerReuseLoopTests : SqlLiteContextTestBase
{
    private readonly TableGateway<TestEntity, int> _gateway;
    private readonly ITestOutputHelper _output;

    public SqlContainerReuseLoopTests(ITestOutputHelper output)
    {
        _output = output;
        TypeMap.Register<TestEntity>();
        _gateway = new TableGateway<TestEntity, int>(Context, AuditValueResolver);
        BuildTestTable().GetAwaiter().GetResult();
    }

    private async Task BuildTestTable()
    {
        var qp = Context.QuotePrefix;
        var qs = Context.QuoteSuffix;
        var sql = string.Format(
            @"CREATE TABLE IF NOT EXISTS {0}Test{1} (
                {0}Id{1} INTEGER PRIMARY KEY AUTOINCREMENT,
                {0}Name{1} TEXT,
                {0}CreatedOn{1} DATETIME,
                {0}CreatedBy{1} TEXT,
                {0}LastUpdatedOn{1} DATETIME,
                {0}LastUpdatedBy{1} TEXT,
                {0}Version{1} INTEGER NOT NULL DEFAULT 0
            )", qp, qs);
        var container = Context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SqlContainer_CanBeReused_WithSetParameterValue_InLoop()
    {
        // 1. Setup a new context in Standard mode to ensure connections are opened/closed per call
        // We use a unique Data Source name to avoid interference with other tests
        var dbName = $"ReuseLoopTest_{Guid.NewGuid():N}.db";
        var config = new pengdows.crud.configuration.DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared;EmulatedProduct=Sqlite",
            DbMode = pengdows.crud.enums.DbMode.Standard,
            EnableMetrics = true
        };

        // Use the same factory as the base class
        var factory = new fakeDbFactory(pengdows.crud.enums.SupportedDatabase.Sqlite);
        factory.EnableDataPersistence = true;

        await using var standardContext = new DatabaseContext(config, factory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, TypeMap);
        var gateway = new TableGateway<TestEntity, int>(standardContext, AuditValueResolver);

        // 2. Seed some data
        var entities = new List<TestEntity>();
        for (int i = 1; i <= 5; i++)
        {
            var entity = new TestEntity { Name = $"Entity {i}" };
            await gateway.CreateAsync(entity);
            entities.Add(entity);
        }

        // Get metrics after seeding
        var metricsAfterSeed = standardContext.Metrics;
        var opensAfterSeed = metricsAfterSeed.ConnectionsOpened;

        // 3. Build a retrieve container for the first ID
        var sc = gateway.BuildRetrieve(new[] { entities[0].Id });

        // 4. Reuse the container in a loop by changing the parameter value
        for (int i = 0; i < entities.Count; i++)
        {
            var targetId = entities[i].Id;

            // Update the parameter value
            sc.SetParameterValue("p0", targetId);

            // Execute the container
            var result = await gateway.LoadSingleAsync(sc);

            // Verify
            Assert.NotNull(result);
            Assert.Equal(targetId, result!.Id);
            Assert.Equal($"Entity {i + 1}", result.Name);
        }

        // 5. Verify that multiple connections were opened (one for each LoadSingleAsync call)
        var finalMetrics = standardContext.Metrics;
        var newOpens = finalMetrics.ConnectionsOpened - opensAfterSeed;

        // We expect exactly entities.Count (5) new connections to have been opened
        Assert.Equal(entities.Count, (int)newOpens);
    }
}
