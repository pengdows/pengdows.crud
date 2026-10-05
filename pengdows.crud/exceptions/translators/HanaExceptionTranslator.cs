using System.Data.Common;
using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

/// <summary>
/// Translates SAP HANA-specific exceptions into the pengdows.crud exception hierarchy.
/// </summary>
/// <remarks>
/// CONFIRMED live against a real Sap.Data.Hana.HanaException thrown by a saplabs/hanaexpress
/// container (see HanaDialect.cs's file-level summary for the full research trail).
/// Critically, HanaException.SqlState is an empty string for every violation kind except unique
/// (which the driver leaves at "23000"), and HanaException.ErrorCode is *always* the generic COM
/// HRESULT -2147467259 regardless of violation kind — this translator does not use either.
/// Constraint-kind classification (Unique/FK/NotNull/Check) is delegated entirely to
/// HanaDialect's IsXxxViolation overrides, which key off HanaException.NativeError (found via
/// DbExceptionTranslationSupport's generic reflection-based error-code extraction — see
/// IDbExceptionTranslator.Translate's own doc comment for why this delegation pattern exists).
/// </remarks>
internal sealed class HanaExceptionTranslator : IDbExceptionTranslator
{
    public DatabaseException Translate(ISqlDialect dialect, Exception exception, DbOperationKind operationKind)
    {
        var database = dialect.DatabaseType;
        var errorCode = DbExceptionTranslationSupport.TryGetErrorCode(exception);
        var sqlState = DbExceptionTranslationSupport.TryGetSqlState(exception);
        var constraintName = DbExceptionTranslationSupport.TryGetConstraintName(exception);
        var message = exception.Message;

        // -10709 "Connection failed": any failed connect (refused, reset, unreachable; confirmed
        // live). HANA's tenant connection limit only resets the socket and surfaces as this too,
        // so it can't be reported as TooManyConnectionsException.
        if (errorCode == -10709)
        {
            return DbExceptionTranslationSupport.CreateConnection(database, exception, operationKind);
        }

        if (exception is DbException dbEx)
        {
            if (dialect.IsUniqueViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.Unique, database, exception, operationKind);
            }

            if (dialect.IsNotNullViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.NotNull, database, exception, operationKind);
            }

            if (dialect.IsCheckConstraintViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.Check, database, exception, operationKind);
            }

            if (dialect.IsForeignKeyViolation(dbEx))
            {
                return DbExceptionTranslationSupport.CreateConstraintViolation(DbConstraintKind.ForeignKey, database, exception, operationKind);
            }
        }

        // Deadlock/Timeout classification is delegated to the dialect's single
        // ClassifyException/TryClassifyProviderException source — see
        // DbExceptionTranslationSupport.TryCreateFromCategory's doc comment.
        if (DbExceptionTranslationSupport.TryCreateFromCategory(
                dialect.ClassifyException(exception), database, exception, operationKind) is { } classified)
        {
            return classified;
        }

        return DbExceptionTranslationSupport.CreateFallback(database, exception, operationKind);
    }
}
