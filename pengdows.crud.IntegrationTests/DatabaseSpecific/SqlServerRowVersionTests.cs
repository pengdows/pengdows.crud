using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// A SQL Server <c>rowversion</c> column mapped as a <see cref="RowVersion"/> [Version]: the server
/// changes it on every write, pengdows.crud only compares it, and a stale copy must be rejected.
/// </summary>
[Collection("IntegrationTests")]
public class SqlServerRowVersionTests : DatabaseTestBase
{
    public SqlServerRowVersionTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(p => p == SupportedDatabase.SqlServer).ToArray();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<RowVersionedEntity>();
        await using var container = context.CreateSqlContainer(@"
IF OBJECT_ID(N'[dbo].[rowversioned]', 'U') IS NOT NULL
    DROP TABLE [dbo].[rowversioned];
CREATE TABLE [dbo].[rowversioned] (
    [id] INT IDENTITY(1,1) PRIMARY KEY,
    [name] NVARCHAR(100) NOT NULL,
    [rv] ROWVERSION NOT NULL
);");
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public Task RowVersion_UpdateThenStaleUpdate_DetectsConflict()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RowVersionedEntity, int>(context);
            var entity = new RowVersionedEntity { Name = "original" };
            Assert.True(await gateway.CreateAsync(entity, context));

            var current = await gateway.RetrieveOneAsync(entity.Id, context);
            var stale = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.Equal(current!.Rv, stale!.Rv);

            current.Name = "first";
            Assert.Equal(1, await gateway.UpdateAsync(current, context));

            var afterUpdate = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.Equal("first", afterUpdate!.Name);
            Assert.NotEqual(stale.Rv, afterUpdate.Rv);

            stale.Name = "stale";
            await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await gateway.UpdateAsync(stale, context));

            var final = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.Equal("first", final!.Name);
        });
    }

    [Table("rowversioned", "dbo")]
    public class RowVersionedEntity
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [NonInsertable]
        [NonUpdateable]
        [Column("rv", DbType.Binary)]
        public RowVersion Rv { get; set; }
    }
}
