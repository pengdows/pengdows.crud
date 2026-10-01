using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit;

namespace testbed.DriverVersionMatrix.Npgsql5;

// FEAT-008: pinned at Npgsql 5.0.18 (the last 5.x release) — the pre-"6+" side of
// PostgreSqlDialect.cs's PrepareParameterValue comment: "Npgsql 6+ requires DateTimeOffset to be
// UTC when writing to timestamptz." pengdows.crud itself never sends a non-UTC DateTimeOffset to
// Npgsql at all (PrepareParameterValue always converts to dto.UtcDateTime first, on every
// version), so this tests the raw driver directly, the same way OracleGuidDbTypeTests does for
// Oracle's DbType.Guid claim, to confirm the documented reason that conversion exists is actually
// true and actually version-scoped where the comment says it is.
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
        await Insert(conn, dto); // must not throw
    }

    // The claim this row exists to verify: pre-6, Npgsql did NOT enforce Offset==0 the way 6+
    // does — it accepted any offset and correctly converted the value to the equivalent UTC
    // instant, rather than the guard in PostgreSqlDialect.PrepareParameterValue being needed to
    // avoid data corruption on this version. Confirmed both halves live against Postgres 16.4:
    // no exception, and the round-tripped value (compared via Postgres's own "AT TIME ZONE 'UTC'"
    // rather than trusting this same driver's read path) matches the correct UTC instant exactly.
    [Fact]
    public async Task WritingNonUtcOffsetDateTimeOffset_ToTimestampTz_SucceedsAndConvertsCorrectly()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await CreateTable(conn);

        var dto = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));
        await Insert(conn, dto); // must not throw, unlike Npgsql 9.x (see the Npgsql9 sibling project)

        await using var select = conn.CreateCommand();
        select.CommandText = "SELECT ts AT TIME ZONE 'UTC' FROM probe_ts WHERE id = 1";
        var storedUtc = (DateTime)(await select.ExecuteScalarAsync())!;

        Assert.Equal(dto.UtcDateTime, storedUtc);
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
