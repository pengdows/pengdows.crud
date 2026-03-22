using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Last-resort translator used when no provider-specific translator is registered.
/// </summary>
/// <remarks>
/// Contract: always produces a <see cref="DatabaseException"/> — never rethrows raw or
/// returns <see langword="null"/>. Returns <see cref="CommandTimeoutException"/> for
/// timeout-shaped exceptions; wraps all others in a <see cref="DatabaseException"/>
/// via <c>DbExceptionTranslationSupport.CreateFallback</c>.
/// </remarks>
internal sealed class FallbackExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(SupportedDatabase database, Exception exception, DbOperationKind operationKind)
    {
        return DbExceptionTranslationSupport.LooksLikeTimeout(exception)
            ? DbExceptionTranslationSupport.CreateTimeout(database, exception, operationKind)
            : DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
