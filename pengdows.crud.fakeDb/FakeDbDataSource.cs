#region

using System.Data.Common;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// Fake DbDataSource implementation for testing DatabaseContext with DbDataSource support.
/// Wraps a fakeDbFactory to provide DataSource-based connection creation.
/// </summary>
public sealed class FakeDbDataSource : DbDataSource
{
    private readonly fakeDbFactory _factory;
    private readonly string _connectionString;

    public FakeDbDataSource(string connectionString, SupportedDatabase pretendToBe)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _factory = new fakeDbFactory(pretendToBe);
        // Register the factory so DbProviderFactories.GetFactory() can find it
        RegisterFactory();
    }

    public FakeDbDataSource(string connectionString, fakeDbFactory factory)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        // Register the factory so DbProviderFactories.GetFactory() can find it
        RegisterFactory();
    }

    /// <summary>
    /// Gets the underlying factory used by this data source.
    /// Useful for testing scenarios where the factory is needed.
    /// </summary>
    public fakeDbFactory Factory => _factory;

    public override string ConnectionString => _connectionString;

    protected override DbConnection CreateDbConnection()
    {
        var connection = _factory.CreateConnection();
        connection.ConnectionString = _connectionString;
        return connection;
    }

    private void RegisterFactory()
    {
        // Register the factory so it can be found by DbProviderFactories.GetFactory()
        // Use a unique provider name based on the connection type
        var providerName = "pengdows.crud.fakeDb";
        try
        {
            // Try to register - it may already be registered
            if (DbProviderFactories.GetFactory(providerName) == null)
            {
                DbProviderFactories.RegisterFactory(providerName, _factory);
            }
        }
        catch (ArgumentException)
        {
            // Already registered or registration failed - that's okay for testing
            // The factory will still work for creating connections
        }
    }

    protected override void Dispose(bool disposing)
    {
        // No resources to dispose in fake implementation
        base.Dispose(disposing);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        // No resources to dispose in fake implementation
        return base.DisposeAsyncCore();
    }
}