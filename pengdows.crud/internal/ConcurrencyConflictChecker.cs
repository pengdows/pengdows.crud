// =============================================================================
// FILE: ConcurrencyConflictChecker.cs
// PURPOSE: Shared proactive optimistic-concurrency check used by both
//          TableGateway<TEntity,TRowID> and PrimaryKeyTableGateway<TEntity>
//          when loadOriginal=true. Compares the freshly loaded DB row against
//          the entity the caller is submitting, using [Version] (preferred)
//          or [LastUpdatedOn]/[LastUpdatedBy] (fallback) as the conflict
//          signal, and throws before any UPDATE is sent.
// =============================================================================

using System.Data;
using System.Globalization;
using pengdows.crud.enums;
using pengdows.crud.exceptions;

namespace pengdows.crud.@internal;

internal static class ConcurrencyConflictChecker
{
    public static void EnsureNotStale<TEntity>(ITableInfo tableInfo, TEntity original, TEntity objectToUpdate,
        SupportedDatabase product)
    {
        var versionColumn = tableInfo.Version;
        if (versionColumn != null)
        {
            var dbValue = versionColumn.MakeParameterValueFromField(original);
            var entityValue = versionColumn.MakeParameterValueFromField(objectToUpdate);
            if (!ValuesMatch(dbValue, entityValue, versionColumn.DbType))
            {
                throw new ConcurrencyConflictException(
                    $"Concurrency conflict on {typeof(TEntity).Name}: version mismatch.", product);
            }

            return;
        }

        if (!tableInfo.HasAuditColumns)
        {
            return;
        }

        var auditColumn = tableInfo.LastUpdatedOn ?? tableInfo.LastUpdatedBy;
        if (auditColumn == null)
        {
            return;
        }

        var dbAudit = auditColumn.MakeParameterValueFromField(original);
        var entityAudit = auditColumn.MakeParameterValueFromField(objectToUpdate);
        if (!ValuesMatch(dbAudit, entityAudit, auditColumn.DbType))
        {
            throw new ConcurrencyConflictException(
                $"Concurrency conflict on {typeof(TEntity).Name}: row was modified since it was loaded.", product);
        }
    }

    private static bool ValuesMatch(object? dbValue, object? entityValue, DbType dbType)
    {
        if (dbValue == null || entityValue == null)
        {
            return Equals(dbValue, entityValue);
        }

        switch (dbType)
        {
            case DbType.DateTime:
            case DbType.DateTime2:
                return TypeCoercionHelper.NormalizeDateTime(Convert.ToDateTime(dbValue, CultureInfo.InvariantCulture))
                       == TypeCoercionHelper.NormalizeDateTime(Convert.ToDateTime(entityValue, CultureInfo.InvariantCulture));
            case DbType.DateTimeOffset:
                return ToUtc(dbValue) == ToUtc(entityValue);
            default:
                return Equals(dbValue, entityValue);
        }
    }

    private static DateTime ToUtc(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => TypeCoercionHelper.NormalizeDateTime(dt),
            _ => TypeCoercionHelper.NormalizeDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture))
        };
    }
}
