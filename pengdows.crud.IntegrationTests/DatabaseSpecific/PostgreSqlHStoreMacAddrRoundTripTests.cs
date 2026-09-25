using System.Data;
using Npgsql;
using pengdows.crud.attributes;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Live PostgreSQL round-trips for <see cref="HStore"/> (hstore) and <see cref="MacAddress"/>
/// (macaddr / macaddr8) through the real CRUD mapper and Npgsql. Backported from 3.0's
/// PostgreSqlAdvancedTypeRoundTripTests (11c3c6c, 1187d9d).
/// </summary>
[Collection("IntegrationTests")]
public sealed class PostgreSqlHStoreMacAddrRoundTripTests : DatabaseTestBase
{
    public PostgreSqlHStoreMacAddrRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.PostgreSql).ToArray();

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using (var extension = context.CreateSqlContainer("CREATE EXTENSION IF NOT EXISTS hstore"))
        {
            await extension.ExecuteNonQueryAsync();
        }

        // Npgsql caches the type catalog per connection string the first time a connection opens
        // (DatabaseContext already opened one during detection, before hstore existed). Reload it
        // and clear the pool so every connection sees the hstore OID.
        var borrowed = ((DatabaseContext)context).GetConnection(ExecutionType.Write);
        try
        {
            if (borrowed.State != ConnectionState.Open)
            {
                await borrowed.OpenAsync();
            }

            var npgsql = (NpgsqlConnection)((IInternalConnectionWrapper)borrowed).UnderlyingConnection;
            await npgsql.ReloadTypesAsync();
            NpgsqlConnection.ClearPool(npgsql);
        }
        finally
        {
            ((DatabaseContext)context).CloseAndDisposeConnectionInternal(borrowed);
        }

        await DropTableIfExistsAsync(context, "hstore_roundtrip");
        await using (var table = context.CreateSqlContainer($"""
            CREATE TABLE {IntegrationObjectNameHelper.Table(context, "hstore_roundtrip")} (
                id           INTEGER PRIMARY KEY,
                hstore_value HSTORE NOT NULL
            )
            """))
        {
            await table.ExecuteNonQueryAsync();
        }

        await DropTableIfExistsAsync(context, "mac_roundtrip");
        await using (var table = context.CreateSqlContainer($"""
            CREATE TABLE {IntegrationObjectNameHelper.Table(context, "mac_roundtrip")} (
                id           INTEGER PRIMARY KEY,
                mac_value    MACADDR NOT NULL,
                mac8_value   MACADDR8 NOT NULL
            )
            """))
        {
            await table.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task HStore_RoundTripsThroughGateway()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            var expected = new HStoreEntity
            {
                Id = 1,
                HStoreValue = new HStore(new Dictionary<string, string?>
                {
                    ["role"] = "admin",
                    ["nickname"] = null,
                    ["needs quoting"] = "has, special=>chars"
                })
            };

            var gateway = new TableGateway<HStoreEntity, int>(context);
            Assert.True(await gateway.CreateAsync(expected, context));

            var actual = await gateway.RetrieveOneAsync(expected.Id, context);
            Assert.NotNull(actual);
            Assert.Equal(expected.HStoreValue, actual!.HStoreValue);
            Assert.Equal("admin", actual.HStoreValue["role"]);
            Assert.Null(actual.HStoreValue["nickname"]);
            Assert.Equal("has, special=>chars", actual.HStoreValue["needs quoting"]);
        });
    }

    [SkippableFact]
    public async Task MacAddress_SixAndEightByte_RoundTripAndBindInWherePredicate()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async context =>
        {
            var gateway = new TableGateway<MacEntity, int>(context);
            var eightByte = MacAddress.Parse("08:00:2B:01:02:03:04:06");
            var sixByte = MacAddress.Parse("08:00:2B:01:02:04");
            Assert.True(await gateway.CreateAsync(new MacEntity
            {
                Id = 2,
                MacValue = sixByte,
                Mac8Value = eightByte
            }, context));

            var actual = await gateway.RetrieveOneAsync(2, context);
            Assert.NotNull(actual);
            Assert.Equal(sixByte, actual!.MacValue);
            Assert.Equal(eightByte, actual.Mac8Value);

            var table = IntegrationObjectNameHelper.Table(context, "mac_roundtrip");
            await using var predicate = context.CreateSqlContainer($"SELECT id FROM {table} WHERE mac8_value = ");
            var p = predicate.AddParameterWithValue("mac8", DbType.Object, eightByte);
            predicate.Query.Append(predicate.MakeParameterName(p));
            Assert.Equal(2, await predicate.ExecuteScalarOrNullAsync<int>());

            await using var predicate6 = context.CreateSqlContainer($"SELECT id FROM {table} WHERE mac_value = ");
            var p6 = predicate6.AddParameterWithValue("mac6", DbType.Object, sixByte);
            predicate6.Query.Append(predicate6.MakeParameterName(p6));
            Assert.Equal(2, await predicate6.ExecuteScalarOrNullAsync<int>());
        });
    }
}

[Table("hstore_roundtrip")]
public sealed class HStoreEntity
{
    [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("hstore_value", DbType.Object)] public HStore HStoreValue { get; set; }
}

[Table("mac_roundtrip")]
public sealed class MacEntity
{
    [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("mac_value", DbType.Object)] public MacAddress MacValue { get; set; }
    [Column("mac8_value", DbType.Object)] public MacAddress Mac8Value { get; set; }
}
