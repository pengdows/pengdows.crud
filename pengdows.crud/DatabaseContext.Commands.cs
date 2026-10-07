// =============================================================================
// FILE: DatabaseContext.Commands.cs
// PURPOSE: SqlContainer logging hook for DatabaseContext.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace pengdows.crud;

public partial class DatabaseContext
{
    /// <summary>
    /// Internal helper so TransactionContext can reuse the same logger factory for containers.
    /// </summary>
    // One per context (PERF-031): CreateLogger<T> builds a Logger<T> wrapper, formats its category and
    // takes the factory's lock on every call. Logger<T> forwards to the factory's own logger, so a
    // provider added to the factory later still receives its messages. The factory is set once, during
    // construction.
    private ILogger<ISqlContainer>? _sqlContainerLogger;

    internal ILogger<ISqlContainer> CreateSqlContainerLogger()
    {
        return _sqlContainerLogger ??= _loggerFactory.CreateLogger<ISqlContainer>();
    }

    protected override ILogger<ISqlContainer>? ResolveSqlContainerLogger()
    {
        return CreateSqlContainerLogger();
    }
}