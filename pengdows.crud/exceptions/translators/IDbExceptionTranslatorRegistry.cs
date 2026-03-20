using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal interface IDbExceptionTranslatorRegistry
{
    IDbExceptionTranslator Get(SupportedDatabase database);
}
