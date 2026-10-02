using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Found live (CockroachDB 25.1, Npgsql 9): a prepared multi-statement command fails on execute
/// ("34000: unknown portal"), while the same command unprepared runs. pengdows prepares by default
/// on the PostgreSQL family, so a dialect that can't prepare multi-statement commands skips the
/// prepare for them (SqlStatementScanner finds a second statement outside quotes and comments).
/// </summary>
public sealed class MultiStatementPrepareTests
{
    [Theory]
    [InlineData("SELECT 1", false)]
    [InlineData("SELECT 1;", false)]
    [InlineData("SELECT 1;  \n -- trailing comment\n", false)]
    [InlineData("SELECT 1; SELECT 2", true)]
    [InlineData("DROP TABLE IF EXISTS t;DROP TYPE m;CREATE TYPE m AS ENUM ('a','b')", true)]
    [InlineData("SELECT 'a;b'", false)]
    [InlineData("SELECT 'it''s; fine'", false)]
    [InlineData("SELECT \"we;ird\" FROM t", false)]
    [InlineData("SELECT $$ a; b $$", false)]
    [InlineData("SELECT $tag$ a; $x$ b; $tag$", false)]
    [InlineData("SELECT 1 -- ; SELECT 2", false)]
    [InlineData("SELECT /* ; */ 1", false)]
    [InlineData("SELECT /* outer /* nested ; */ still comment ; */ 1", false)]
    [InlineData("SELECT 1; /* c */ ;", false)]
    [InlineData("SELECT $1; SELECT 2", true)]
    public void HasMultipleStatements(string sql, bool expected)
    {
        Assert.Equal(expected, SqlStatementScanner.HasMultipleStatements(sql));
    }

    [Theory]
    [InlineData(SupportedDatabase.CockroachDb, "SELECT 1; SELECT 2", 0)]
    [InlineData(SupportedDatabase.CockroachDb, "SELECT 1", 1)]
    [InlineData(SupportedDatabase.PostgreSql, "SELECT 1; SELECT 2", 1)]
    public async Task ExecuteNonQueryAsync_PreparesUnlessTheDialectCantPrepareMultipleStatements(
        SupportedDatabase product, string sql, int expectedPrepares)
    {
        var factory = new fakeDbFactory(product);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = product });
        var exec = new fakeDbConnection { EmulatedProduct = product };
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = $"Host=x;EmulatedProduct={product}",
            DbMode = DbMode.Standard
        }, factory);

        await using (var sc = context.CreateSqlContainer(sql))
        {
            await sc.ExecuteNonQueryAsync();
        }

        Assert.Equal(expectedPrepares, exec.CreatedCommands.Sum(c => c.PrepareCount));
    }
}
