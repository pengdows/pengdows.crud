using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// A caller changes a session setting on a pooled connection ("poisons" it), then the connection is returned to the
/// pool. The next checkout must see the setting pengdows.crud requires.
/// </summary>
/// <remarks>
/// The poison has to survive the pool's own reset on return, or the test would pass without pengdows.crud doing
/// anything. Npgsql resets a connection on return unless <c>No Reset On Close</c> is set, so the test sets it. On
/// that pool the server's default (<c>standard_conforming_strings = on</c>) already matches the requirement, so
/// normal (diff) mode sends nothing and leaves the poison in place. Paranoid mode sends the setting on every checkout
/// and repairs it. The test checks both results, which shows the two modes really differ on a live server.
/// </remarks>
[Collection("IntegrationTests")]
public class PoisonedPooledConnectionTests : DatabaseTestBase
{
    private const string PoisonSql = "SET standard_conforming_strings = off";
    private const string ProbeSql = "SHOW standard_conforming_strings";

    public PoisonedPooledConnectionTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        new[] { SupportedDatabase.PostgreSql }
            .Where(p => base.GetSupportedProviders().Contains(p));

    [SkippableFact]
    public async Task PostgreSql_PoisonedPooledConnection_ParanoidRepairsOnNextCheckout_NormalLeavesItPoisoned()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.PostgreSql, async _ =>
        {
            var raw = Fixture.GetRawConnectionString(SupportedDatabase.PostgreSql).TrimEnd(';');

            // Each context gets its own pool (a unique Application Name), so a poisoned connection cannot reach any
            // other test through a shared pool.
            var paranoidConnectionString = raw + ";No Reset On Close=true;Application Name=poison-paranoid-" + Guid.NewGuid().ToString("N");
            var normalConnectionString = raw + ";No Reset On Close=true;Application Name=poison-normal-" + Guid.NewGuid().ToString("N");

            using var paranoid = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = paranoidConnectionString,
                ProviderName = "Npgsql",
                SessionSettingsMode = SessionSettingsMode.Paranoid,
            }, NpgsqlFactory.Instance);

            using var normal = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = normalConnectionString,
                ProviderName = "Npgsql",
            }, NpgsqlFactory.Instance);

            try
            {
                var paranoidValue = await ValueAfterPoisonAndNextCheckoutAsync(paranoid, PoisonSql, ProbeSql, "off");
                Assert.Equal("on", paranoidValue);

                // Documents the trade-off of normal mode: its captured default already matched, so nothing was sent.
                var normalValue = await ValueAfterPoisonAndNextCheckoutAsync(normal, PoisonSql, ProbeSql, "off");
                Assert.Equal("off", normalValue);
            }
            finally
            {
                // Each context's pool is private to its Application Name; release its idle connections.
                NpgsqlConnection.ClearPool(new NpgsqlConnection(paranoidConnectionString));
                NpgsqlConnection.ClearPool(new NpgsqlConnection(normalConnectionString));
            }
        });
    }

    // Poisons one pooled connection, proves the poison took effect on that checkout, then returns the value the next
    // checkout's connection reports for the setting.
    private static async Task<string?> ValueAfterPoisonAndNextCheckoutAsync(DatabaseContext context, string poisonSql,
        string probeSql, string poisonedValue)
    {
        using (var first = context.GetConnection(ExecutionType.Write))
        {
            first.Open();
            await ExecuteAsync(first, poisonSql);
            Assert.Equal(poisonedValue, await ScalarAsync(first, probeSql));
        }

        using var second = context.GetConnection(ExecutionType.Write);
        second.Open();
        return await ScalarAsync(second, probeSql);
    }

    private static async Task ExecuteAsync(IDbConnection connection, string sql)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(IDbConnection connection, string sql)
    {
        await using var command = (DbCommand)connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
