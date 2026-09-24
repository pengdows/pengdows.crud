using System.Data;
using pengdows.crud.@internal;
using pengdows.crud.infrastructure;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// <see cref="Range{T}"/> written through a gateway into a real <c>int4range</c> column: what the
/// database stores must match what the value object means.
/// </summary>
[Collection("IntegrationTests")]
public class PostgreSqlRangeRoundTripTests : DatabaseTestBase
{
    private static long _nextId;

    private static readonly SupportedDatabase[] RangeProviders =
    {
        SupportedDatabase.PostgreSql,
        SupportedDatabase.YugabyteDb
    };

    public PostgreSqlRangeRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(RangeProviders.Contains).ToArray();
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        context.RegisterEntity<RangeEntity>();
        await DropTableIfExistsAsync(context, "range_entity");

        var table = IntegrationObjectNameHelper.Table(context, "range_entity");
        await using var container = context.CreateSqlContainer($@"
CREATE TABLE {table} (
    {context.WrapObjectName("id")} BIGINT PRIMARY KEY,
    {context.WrapObjectName("span")} INT4RANGE NOT NULL
)");
        await container.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsEmptyInDatabaseAsync(IDatabaseContext context, long id)
    {
        var table = IntegrationObjectNameHelper.Table(context, "range_entity");
        await using var sc = context.CreateSqlContainer(
            $"SELECT isempty({context.WrapObjectName("span")}) FROM {table} WHERE {context.WrapObjectName("id")} = ");
        var p = sc.AddParameterWithValue("id", DbType.Int64, id);
        sc.Query.Append(sc.MakeParameterName(p));
        return await sc.ExecuteScalarRequiredAsync<bool>();
    }

    [SkippableFact]
    public Task BoundedRange_RoundTrips()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RangeEntity, long>(context);
            var expected = new Range<int>(1, 10, true, false);
            var entity = new RangeEntity { Id = Interlocked.Increment(ref _nextId), Span = expected };

            Assert.True(await gateway.CreateAsync(entity, context));

            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.Equal(expected, retrieved!.Span);
        });
    }

    [SkippableFact]
    public Task EmptyRange_IsStoredAsEmpty_AndReadsBackEmpty()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RangeEntity, long>(context);
            var entity = new RangeEntity { Id = Interlocked.Increment(ref _nextId), Span = Range<int>.Empty };

            Assert.True(await gateway.CreateAsync(entity, context));

            Assert.True(await IsEmptyInDatabaseAsync(context, entity.Id),
                $"{provider}: Range<int>.Empty was stored as a non-empty range");
            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);
            Assert.Equal(Range<int>.Empty, retrieved!.Span);
        });
    }

    [SkippableFact]
    public Task UnboundedRange_IsStoredAsAllValues_AndIsNotEmpty()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<RangeEntity, long>(context);
            var unbounded = new Range<int>(null, null, false, false);
            var entity = new RangeEntity { Id = Interlocked.Increment(ref _nextId), Span = unbounded };

            Assert.True(await gateway.CreateAsync(entity, context));

            Assert.False(await IsEmptyInDatabaseAsync(context, entity.Id));
            var retrieved = await gateway.RetrieveOneAsync(entity.Id, context);
            // IsEmpty stays true for an unbounded range on 2.0.x; IsEmptyRange tells them apart.
            Assert.False(retrieved!.Span.IsEmptyRange, $"{provider}: an unbounded range read back as empty");
            Assert.Equal(unbounded, retrieved.Span);
        });
    }

    [Table("range_entity")]
    public class RangeEntity
    {
        [Id(true)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("span", DbType.Object)]
        public Range<int> Span { get; set; }
    }
}
