using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Informix's named ROW type (B59), distinct type and LVARCHAR (A74, B67). Written through plain SQL: none has a CLR property type
/// of its own. Needs Informix's Linux client, so it was written on Windows and has to be run on a Linux host.
/// </summary>
[Collection("IntegrationTests")]
public sealed class InformixSpecialTypeTests : DatabaseTestBase
{
    public InformixSpecialTypeTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.Informix).ToArray();

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    private static async Task TryExec(IDatabaseContext context, string sql)
    {
        try
        {
            await Exec(context, sql);
        }
        catch (Exception)
        {
            // the object was not there
        }
    }

    private static async Task<T?> Scalar<T>(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        return await sc.ExecuteScalarOrNullAsync<T>();
    }

    [SkippableFact]
    public async Task NamedRowType_StoresItsFields()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Informix, async context =>
        {
            await TryExec(context, "DROP TABLE ifx_row_doc");
            await TryExec(context, "DROP ROW TYPE ifx_addr_t RESTRICT");
            await Exec(context, "CREATE ROW TYPE ifx_addr_t (street VARCHAR(40), zip INTEGER)");
            await Exec(context, "CREATE TABLE ifx_row_doc (id INTEGER NOT NULL PRIMARY KEY, a ifx_addr_t)");
            await Exec(context, "INSERT INTO ifx_row_doc VALUES (1, ROW('1 Main St', 12345)::ifx_addr_t)");

            Assert.Equal("1 Main St", await Scalar<string>(context, "SELECT a.street FROM ifx_row_doc WHERE id = 1"));
            Assert.Equal(12345, await Scalar<int>(context, "SELECT a.zip FROM ifx_row_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE ifx_row_doc");
            await Exec(context, "DROP ROW TYPE ifx_addr_t RESTRICT");
        });
    }

    [SkippableFact]
    public async Task DistinctType_HasItsBaseStorage_ButDoesNotMixWithIt()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Informix, async context =>
        {
            await TryExec(context, "DROP TABLE ifx_distinct_doc");
            await TryExec(context, "DROP TYPE ifx_money_t RESTRICT");
            await Exec(context, "CREATE DISTINCT TYPE ifx_money_t AS DECIMAL(9,2)");
            await Exec(context, "CREATE TABLE ifx_distinct_doc (id INTEGER NOT NULL PRIMARY KEY, m ifx_money_t)");
            await Exec(context, "INSERT INTO ifx_distinct_doc VALUES (1, 12.50::DECIMAL(9,2)::ifx_money_t)");

            Assert.Equal(12.50m, await Scalar<decimal>(context, "SELECT m::DECIMAL(9,2) FROM ifx_distinct_doc WHERE id = 1"));
            await Assert.ThrowsAnyAsync<Exception>(() => Exec(context, "INSERT INTO ifx_distinct_doc VALUES (2, 7.25)")); // no implicit cast from the base type
            await Exec(context, "DROP TABLE ifx_distinct_doc");
            await Exec(context, "DROP TYPE ifx_money_t RESTRICT");
        });
    }

    [SkippableFact]
    public async Task Lvarchar_HoldsMoreThanVarcharsLimit()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Informix, async context =>
        {
            await TryExec(context, "DROP TABLE ifx_lvarchar_doc");
            await Exec(context, "CREATE TABLE ifx_lvarchar_doc (id INTEGER NOT NULL PRIMARY KEY, t LVARCHAR(4000))");
            var text = new string('x', 3000); // a VARCHAR stops at 255
            await using (var insert = context.CreateSqlContainer("INSERT INTO ifx_lvarchar_doc VALUES (1, "))
            {
                var p = insert.AddParameterWithValue("t", System.Data.DbType.String, text);
                insert.Query.Append(insert.MakeParameterName(p) + ")");
                await insert.ExecuteNonQueryAsync();
            }

            Assert.Equal(3000, await Scalar<int>(context, "SELECT LENGTH(t) FROM ifx_lvarchar_doc WHERE id = 1"));
            await Exec(context, "DROP TABLE ifx_lvarchar_doc");
        });
    }
}
