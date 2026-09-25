using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// RetrieveAsync(ids) on the PostgreSQL family: PostgreSQL binds a typed array to
/// <c>= ANY(@w0)</c>; CockroachDB and YugabyteDB use expanded IN parameters. Backport of 3.0
/// 81eef2b (BP-116).
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostgreSqlFamilySetValuedRetrieveTests : DatabaseTestBase
{
    private static readonly SupportedDatabase[] Providers =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.CockroachDb,
        SupportedDatabase.YugabyteDb
    };

    public PostgreSqlFamilySetValuedRetrieveTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(Providers.Contains).ToArray();

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, "setvalued_long");
        await DropTableIfExistsAsync(context, "setvalued_guid");
        await DropTableIfExistsAsync(context, "setvalued_text");
        foreach (var (table, type) in new[] { ("setvalued_long", "BIGINT"), ("setvalued_guid", "UUID"), ("setvalued_text", "VARCHAR(40)") })
        {
            await using var sc = context.CreateSqlContainer(
                $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, table)} (" +
                $"{context.WrapObjectName("id")} {type} PRIMARY KEY, {context.WrapObjectName("name")} VARCHAR(40) NOT NULL)");
            await sc.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public Task RetrieveAsync_MultipleIds_AllKeyTypes()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var longs = new TableGateway<SetValuedLongEntity, long>(context);
            var guids = new TableGateway<SetValuedGuidEntity, Guid>(context);
            var texts = new TableGateway<SetValuedTextEntity, string>(context);

            var g = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            for (var i = 0; i < 3; i++)
            {
                Assert.True(await longs.CreateAsync(new SetValuedLongEntity { Id = i + 1, Name = $"l{i}" }, context));
                Assert.True(await guids.CreateAsync(new SetValuedGuidEntity { Id = g[i], Name = $"g{i}" }, context));
                Assert.True(await texts.CreateAsync(new SetValuedTextEntity { Id = $"k{i}", Name = $"t{i}" }, context));
            }

            var failures = new List<string>();
            async Task Check(string name, Func<Task<int>> run, int expected)
            {
                try
                {
                    var count = await run();
                    if (count != expected)
                    {
                        failures.Add($"{name}: expected {expected} rows, got {count}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            await Check("long x3", async () => (await longs.RetrieveAsync(new long[] { 1, 2, 3 }, context)).Count, 3);
            await Check("long x2", async () => (await longs.RetrieveAsync(new long[] { 1, 3 }, context)).Count, 2);
            await Check("long x1", async () => (await longs.RetrieveAsync(new long[] { 2 }, context)).Count, 1);
            await Check("guid x3", async () => (await guids.RetrieveAsync(g, context)).Count, 3);
            await Check("guid x1", async () => (await guids.RetrieveAsync(new[] { g[1] }, context)).Count, 1);
            await Check("text x3", async () => (await texts.RetrieveAsync(new[] { "k0", "k1", "k2" }, context)).Count, 3);
            await Check("text x1", async () => (await texts.RetrieveAsync(new[] { "k1" }, context)).Count, 1);
            await Check("stream long x1", async () =>
            {
                var n = 0;
                await foreach (var _ in longs.RetrieveStreamAsync(new long[] { 2 }, context))
                {
                    n++;
                }

                return n;
            }, 1);

            Assert.True(failures.Count == 0, $"{provider}: " + string.Join(" | ", failures));
        });
    }
}

[Table("setvalued_long")]
public sealed class SetValuedLongEntity
{
    [Id] [Column("id", DbType.Int64)] public long Id { get; set; }
    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
}

[Table("setvalued_guid")]
public sealed class SetValuedGuidEntity
{
    [Id] [Column("id", DbType.Guid)] public Guid Id { get; set; }
    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
}

[Table("setvalued_text")]
public sealed class SetValuedTextEntity
{
    [Id] [Column("id", DbType.String)] public string Id { get; set; } = string.Empty;
    [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
}
