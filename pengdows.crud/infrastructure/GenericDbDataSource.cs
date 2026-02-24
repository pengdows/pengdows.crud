using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace pengdows.crud.infrastructure;

/// <summary>
/// A generic implementation of DbDataSource that wraps a DbProviderFactory.
/// This allows the framework to use the DbDataSource pattern even for providers
/// that do not yet natively support it.
/// </summary>
internal sealed class GenericDbDataSource : DbDataSource
{
    private readonly DbProviderFactory _factory;
    private readonly string _connectionString;

    public GenericDbDataSource(DbProviderFactory factory, string connectionString)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public override string ConnectionString => _connectionString;

    protected override DbConnection CreateDbConnection()
    {
        var conn = _factory.CreateConnection()
                   ?? throw new InvalidOperationException("Provider factory returned null connection.");
        conn.ConnectionString = _connectionString;
        return conn;
    }

    protected override DbCommand CreateDbCommand(string? commandText = null)
    {
        // Base implementation wires this to the data source semantics.
        var cmd = base.CreateDbCommand(commandText);
        return cmd;
    }

    public new DbConnection OpenConnection()
    {
        var conn = CreateConnection();
        conn.Open();
        return conn;
    }

    public new async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        return conn;
    }

    protected override void Dispose(bool disposing) { /* nothing owned */ }

    protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
