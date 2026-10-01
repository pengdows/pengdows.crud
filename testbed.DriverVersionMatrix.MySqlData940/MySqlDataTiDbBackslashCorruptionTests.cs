using System.Collections.Generic;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using Xunit;

namespace testbed.DriverVersionMatrix.MySqlData940;

// FEAT-008: sibling of testbed.DriverVersionMatrix.MySqlDataTiDbBackslashCorruptionTests, pinned
// at MySql.Data 9.4.0 -- the midpoint version manually checked when re-verifying
// TiDbDialect.PrepareStatements's workaround. Automates what was previously only a manually
// re-run check, so this specific version's behavior has a real, reproducible regression gate
// instead of a one-time verification recorded only in prose.
public sealed class MySqlDataTiDbBackslashCorruptionTests : IAsyncLifetime
{
    private const int Port = 4000;
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("pingcap/tidb:v8.5.7") // same image tag as testbed/TiDB/TiDBTestContainer.cs
        .WithPortBinding(Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(Port))
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(Port);
        _connectionString =
            $"Server=localhost;Port={hostPort};User=root;Database=test;Pooling=true;MinimumPoolSize=1;MaximumPoolSize=10;ConnectionTimeout=15;";

        // TiDB can accept the port before its SQL layer is actually ready; retry the first real
        // connection attempt the same way testbed's own TiDBTestContainer does.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var probe = new MySqlConnection(_connectionString);
                await probe.OpenAsync();
                return;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(1000);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Prepare_AgainstTiDb_ThrowsKeyNotFoundException_ConfirmingWorkaroundStillNecessary()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE IF NOT EXISTS backslash_probe (id INT PRIMARY KEY, payload TEXT)";
        await create.ExecuteNonQueryAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO backslash_probe (id, payload) VALUES (@id, @payload)";
        insert.Parameters.AddWithValue("@id", 1);
        insert.Parameters.AddWithValue("@payload", "irrelevant — Prepare() fails before this value is ever sent");

        // This is the exact code path TiDbDialect.PrepareStatements normally disables for
        // MySql.Data. Locked down as a regression gate: if a future MySql.Data release fixes
        // TiDB's charset-index handling, this test starts failing here (no exception thrown) —
        // that would be the signal to revisit the PrepareStatements workaround, not something to
        // silently paper over by widening this Assert.Throws.
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
        {
            insert.Prepare();
            return Task.CompletedTask;
        });

        Assert.Contains("0", ex.Message);
    }
}
