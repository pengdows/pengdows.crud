namespace pengdows.crud.enums;

/// <summary>
/// Which savepoint operations a dialect actually supports. More granular than
/// <see cref="pengdows.crud.dialects.ISqlDialect.SupportsSavepoints"/>, which only distinguishes
/// "supports savepoints at all" from "doesn't" — that single flag can't express SQL Server/Sybase
/// (T-SQL's <c>SAVE TRANSACTION</c> has no explicit release statement at all — a savepoint is
/// implicitly valid until superseded or the transaction ends) versus a fully ANSI-compliant
/// engine that supports <c>RELEASE SAVEPOINT</c> too.
/// </summary>
/// <remarks>
/// Additive alongside <c>SupportsSavepoints</c>, not a replacement for it — that member is
/// already published and every existing dialect already overrides it.
/// </remarks>
[Flags]
public enum SavepointCapabilities
{
    /// <summary>Savepoints are not supported at all.</summary>
    None = 0,

    /// <summary>The dialect can create a savepoint (<c>SavepointAsync</c>).</summary>
    Create = 1,

    /// <summary>The dialect can roll back to a savepoint (<c>RollbackToSavepointAsync</c>).</summary>
    Rollback = 2,

    /// <summary>
    /// The dialect can explicitly release a savepoint (<c>ReleaseSavepointAsync</c>) before the
    /// transaction ends. Not universal even among dialects that support Create/Rollback — T-SQL
    /// (SQL Server, Sybase) has no equivalent statement.
    /// </summary>
    Release = 4
}
