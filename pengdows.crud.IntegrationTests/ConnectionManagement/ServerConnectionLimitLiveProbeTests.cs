using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.ConnectionManagement;

/// <summary>
/// The probe queries are unit-tested against fake answers; this runs them against real PostgreSQL
/// servers with a small max_connections to confirm the SQL is valid and the arithmetic matches what
/// the server enforces. PostgreSQL 15 has no reserved_connections (the query errors and the
/// documented default of 0 applies); 17 does.
/// </summary>
public class ServerConnectionLimitLiveProbeTests
{
    private const int ServerMaxConnections = 25;
    private const int SuperuserReserved = 3;
    private const string Password = "probe_test_pw";

    private readonly ITestOutputHelper _output;

    public ServerConnectionLimitLiveProbeTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("postgres:15-alpine")]
    [InlineData("postgres:17-alpine")]
    public async Task ClampOn_SizesEveryRoleToWhatTheRealServerAllows(string image)
    {
        var container = new ContainerBuilder()
            .WithImage(image)
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEnvironment("POSTGRES_DB", "probe")
            .WithPortBinding(0, 5432)
            .WithCommand("-c", $"max_connections={ServerMaxConnections}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
        await container.StartAsync();

        try
        {
            var connectionString =
                $"Host=localhost;Port={container.GetMappedPublicPort(5432)};Database=probe;Username=postgres;Password={Password}";

            // The server needs a moment after the port opens before it accepts logins.
            for (var i = 0; i < 60; i++)
            {
                try
                {
                    await using var probe = new NpgsqlConnection(connectionString + ";Pooling=false");
                    await probe.OpenAsync();
                    break;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }

            using var context = new DatabaseContext(new DatabaseContextConfiguration
            {
                ConnectionString = connectionString,
                ProviderName = "Npgsql",
                DbMode = DbMode.Standard,
                EnableMetrics = true,
                ClampPoolsToServerConnectionLimit = true
            }, NpgsqlFactory.Instance);

            var reader = context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots;
            var writer = context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots;
            _output.WriteLine($"{image}: server max_connections={ServerMaxConnections}, reader slots={reader}, writer slots={writer}");

            Assert.Equal(ServerMaxConnections - SuperuserReserved, reader);
            Assert.Equal(ServerMaxConnections - SuperuserReserved, writer);
        }
        finally
        {
            await container.StopAsync();
            await container.DisposeAsync();
        }
    }
}
