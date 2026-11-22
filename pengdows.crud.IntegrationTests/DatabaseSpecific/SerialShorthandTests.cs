using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.@internal;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The SERIAL shorthand, declared as the spelling itself (not IDENTITY or AUTO_INCREMENT, which InsertReturningTests covers): the
/// column is filled by the server when the insert leaves it out, and the gateway reads the generated value back into the entity.
/// PostgreSQL and YugabyteDB expand it to an int4 with a sequence default, CockroachDB to an int with unique_rowid() (distinct, not
/// consecutive), MySQL, MariaDB and TiDB to BIGINT UNSIGNED AUTO_INCREMENT UNIQUE, Informix to its own SERIAL type.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SerialShorthandTests : DatabaseTestBase
{
    private const string TableName = "serial_shorthand";

    // The documented SERIAL databases: the PostgreSQL and MySQL families, minus SingleStore (not documented for SERIAL), plus Informix.
    private static readonly SupportedDatabase[] MySqlDocumented =
        DatabaseFamilies.MySql.Where(p => p != SupportedDatabase.SingleStore).ToArray();

    private static readonly SupportedDatabase[] Documented =
        DatabaseFamilies.PostgreSql.Concat(MySqlDocumented).Append(SupportedDatabase.Informix).ToArray();

    public SerialShorthandTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(Documented.Contains).ToArray();

    // The table is rebuilt by the test, so a sequence it leaves behind never carries values over.
    protected override bool ReusesSchemaAcrossTests => false;

    private static string Ddl(SupportedDatabase provider, string table, IDatabaseContext context) => provider switch
    {
        _ when Array.IndexOf(MySqlDocumented, provider) >= 0 =>
            $"CREATE TABLE {table} (`id` SERIAL, `name` VARCHAR(255) NOT NULL)",
        SupportedDatabase.Informix =>
            $"CREATE TABLE {table} ({context.WrapObjectName("id")} SERIAL NOT NULL PRIMARY KEY, {context.WrapObjectName("name")} VARCHAR(255) NOT NULL)",
        _ => $"CREATE TABLE {table} (id SERIAL PRIMARY KEY, name VARCHAR(255) NOT NULL)"
    };

    [SkippableFact]
    public async Task SerialColumn_IsFilledByTheServer_AndReadBackIntoTheEntity()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            await DropTableIfExistsAsync(context, TableName);
            var table = context.WrapObjectName(TableName);
            await using (var create = context.CreateSqlContainer(Ddl(provider, table, context)))
            {
                await create.ExecuteNonQueryAsync();
            }

            ((TypeMapRegistry)context.GetInternalTypeMapRegistry()).Register<SerialRow>();
            var gateway = new TableGateway<SerialRow, long>(context);
            var rows = new[] { new SerialRow { Name = "a" }, new SerialRow { Name = "b" }, new SerialRow { Name = "c" } };
            foreach (var row in rows)
            {
                Assert.True(await gateway.CreateAsync(row, context), $"{provider}: insert");
                Assert.True(row.Id > 0, $"{provider}: expected a generated id, got {row.Id}");
            }

            var ids = rows.Select(r => r.Id).ToArray();
            Assert.Equal(3, ids.Distinct().Count());
            if (provider != SupportedDatabase.CockroachDb)
            {
                // unique_rowid() on CockroachDB is distinct and roughly time ordered, not consecutive; the others count up.
                Assert.Equal(ids.OrderBy(i => i), ids);
            }

            foreach (var row in rows)
            {
                var back = await gateway.RetrieveOneAsync(row.Id, context);
                Assert.NotNull(back);
                Assert.Equal(row.Name, back!.Name);
            }
        });
    }

    [Table(TableName)]
    private sealed class SerialRow
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)] public string Name { get; set; } = string.Empty;
    }
}
