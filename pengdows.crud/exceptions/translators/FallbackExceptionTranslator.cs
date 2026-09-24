using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Last-resort translator used when no provider-specific translator is registered.
/// </summary>
/// <remarks>
/// Contract: always produces a <see cref="DatabaseException"/> — never rethrows raw or
/// returns <see langword="null"/>. Returns <see cref="CommandTimeoutException"/> for
/// timeout-shaped exceptions; wraps all others in a <see cref="DatabaseException"/>
/// via <c>DbExceptionTranslationSupport.CreateFallback</c>. No constraint-kind delegation here —
/// this path is only reached for a database with no registered translator at all (e.g.
/// <c>FlatFile</c>), which also has no dialect-level constraint overrides to delegate to.
/// </remarks>
internal sealed class FallbackExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        return DbExceptionTranslationSupport.LooksLikeTimeout(exception)
            ? DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind)
            : DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
