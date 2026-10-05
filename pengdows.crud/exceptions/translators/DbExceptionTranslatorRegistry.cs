using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Each database's translator is declared by its dialect (<see cref="DatabaseTraits"/>, REV-039);
/// anything without one (Unknown, combined flags) gets <see cref="FallbackExceptionTranslator"/>.
/// </summary>
internal sealed class DbExceptionTranslatorRegistry : IDbExceptionTranslatorRegistry
{
    public IDbExceptionTranslator Get(SupportedDatabase database)
    {
        return DatabaseTraits.For(database).ExceptionTranslator;
    }
}
