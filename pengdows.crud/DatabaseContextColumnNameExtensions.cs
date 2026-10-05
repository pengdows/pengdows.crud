// =============================================================================
// FILE: DatabaseContextColumnNameExtensions.cs
// PURPOSE: ColumnName<TEntity>() on IDatabaseContext: any entity's mapped column name for custom
//          SQL, e.g. a joined entity's columns inside another entity's gateway.
// =============================================================================

using pengdows.crud.@internal;

namespace pengdows.crud;

/// <summary>
/// Mapped column names for custom SQL.
/// </summary>
public static class DatabaseContextColumnNameExtensions
{
    /// <summary>
    /// Returns the database column name that <typeparamref name="TEntity"/>'s property
    /// <paramref name="propertyName"/> is mapped to with <c>[Column]</c>. Pass
    /// <c>nameof(TEntity.Property)</c> and wrap the result for your container's context:
    /// <c>sc.WrapObjectName("c." + ctx.ColumnName&lt;Customer&gt;(nameof(Customer.Name)))</c>.
    /// The entity is registered on first use, as a gateway would register it.
    /// </summary>
    /// <param name="context">The context (or transaction) whose type map holds the entity.</param>
    /// <param name="propertyName">The CLR property name (case-sensitive).</param>
    /// <returns>The unquoted column name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is null.</exception>
    /// <exception cref="ArgumentException">The property doesn't exist or has no <c>[Column]</c> mapping.</exception>
    public static string ColumnName<TEntity>(this IDatabaseContext context, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(propertyName);
        var tableInfo = context.GetInternalTypeMapRegistry().GetTableInfo<TEntity>();
        return ColumnNameResolver.Resolve(tableInfo, typeof(TEntity), propertyName);
    }
}
