using System.Data;
using System.Threading.Tasks;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-058, against the real Microsoft.Data.Sqlite: a decimal bound as text has no affinity, and
/// SQLite applies no conversion when neither side of a comparison has one — so text compared with
/// an arithmetic or aggregate result (also no affinity) is always "greater" and never matches.
/// TYPE-002 still requires a decimal a double can't hold to keep every digit in a TEXT column.
/// </summary>
public class SqliteDecimalBindingTests : RealSqliteContextTestBase
{
    private async Task SeedAsync()
    {
        await using var create = Context.CreateSqlContainer(
            "CREATE TABLE t (price DECIMAL(10,2), qty INTEGER, txt TEXT)");
        await create.ExecuteNonQueryAsync();
        await using var insert = Context.CreateSqlContainer("INSERT INTO t (price, qty) VALUES (10.25, 2)");
        await insert.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string predicate, decimal value)
    {
        await using var sc = Context.CreateSqlContainer();
        var p = sc.AddParameterWithValue("p", DbType.Decimal, value);
        sc.Query.Append("SELECT COUNT(*) FROM t WHERE ").Append(predicate.Replace("@p", sc.MakeParameterName(p)));
        return await sc.ExecuteScalarRequiredAsync<long>();
    }

    [Fact]
    public async Task DecimalParameter_ComparedWithArithmetic_Matches()
    {
        await SeedAsync();

        Assert.Equal(1, await CountAsync("price * qty > @p", 20m));
        Assert.Equal(1, await CountAsync("price * qty = @p", 20.5m));
    }

    [Fact]
    public async Task DecimalParameter_ComparedWithAggregate_Matches()
    {
        await SeedAsync();

        await using var sc = Context.CreateSqlContainer();
        var p = sc.AddParameterWithValue("p", DbType.Decimal, 10m);
        sc.Query.Append("SELECT COUNT(*) FROM (SELECT SUM(price) AS s FROM t) WHERE s > ").Append(sc.MakeParameterName(p));

        Assert.Equal(1, await sc.ExecuteScalarRequiredAsync<long>());
    }

    [Fact]
    public async Task DecimalParameter_ComparedWithNumericColumn_Matches()
    {
        await SeedAsync();

        Assert.Equal(1, await CountAsync("price = @p", 10.25m));
    }

    [Fact]
    public async Task DecimalTooPreciseForADouble_KeepsEveryDigitInATextColumn()
    {
        await SeedAsync();
        const decimal wide = -1234567890123456.0123456789m;

        await using (var update = Context.CreateSqlContainer())
        {
            var p = update.AddParameterWithValue("p", DbType.Decimal, wide);
            update.Query.Append("UPDATE t SET txt = ").Append(update.MakeParameterName(p));
            await update.ExecuteNonQueryAsync();
        }

        await using var read = Context.CreateSqlContainer("SELECT txt FROM t");
        Assert.Equal("-1234567890123456.0123456789", await read.ExecuteScalarRequiredAsync<string>());
    }
}
