using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// HARN-016: every OracleDataSource owns a private pool that outlived it, so each disposed context left
/// up to two server sessions open (one per read and write data source) until the process exited; the
/// type matrix's per-type contexts ran Oracle Free (200 processes) out. A context now clears the pools
/// of the data sources it created when it is disposed, so disposed contexts leave nothing behind.
/// </summary>
[Collection("IntegrationTests")]
public sealed class OracleDataSourcePoolReleaseTests : DatabaseTestBase
{
    public OracleDataSourcePoolReleaseTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() => [SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await DropTableIfExistsAsync(context, "pool_release");
        await using var create = context.CreateSqlContainer(
            "CREATE TABLE \"pool_release\" (\"id\" NUMBER(10) PRIMARY KEY, \"at\" TIMESTAMP(6))");
        await create.ExecuteNonQueryAsync();
    }

    [Table("pool_release")]
    public sealed class Row
    {
        [Id(true)] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("at", DbType.DateTime)] public DateTime At { get; set; }
    }

    private static async Task<long> SessionsAsync(IDatabaseContext monitor)
    {
        await using var sc = monitor.CreateSqlContainer(
            "SELECT COUNT(*) FROM v$session WHERE username = SYS_CONTEXT('USERENV', 'SESSION_USER')");
        return await sc.ExecuteScalarRequiredAsync<long>();
    }

    [SkippableFact]
    public async Task DisposedContexts_LeaveNoSessionsBehind()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async monitor =>
        {
            var before = await SessionsAsync(monitor);
            for (var i = 0; i < 20; i++)
            {
                await using var context = await CreateAdditionalContextAsync(SupportedDatabase.Oracle);
                var gateway = new TableGateway<Row, int>(context);
                await gateway.CreateAsync(new Row { Id = i + 1, At = DateTime.UtcNow }, context);
                Assert.NotNull(await gateway.RetrieveOneAsync(i + 1, context));
            }

            var after = await SessionsAsync(monitor);
            Output.WriteLine($"Oracle sessions before {before}, after 20 disposed contexts {after}");
            Assert.True(after <= before, $"20 disposed contexts left {after - before} sessions open");
        });
    }
}
