using System.Globalization;
using System.Text.RegularExpressions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Aurora MySQL and Aurora PostgreSQL connect with the plain MySQL and PostgreSQL drivers, so the
/// product has to be detected from the server (aurora_version). The context must report the Aurora
/// product, use the matching dialect, and gate features on the server's own version.
/// </summary>
[Collection("IntegrationTests")]
public class AuroraIdentificationTests : DatabaseTestBase
{
    public AuroraIdentificationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        IntegrationTestConfiguration.EnabledProviders.Where(p =>
            p is SupportedDatabase.AuroraMySql or SupportedDatabase.AuroraPostgreSql);

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context) =>
        Task.CompletedTask;

    [SkippableFact]
    public async Task Context_ReportsTheAuroraProduct_AndTheServersOwnVersion()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var dialect = (SqlDialect)context.GetDialect();
            Output.WriteLine($"{provider}: product={context.Product}, name={dialect.ProductInfo.ProductName}, " +
                             $"version string='{dialect.ProductInfo.ProductVersion}', parsed={dialect.ProductInfo.ParsedVersion}");

            Assert.Equal(provider, context.Product);
            if (provider == SupportedDatabase.AuroraMySql)
            {
                Assert.IsAssignableFrom<MySqlDialect>(dialect);
            }
            else
            {
                Assert.IsAssignableFrom<PostgreSqlDialect>(dialect);
            }

            var serverVersion = await ServerVersionAsync(provider, context);
            Output.WriteLine($"{provider}: server reports {serverVersion}");
            var parsed = dialect.ProductInfo.ParsedVersion;
            Assert.NotNull(parsed);
            Assert.Equal(serverVersion.Major, parsed!.Major);
            Assert.Equal(serverVersion.Minor, parsed.Minor);
        });
    }

    // The engine's own compatible version: VERSION() on Aurora MySQL, server_version on Aurora PostgreSQL.
    private static async Task<Version> ServerVersionAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var sql = provider == SupportedDatabase.AuroraMySql ? "SELECT VERSION()" : "SHOW server_version";
        await using var sc = context.CreateSqlContainer(sql);
        var text = await sc.ExecuteScalarRequiredAsync<string>();
        var match = Regex.Match(text, @"^\d+(\.\d+){1,2}");
        Assert.True(match.Success, $"unparseable server version '{text}'");
        var parts = match.Value.Split('.').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length == 2 ? new Version(parts[0], parts[1]) : new Version(parts[0], parts[1], parts[2]);
    }
}
