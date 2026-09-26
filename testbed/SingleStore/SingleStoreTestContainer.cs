#region

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MySqlConnector;
using pengdows.crud;

#endregion

namespace testbed.SingleStore;

/// <summary>
/// SingleStore's own dev image (<c>ghcr.io/singlestore-labs/singlestoredb-dev</c>), not
/// <see cref="testbed.MySQL.MySqlTestContainer"/> pointed at a different image: the startup
/// behavior differs (ported from the 3.0 branch, where it was verified live).
/// <list type="bullet">
///   <item>The root password env var is <c>ROOT_PASSWORD</c>, not MySQL's <c>MYSQL_ROOT_PASSWORD</c>.</item>
///   <item>There is no bootstrap-database env var like MySQL's <c>MYSQL_DATABASE</c>, so this class
///   connects with no default database and creates its per-instance database via SQL.</item>
///   <item>No <c>SINGLESTORE_LICENSE</c> is required: the image self-issues a free developer license
///   and only logs a capacity WARN for its 2-node (master+leaf) topology.</item>
///   <item><c>SELECT @@memsql_version</c> is what <c>DatabaseDetectionService</c> probes to tell
///   SingleStore apart from MySQL (whose <c>VERSION()</c> it imitates).</item>
/// </list>
/// Single pinned image, no per-version matrix (same as Sybase/Informix/Spanner).
/// </summary>
public class SingleStoreTestContainer : TestContainer
{
    private const string Username = "root";
    private const string Password = "rootpassword";
    private const int Port = 3306;
    private readonly IContainer _container;
    // Random per instance so two processes sharing one physical server never see each other's tables.
    private readonly string _database = "crud_test_" + Guid.NewGuid().ToString("N")[..12];
    private string? _connectionString;

    public SingleStoreTestContainer(string? image = null)
    {
        _container = new ContainerBuilder()
            .WithImage(image ?? "ghcr.io/singlestore-labs/singlestoredb-dev:latest")
            .WithEnvironment("ROOT_PASSWORD", Password)
            .WithPortBinding(Port, true)
            .WithExposedPort(Port)
            .Build();
    }

    /// <summary>
    /// The real connection string for this running container (the context's own
    /// <c>ConnectionString</c> is redacted).
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("Container not started yet.");

    public override async Task StartAsync()
    {
        await _container.StartAsync();

        var adminConnectionString = BuildConnectionString(database: null);
        await WaitForDbToStart(MySqlConnectorFactory.Instance, adminConnectionString, _container, 180);

        await using (var conn = new MySqlConnection(adminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE `{_database}`";
            await cmd.ExecuteNonQueryAsync();
        }

        _connectionString = BuildConnectionString(_database);
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

    protected override ValueTask DisposeAsyncCore()
    {
        return _container.DisposeAsync();
    }

    private string BuildConnectionString(string? database)
    {
        var hostPort = _container.GetMappedPublicPort(Port);
        var databasePart = database is null ? string.Empty : $"Database={database};";
        return $"Server=localhost;Port={hostPort};{databasePart}User ID={Username};Password={Password};AllowPublicKeyRetrieval=True;SslMode=None;";
    }
}
