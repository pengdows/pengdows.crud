using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ErrorHandling;

[Collection("IntegrationTests")]
public class ConcurrencyConflictTests : DatabaseTestBase
{
    [Table("versioned_jobs")]
    private sealed class VersionedJob
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("version", DbType.Int32)]
        public int Version { get; set; }
    }

    public ConcurrencyConflictTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return new[] { SupportedDatabase.Sqlite, SupportedDatabase.PostgreSql };
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider switch
        {
            SupportedDatabase.Sqlite => """
                CREATE TABLE IF NOT EXISTS versioned_jobs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    version INTEGER NOT NULL DEFAULT 0
                )
                """,
            SupportedDatabase.PostgreSql => """
                CREATE TABLE IF NOT EXISTS versioned_jobs (
                    id BIGSERIAL PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    version INTEGER NOT NULL DEFAULT 0
                )
                """,
            _ => throw new NotSupportedException($"Provider {provider} is not supported.")
        };

        await using var container = context.CreateSqlContainer(sql);
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task UpdateAsync_WithStaleVersion_ThrowsConcurrencyConflictException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<VersionedJob, long>(context, GetAuditResolver());
            var entity = new VersionedJob { Name = "job-a" };
            Assert.True(await gateway.CreateAsync(entity, context));

            var stale = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.NotNull(stale);

            await using (var bump = context.CreateSqlContainer(
                             $"UPDATE {context.WrapObjectName("versioned_jobs")} SET {context.WrapObjectName("version")} = 2 WHERE {context.WrapObjectName("id")} = {entity.Id}"))
            {
                Assert.Equal(1, await bump.ExecuteNonQueryAsync());
            }

            stale!.Name = "job-b";
            stale.Version = 1;

            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
                await gateway.UpdateAsync(stale, false, context));

            Assert.Equal(provider, ex.Database);
            Assert.Null(ex.ConstraintName);
        });
    }
}
