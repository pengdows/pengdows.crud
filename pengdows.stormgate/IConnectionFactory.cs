using System.Data.Common;

namespace pengdows.stormgate;

/// <summary>
/// Minimal abstraction used by consumers that need opened database connections.
/// </summary>
public interface IConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}
