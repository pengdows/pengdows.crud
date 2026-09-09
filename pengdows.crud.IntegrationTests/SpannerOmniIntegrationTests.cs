using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Xunit;

namespace pengdows.crud.IntegrationTests;

public sealed class SpannerOmniIntegrationTests : IAsyncLifetime
{
    private const string OmniImage = "us-docker.pkg.dev/spanner-omni/images/spanner-omni:2026.r2.1-beta";
    private const string AdapterImage = "gcr.io/cloud-spanner-pg-adapter/pgadapter:latest";
    private const string Database = "pengdows_test";
    private INetwork? _network;
    private IContainer? _omni;
    private IContainer? _adapter;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().WithName($"pengdows-spanner-{Guid.NewGuid():N}").Build();
        await _network.CreateAsync();
        _omni = new ContainerBuilder().WithImage(OmniImage).WithNetwork(_network)
            .WithNetworkAliases("spanner-omni").WithCommand("start-single-server")
            .WithTmpfsMount("/spanner")
            .WithPortBinding(15000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(15000))
            .Build();
        await _omni.StartAsync();
        var create = await _omni.ExecAsync(new[] { "/google/spanner/bin/spanner", "databases", "create", Database, "--database-dialect", "POSTGRESQL" });
        Assert.Equal(0, create.ExitCode);
        _adapter = new ContainerBuilder().WithImage(AdapterImage).WithNetwork(_network)
            .WithEnvironment("SPANNER_EMULATOR_HOST", "spanner-omni:15000")
            // Project must match what `spanner databases create` actually created the database
            // under — Spanner Omni's own CLI uses "default", not "emulator-project". With
            // autoConfigEmulator=true, a mismatched project makes PGAdapter try to auto-create a
            // "default" instance under the wrong project, which the single-instance emulator
            // rejects (UNIMPLEMENTED: CreateInstance not allowed), and the connection then times
            // out. Verified by reproducing this exact failure and fix against a live container.
            .WithCommand("-p", "default", "-i", "default", "-d", Database, "-c", "\"\"", "-r", "autoConfigEmulator=true", "-e", "spanner-omni:15000", "-s", "5432", "-x")
            .WithPortBinding(5432, true).Build();
        await _adapter.StartAsync();
        // Pooling stays on (pengdows.crud assumes provider-level pooling is always enabled — it's
        // not an optional mode). No Reset On Close=true is the real fix for PGAdapter rejecting
        // Npgsql's default pool-return statement ("SET SESSION AUTHORIZATION DEFAULT;RESET ALL;")
        // with its own "P0001: Invalid SET statement... Expected TO or =." — verified live; see
        // the identical fix and comment on testbed/Spanner/SpannerOmniTestContainer.cs.
        _connectionString = $"Host=localhost;Port={_adapter.GetMappedPublicPort(5432)};Username=postgres;Database={Database};Pooling=true;Timeout=30;CommandTimeout=30;No Reset On Close=true";
        var ready = DateTime.UtcNow.AddSeconds(60);
        Exception? lastError = null;
        while (DateTime.UtcNow < ready)
        {
            try
            {
                await using var probe = new NpgsqlConnection(_connectionString);
                await probe.OpenAsync();
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                lastError = ex;
                await Task.Delay(1000);
            }
        }
        var logs = await _adapter.GetLogsAsync();
        throw new TimeoutException($"PGAdapter did not become ready within 60 seconds. Last error: {lastError?.Message}\nSTDOUT:\n{logs.Stdout}\nSTDERR:\n{logs.Stderr}", lastError);
    }

    public async Task DisposeAsync()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (_omni is not null) await _omni.DisposeAsync();
        if (_network is not null) await _network.DeleteAsync();
    }

    [Fact]
    public async Task PostgreSqlCrud_WorksAgainstSpannerOmni()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE pengdows_items (id INT8 NOT NULL, name VARCHAR(100), PRIMARY KEY (id))";
            await create.ExecuteNonQueryAsync();
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO pengdows_items (id, name) VALUES (@id, @name)";
            insert.Parameters.AddWithValue("id", 7L);
            insert.Parameters.AddWithValue("name", "omni");
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT name FROM pengdows_items WHERE id = @id";
            select.Parameters.AddWithValue("id", 7L);
            Assert.Equal("omni", await select.ExecuteScalarAsync());
        }
        await using var drop = connection.CreateCommand();
        drop.CommandText = "DROP TABLE pengdows_items";
        await drop.ExecuteNonQueryAsync();
    }
}
