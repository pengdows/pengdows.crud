using System;

namespace pengdows.crud.enums;

/// <summary>
/// Fine-grained savepoint capabilities for a dialect. More granular than
/// <c>ISqlDialect.SupportsSavepoints</c>: some dialects (SQL Server, Sybase, Oracle) can create and
/// roll back to a savepoint but have no explicit release statement at all.
/// </summary>
[Flags]
public enum SavepointCapabilities
{
    None = 0,
    Create = 1,
    Rollback = 2,
    Release = 4
}
