#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySqlConnector;
using pengdows.crud;

#endregion

namespace testbed.SingleStore;

/// <summary>
/// SingleStore's own dev image (<c>ghcr.io/singlestore-labs/singlestoredb-dev</c>), NOT
/// <see cref="testbed.MySQL.MySqlTestContainer"/> pointed at a different image — confirmed live
/// (this session) that the two images' startup env vars/behavior genuinely differ:
/// <list type="bullet">
///   <item>Root password env var is <c>ROOT_PASSWORD</c>, not MySQL's <c>MYSQL_ROOT_PASSWORD</c>.</item>
///   <item>No bootstrap-database env var equivalent to MySQL's <c>MYSQL_DATABASE</c> — this class
///   connects with no default database and creates its per-instance database via SQL instead,
///   the same way <see cref="testbed.MySQL.MySqlTestContainer"/> creates its own random database
///   on top of its (different) bootstrap database.</item>
///   <item>Confirmed live: no <c>SINGLESTORE_LICENSE</c> env var is required to start — the image
///   self-issues a free "Developer Image edition" license (unlimited expiration, 1 capacity
///   unit) and logs a capacity-exceeded WARN for its own 2-node (master+leaf) topology, but this
///   does not prevent ordinary DDL/DML from working. Do not add a license env var here without a
///   real, live-verified reason to.</item>
///   <item><c>SELECT VERSION()</c> reports a generic MySQL-compatible <c>5.7.32</c> and
///   <c>SELECT @@memsql_version</c> reports the real SingleStore version (<c>9.1.1</c> in the
///   image this was verified against) — this is exactly the discriminator
///   <c>DatabaseDetectionService</c> already probes for (see docs/supported-databases.md's
///   SingleStore note), so no extra detection wiring is needed here.</item>
/// </list>
/// Single pinned image, no per-version matrix (matches the Sybase/Informix/Spanner precedent for
/// "one container class, no <c>AddDocker</c> version fan-out") — SingleStore doesn't publish a
/// versioned image matrix the way MySQL/MariaDB/Postgres do.
/// </summary>
public class SingleStoreTestContainer : TestContainer
{
    private readonly IContainer _container;
    private string? _connectionString;
    // Random per instance so two processes sharing one physical container never see each other's
    // tables, even if they run concurrently (same rationale as MySqlTestContainer's _database).
    private readonly string _database = "crud_test_" + TestContainerReuse.NewSuffix();
    private readonly string _password = "rootpassword";
    private readonly int _port = 3306;
    private const string Username = "root";
    private readonly string _image;

    public SingleStoreTestContainer(string? image = null)
    {
        _image = image ?? "ghcr.io/singlestore-labs/singlestoredb-dev:latest";
        var builder = new ContainerBuilder()
            .WithImage(_image)
            .WithEnvironment("ROOT_PASSWORD", _password)
            .WithPortBinding(_port, true)
            .WithExposedPort(_port);

        if (TestContainerReuse.Enabled)
        {
            builder = builder.WithReuse(true);
        }

        _container = builder.Build();
    }

    public override async Task StartAsync()
    {
        if (TestContainerReuse.Enabled)
        {
            using var _ = await TestContainerReuse.AcquireStartupLockAsync("singlestore-" + _image);
            await _container.StartAsync();
        }
        else
        {
            await _container.StartAsync();
        }

        var hostPort = _container.GetMappedPublicPort(_port);
        // No database specified yet — SingleStore's dev image has no MYSQL_DATABASE-equivalent
        // bootstrap var, so the admin connection targets no default schema.
        var adminConnectionString =
            $@"Server=localhost;Port={hostPort};User ID={Username};Password={_password};AllowPublicKeyRetrieval=True;SslMode=None;";
        await WaitForDbToStart(MySqlConnectorFactory.Instance, adminConnectionString, _container);

        await using (var conn = new MySqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{_database}`";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString =
            $@"Server=localhost;Port={hostPort};Database={_database};User ID={Username};Password={_password};AllowPublicKeyRetrieval=True;SslMode=None;";
    }

    public override Task<IDatabaseContext> GetDatabaseContextAsync(IServiceProvider services)
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Container not started yet.");
        }

        return Task.FromResult<IDatabaseContext>(
            new DatabaseContext(_connectionString, MySqlConnectorFactory.Instance, new TypeMapRegistry()));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionString is not null)
        {
            try
            {
                var hostPort = _container.GetMappedPublicPort(_port);
                var adminConnectionString =
                    $@"Server=localhost;Port={hostPort};User ID={Username};Password={_password};AllowPublicKeyRetrieval=True;SslMode=None;";
                await using var conn = new MySqlConnection(adminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Best-effort: a shared/reused container may already be gone.
            }
        }

        if (!TestContainerReuse.Enabled)
        {
            await _container.DisposeAsync();
        }
    }
}
