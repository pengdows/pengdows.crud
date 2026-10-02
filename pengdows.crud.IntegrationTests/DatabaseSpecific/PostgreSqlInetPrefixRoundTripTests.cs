using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Npgsql's GetValue() returns a plain IPAddress for an inet column, dropping its netmask
/// (confirmed live, Npgsql 9: '192.168.1.10/24'::inet reads as 192.168.1.10). Hydration must read
/// NpgsqlInet instead so an Inet with a prefix round-trips (TYPE-002).
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSqlInetPrefixRoundTripTests : DatabaseTestBase
{
    private static readonly SupportedDatabase[] Family =
        { SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb };

    public PostgreSqlInetPrefixRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        if (!Family.Contains(provider))
        {
            return;
        }

        await DropTableIfExistsAsync(context, "inet_prefix");
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, "inet_prefix")} (id INTEGER PRIMARY KEY, addr INET NOT NULL)");
        await create.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task InetWithPrefix_RoundTripsThroughGatewayAndDataReaderMapper()
    {
        await RunTestAgainstProvidersAsync(Family, async (provider, context) =>
        {
            var expected = Inet.Parse("192.168.1.10/24");
            var gateway = new TableGateway<InetRow, int>(context);
            await gateway.CreateAsync(new InetRow { Id = 1, Addr = expected }, context);

            Assert.Equal(expected, (await gateway.RetrieveOneAsync(1, context))!.Addr);

            await using var sc = context.CreateSqlContainer(
                $"SELECT addr AS \"Addr\" FROM {IntegrationObjectNameHelper.Table(context, "inet_prefix")}");
            await using var reader = await sc.ExecuteReaderAsync();
            var mapped = await DataReaderMapper.LoadObjectsFromDataReaderAsync<MappedInet>(reader);
            Assert.Equal(expected, Assert.Single(mapped).Addr);
        });
    }

    [Table("inet_prefix")]
    private sealed class InetRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("addr", DbType.Object)] public Inet Addr { get; set; }
    }

    private sealed class MappedInet
    {
        public Inet Addr { get; set; }
    }
}
