using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using pengdows.crud;

namespace testbed.Spanner;

public sealed class SpannerOmniTestContainer : TestContainer
{
    private const string OmniImage = "us-docker.pkg.dev/spanner-omni/images/spanner-omni:2026.r2.1-beta";
    private const string AdapterImage = "gcr.io/cloud-spanner-pg-adapter/pgadapter:latest";
    private const string Database = "pengdows_test";
    private readonly INetwork _network = new NetworkBuilder().WithName($"pengdows-spanner-{Guid.NewGuid():N}").Build();
    private IContainer? _omni;
    private IContainer? _adapter;
    private string? _connectionString;

    public override async Task StartAsync()
    {
        await _network.CreateAsync();
        _omni = new ContainerBuilder().WithImage(OmniImage).WithNetwork(_network)
            .WithNetworkAliases("spanner-omni").WithCommand("start-single-server")
            .WithTmpfsMount("/spanner").WithPortBinding(15000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(15000)).Build();
        await _omni.StartAsync();
        var create = await _omni.ExecAsync(new[] { "/google/spanner/bin/spanner", "databases", "create", Database, "--database-dialect", "POSTGRESQL" });
        if (create.ExitCode != 0)
        {
            throw new InvalidOperationException($"Spanner Omni database creation failed: {create.Stderr}");
        }

        _adapter = new ContainerBuilder().WithImage(AdapterImage).WithNetwork(_network)
            .WithEnvironment("SPANNER_EMULATOR_HOST", "spanner-omni:15000")
            // Project must match what `spanner databases create` actually created the database
            // under — Spanner Omni's own CLI uses "default", not "emulator-project". With
            // autoConfigEmulator=true, a mismatched project makes PGAdapter try to auto-create a
            // "default" instance under the wrong project, which the single-instance emulator
            // rejects (UNIMPLEMENTED: CreateInstance not allowed), and the connection then times
            // out. Verified by reproducing this exact failure and fix against a live container.
            // startup.sh reconstructs a shell command; the quoted empty token is intentional.
            .WithCommand("-p", "default", "-i", "default", "-d", Database, "-c", "\"\"", "-r", "autoConfigEmulator=true", "-e", "spanner-omni:15000", "-s", "5432", "-x")
            .WithPortBinding(5432, true).Build();
        await _adapter.StartAsync();
        // No Reset On Close=true: Npgsql issues "SET SESSION AUTHORIZATION DEFAULT;RESET ALL;" by
        // default whenever a pooled physical connection is returned, and PGAdapter (verified
        // against a live instance, version 0.55.3) rejects that exact statement with its own
        // "P0001: Invalid SET statement... Expected TO or =." — breaking every subsequent
        // operation on a recycled connection. Real PostgreSQL accepts this fine; this flag is
        // specifically for proxies/adapters, like PGAdapter here, that only implement a subset of
        // PostgreSQL's SQL surface.
        _connectionString = $"Host=localhost;Port={_adapter.GetMappedPublicPort(5432)};Username=postgres;Database={Database};Pooling=true;Timeout=30;CommandTimeout=60;No Reset On Close=true";
        await WaitForDbToStart(NpgsqlFactory.Instance, _connectionString, _adapter, 120);
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null) throw new InvalidOperationException("Container not started yet.");
        return Task.FromResult<IDatabaseContext>(new DatabaseContext(_connectionString, NpgsqlFactory.Instance, new TypeMapRegistry()));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (_omni is not null) await _omni.DisposeAsync();
        await _network.DeleteAsync();
    }
}
