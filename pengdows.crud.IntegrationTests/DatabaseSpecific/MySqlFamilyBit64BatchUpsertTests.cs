using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// DEC-012 / PERF-029: TiDB's VALUES(col) in ON DUPLICATE KEY UPDATE returns a BIT(64) value
/// byte-reversed (REV-083). A batch upsert therefore runs one statement per row for a column that is
/// BIT, or might be; the async gateways learn each long/ulong/byte[] column's declared type once per
/// table, so BIGINT and VARBINARY columns keep the one-statement batch. Both shapes must store every
/// value exactly through the update branch, on every MySQL-family database.
/// </summary>
[Collection("IntegrationTests")]
public sealed class MySqlFamilyBit64BatchUpsertTests : DatabaseTestBase
{
    private const string BitTable = "bit64_upsert";
    private const string WideTable = "wide_upsert";

    public MySqlFamilyBit64BatchUpsertTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    private static readonly SupportedDatabase[] Providers =
    {
        SupportedDatabase.MySql,
        SupportedDatabase.MariaDb,
        SupportedDatabase.TiDb,
        SupportedDatabase.SingleStore
    };

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(Providers.Contains).ToArray();

    [Table(BitTable)]
    public sealed class BitRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("flags", DbType.UInt64)] public ulong Flags { get; set; }
        [Column("n", DbType.Int64)] public long N { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    [Table(WideTable)]
    public sealed class WideRow
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("n", DbType.Int64)] public long N { get; set; }
        [Column("raw", DbType.Binary)] public byte[] Raw { get; set; } = Array.Empty<byte>();
        [Column("name", DbType.String)] public string Name { get; set; } = "";
    }

    private static async Task CreateTableAsync(IDatabaseContext context, string name, string columns)
    {
        await DropTableIfExistsAsync(context, name);
        await using var create = context.CreateSqlContainer(
            $"CREATE TABLE {IntegrationObjectNameHelper.Table(context, name)} ({columns})");
        await create.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task BatchUpsert_Bit64Columns_UpdateBranchStoresExactValues()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            string W(string c) => context.WrapObjectName(c);
            await CreateTableAsync(context, BitTable,
                $"{W("id")} INT PRIMARY KEY, {W("flags")} BIT(64) NOT NULL, {W("n")} BIT(64) NOT NULL, {W("name")} VARCHAR(20) NOT NULL");
            await using var typeContext = await CreateAdditionalContextAsync(provider);
            var gateway = new TableGateway<BitRow, int>(typeContext);

            await gateway.BatchUpsertAsync(new[]
            {
                new BitRow { Id = 1, Flags = 1, N = 1, Name = "a" }, new BitRow { Id = 2, Flags = 2, N = 2, Name = "b" }
            });
            var updated = new[]
            {
                new BitRow { Id = 1, Flags = 0x0102_0304_0506_0708UL, N = 0x1122_3344_5566_7788L, Name = "a2" },
                new BitRow { Id = 2, Flags = 0x8000_0000_0000_0001UL, N = 0x0A0B_0C0D_0E0F_1011L, Name = "b2" }
            };
            await gateway.BatchUpsertAsync(updated);

            var read = (await gateway.RetrieveAsync(new[] { 1, 2 })).OrderBy(r => r.Id).ToList();
            Assert.Equal(updated.Select(r => (r.Flags, r.N, r.Name)), read.Select(r => (r.Flags, r.N, r.Name)));
            if (provider == SupportedDatabase.TiDb)
            {
                // Learned as BIT: one statement per row, never VALUES().
                Assert.Equal(2, gateway.BuildBatchUpsert(updated).Count);
            }
        });
    }

    [SkippableFact]
    public async Task BatchUpsert_BigIntAndVarBinaryColumns_UpdateBranchStoresExactValues()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            string W(string c) => context.WrapObjectName(c);
            await CreateTableAsync(context, WideTable,
                $"{W("id")} INT PRIMARY KEY, {W("n")} BIGINT NOT NULL, {W("raw")} VARBINARY(16) NOT NULL, {W("name")} VARCHAR(20) NOT NULL");
            await using var typeContext = await CreateAdditionalContextAsync(provider);
            var gateway = new TableGateway<WideRow, int>(typeContext);

            await gateway.BatchUpsertAsync(new[]
            {
                new WideRow { Id = 1, N = 1, Raw = new byte[] { 1 }, Name = "a" },
                new WideRow { Id = 2, N = 2, Raw = new byte[] { 2 }, Name = "b" }
            });
            var updated = new[]
            {
                new WideRow { Id = 1, N = 0x1122_3344_5566_7788L, Raw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Name = "a2" },
                new WideRow { Id = 2, N = long.MinValue, Raw = new byte[] { 0xFF, 0, 0x80 }, Name = "b2" }
            };
            await gateway.BatchUpsertAsync(updated);

            var read = (await gateway.RetrieveAsync(new[] { 1, 2 })).OrderBy(r => r.Id).ToList();
            Assert.Equal(updated.Select(r => (r.N, Convert.ToHexString(r.Raw), r.Name)),
                read.Select(r => (r.N, Convert.ToHexString(r.Raw), r.Name)));
            if (provider == SupportedDatabase.TiDb)
            {
                // PERF-029: learned as BIGINT/VARBINARY, so one statement for the batch.
                Assert.Single(gateway.BuildBatchUpsert(updated));
            }
        });
    }
}
