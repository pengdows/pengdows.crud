using System;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// Primary SQL-first CRUD gateway for table-mapped entities.
/// </summary>
/// <typeparam name="TEntity">The entity type to operate on.</typeparam>
/// <typeparam name="TRowID">The row ID type.</typeparam>
public class TableGateway<TEntity, TRowID> : EntityHelper<TEntity, TRowID>
    where TEntity : class, new()
{
    public TableGateway(IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
        : base(databaseContext, auditValueResolver, enumParseBehavior, logger)
    {
    }
}
