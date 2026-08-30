using System.Collections.Generic;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySql.Data.MySqlClient;
using Xunit;

namespace testbed.DriverVersionMatrix;

// FEAT-008: TiDbDialect.cs (pengdows.crud/dialects/TiDbDialect.cs) documents a real,
// empirically-found bug: Oracle's MySql.Data driver's text-protocol prepared-statement path was
// believed to corrupt string parameter values against TiDB via backslash-escaping mismatches.
// pengdows.crud works around this by disabling prepared statements for TiDB unless the driver is
// MySqlConnector (PrepareStatements => _isMySqlConnector). That comment was written "tested at
// 9.3.0" and asked: "Re-verify against a newer MySql.Data release before assuming this workaround
// is still needed; it may already be fixed upstream without a version bump being noticed here."
//
// Re-verified for real against a live TiDB container, across three MySql.Data versions spanning
// nearly two years of releases (9.3.0 — the originally-tested version — through 9.4.0 to 9.7.0,
// the newest available on NuGet as of this check): the actual failure is more fundamental and
// MORE severe than documented, and identical across all three versions. MySqlCommand.Prepare()
// itself throws an unhandled KeyNotFoundException against TiDB before any parameter value is ever
// sent — TiDB's version-handshake reports a character-set ID that MySql.Data's internal charset
// table (MySqlField.SetFieldEncoding) doesn't recognize, so the backslash-escaping corruption
// scenario the workaround was originally written for is never even reached; Prepare() crashes
// outright instead. This confirms — more strongly than the original finding — that
// TiDbDialect.PrepareStatements's existing workaround (skip prepared statements entirely for
// TiDB+MySql.Data) remains necessary, and has been necessary continuously since at least 9.3.0.
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
