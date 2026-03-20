using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal interface IDbExceptionTranslator
{
    DatabaseException Translate(
        SupportedDatabase database,
        Exception exception,
        DbOperationKind operationKind);
}
