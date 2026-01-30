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
    internal ILogger<ISqlContainer> CreateSqlContainerLogger()
    {
        return _loggerFactory.CreateLogger<ISqlContainer>();
    }

    protected override ILogger<ISqlContainer>? ResolveSqlContainerLogger()
    {
        return CreateSqlContainerLogger();
    }
}
