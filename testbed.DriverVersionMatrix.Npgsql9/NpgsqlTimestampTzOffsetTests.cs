using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit;

namespace testbed.DriverVersionMatrix.Npgsql9;

// FEAT-008: sibling of testbed.DriverVersionMatrix.Npgsql5 — see that project's header comment
// for the full rationale. This pins Npgsql 9.0.3, the "6+" side of PostgreSqlDialect.cs's claim
// that Npgsql 6+ requires a DateTimeOffset written to a timestamptz column to have Offset==0.
public sealed class NpgsqlTimestampTzOffsetTests : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("postgres:16.4") // same image tag as testbed/PostgreSQL/PostgreSqlTestContainer.cs
        .WithEnvironment("POSTGRES_PASSWORD", "test")
        .WithPortBinding(5432, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var hostPort = _container.GetMappedPublicPort(5432);
        _connectionString = $"Host=localhost;Port={hostPort};Username=postgres;Password=test;Database=postgres";

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var probe = new NpgsqlConnection(_connectionString);
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
    public async Task WritingUtcOffsetDateTimeOffset_ToTimestampTz_Succeeds()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await CreateTable(conn);

        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await Insert(conn, dto); // must not throw — matches PostgreSqlDialect.PrepareParameterValue's post-conversion shape
    }

    // Locked down as a regression gate, matching this repo's convention for FEAT-008 findings: if
    // a future Npgsql release relaxes this requirement, this test starts failing here (no
    // exception thrown) — the signal to revisit PostgreSqlDialect.PrepareParameterValue's
    // unconditional dto.UtcDateTime conversion, not something to silently widen this assertion
    // around.
    [Fact]
    public async Task WritingNonUtcOffsetDateTimeOffset_ToTimestampTz_ThrowsArgumentException()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await CreateTable(conn);

        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Insert(conn, dto));
        Assert.Contains("Offset", ex.Message, StringComparison.Ordinal);
    }

    private static async Task CreateTable(NpgsqlConnection conn)
    {
        await using var create = conn.CreateCommand();
        create.CommandText = "DROP TABLE IF EXISTS probe_ts; CREATE TABLE probe_ts (id INT PRIMARY KEY, ts TIMESTAMPTZ)";
        await create.ExecuteNonQueryAsync();
    }

    private static async Task Insert(NpgsqlConnection conn, DateTimeOffset value)
    {
        await using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO probe_ts (id, ts) VALUES (1, @p0)";
        insert.Parameters.Add(new NpgsqlParameter("p0", value));
        await insert.ExecuteNonQueryAsync();
    }
}
