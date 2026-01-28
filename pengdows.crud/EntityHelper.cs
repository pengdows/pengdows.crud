using System;
using Microsoft.Extensions.Logging;
using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// Legacy compatibility shim for <see cref="TableGateway{TEntity, TRowID}"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type to operate on.</typeparam>
/// <typeparam name="TRowID">The row ID type.</typeparam>
/// <remarks>
/// <para><strong>Migration Notice:</strong></para>
/// <para>
/// This type has been renamed to <see cref="TableGateway{TEntity, TRowID}"/>.
/// Update your code to use <c>TableGateway</c> directly. This shim will be
/// removed in a future major version.
/// </para>
/// </remarks>
[Obsolete("EntityHelper has been renamed to TableGateway<TEntity, TRowID>. Update your code to use TableGateway directly.")]
public class EntityHelper<TEntity, TRowID> : TableGateway<TEntity, TRowID>, IEntityHelper<TEntity, TRowID>
    where TEntity : class, new()
{
    /// <inheritdoc cref="TableGateway{TEntity, TRowID}(IDatabaseContext, IAuditValueResolver?, EnumParseFailureMode, ILogger?)"/>
    public EntityHelper(IDatabaseContext databaseContext,
        IAuditValueResolver? auditValueResolver = null,
        EnumParseFailureMode enumParseBehavior = EnumParseFailureMode.Throw,
        ILogger? logger = null)
        : base(databaseContext, auditValueResolver, enumParseBehavior, logger)
    {
    }
}
