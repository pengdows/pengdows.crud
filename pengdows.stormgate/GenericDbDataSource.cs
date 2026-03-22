using System.Data.Common;

namespace pengdows.stormgate;

/// <summary>
/// Generic DbDataSource adapter over DbProviderFactory.
/// Used when the provider does not expose a native DbDataSource.
/// </summary>
internal sealed class GenericDbDataSource : DbDataSource
{
    private readonly DbProviderFactory _factory;
    private readonly string _connectionString;

    public GenericDbDataSource(DbProviderFactory factory, string connectionString)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public override string ConnectionString => _connectionString;

    protected override DbConnection CreateDbConnection()
    {
        var connection = _factory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"Provider factory '{_factory.GetType().FullName}' returned null from CreateConnection().");

        connection.ConnectionString = _connectionString;
        return connection;
    }

    protected override void Dispose(bool disposing)
    {
        // _factory is not disposed here. DbProviderFactory instances are expected to be singletons
        // (e.g. SqlClientFactory.Instance, NpgsqlFactory.Instance) and must outlive this data source.
        // If a caller passes a factory with a shorter lifetime, the caller is responsible for disposing
        // it after this data source has been disposed.
    }

    protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
