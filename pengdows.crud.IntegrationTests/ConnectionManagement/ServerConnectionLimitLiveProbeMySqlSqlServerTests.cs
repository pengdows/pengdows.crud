using System.Data.Common;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// The MySQL and SQL Server probes run against real servers: the SQL is valid, and the number read
/// matches what the server enforces. MySQL reports max_connections (or a lower per-user limit); a stock
/// SQL Server reports "user connections" as 0, meaning unlimited, which is "unknown" and must leave the
/// pools at the provider default rather than invent a ceiling.
/// </summary>
public class ServerConnectionLimitLiveProbeMySqlSqlServerTests
{
    private const string MySqlImage = "mysql:8.4.11";
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-22.04";
    private const string MySqlPassword = "probe_test_pw";
    private const string SqlServerPassword = "YourPassword123";
    private const int ProviderDefaultPoolSize = 100;

    private readonly ITestOutputHelper _output;

    public ServerConnectionLimitLiveProbeMySqlSqlServerTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(25, 0, 25)]
    [InlineData(100, 15, 15)]
    public async Task MySql_ClampOn_SharesTheLowerOfMaxConnectionsAndTheUserLimitBetweenTheRoles(
        int maxConnections, int maxUserConnections, int expected)
    {
        var container = new ContainerBuilder()
            .WithImage(MySqlImage)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", MySqlPassword)
            .WithEnvironment("MYSQL_DATABASE", "probe")
            .WithPortBinding(0, 3306)
            .WithCommand($"--max-connections={maxConnections}", $"--max-user-connections={maxUserConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3306))
            .Build();
        await container.StartAsync();

        try
        {
            var connectionString =
                $"Server=localhost;Port={container.GetMappedPublicPort(3306)};Database=probe;User ID=root;Password={MySqlPassword};AllowPublicKeyRetrieval=True;SslMode=None;";
            await WaitUntilAcceptingLoginsAsync(MySqlConnectorFactory.Instance, connectionString + "Pooling=false;");

            using var context = new DatabaseContext(NewConfiguration(connectionString, "MySqlConnector"),
                MySqlConnectorFactory.Instance);

            var (reader, writer) = Slots(context);
            _output.WriteLine($"{MySqlImage}: max_connections={maxConnections}, max_user_connections={maxUserConnections}, reader={reader}, writer={writer}");
            // The usable limit is shared between the two role pools: together they fit it exactly.
            Assert.Equal(expected, reader + writer);
            Assert.InRange(reader, 1, expected - 1);
            Assert.InRange(writer, 1, expected - 1);
        }
        finally
        {
            await container.StopAsync();
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task SqlServer_ClampOn_AnUnlimitedServerLeavesThePoolsAtTheProviderDefault()
    {
        var container = new ContainerBuilder()
            .WithImage(SqlServerImage)
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlServerPassword)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithPortBinding(0, 1433)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();
        await container.StartAsync();

        try
        {
            var connectionString =
                $"Server=localhost,{container.GetMappedPublicPort(1433)};uid=sa;pwd={SqlServerPassword};Initial Catalog=master;TrustServerCertificate=true;";
            await WaitUntilAcceptingLoginsAsync(SqlClientFactory.Instance, connectionString + "Pooling=false;Connection Timeout=3;");

            using var context = new DatabaseContext(NewConfiguration(connectionString, "Microsoft.Data.SqlClient"),
                SqlClientFactory.Instance);

            var (reader, writer) = Slots(context);
            _output.WriteLine($"{SqlServerImage}: user connections=0 (unlimited), reader={reader}, writer={writer}");
            Assert.Equal(ProviderDefaultPoolSize, reader);
            Assert.Equal(ProviderDefaultPoolSize, writer);
        }
        finally
        {
            await container.StopAsync();
            await container.DisposeAsync();
        }
    }

    private static DatabaseContextConfiguration NewConfiguration(string connectionString, string providerName) => new()
    {
        ConnectionString = connectionString,
        ProviderName = providerName,
        DbMode = DbMode.Standard,
        EnableMetrics = true,
        ClampPoolsToServerConnectionLimit = true
    };

    private static (int Reader, int Writer) Slots(DatabaseContext context) =>
        (context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots,
            context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);

    // The port opens before the server accepts logins; a failure after the wait is a real failure.
    private static async Task WaitUntilAcceptingLoginsAsync(DbProviderFactory factory, string connectionString)
    {
        Exception? last = null;
        for (var i = 0; i < 120; i++)
        {
            try
            {
                await using var connection = factory.CreateConnection()!;
                connection.ConnectionString = connectionString;
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("The server never accepted a login.", last);
    }
}
