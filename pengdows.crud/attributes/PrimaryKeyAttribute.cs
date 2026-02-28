// =============================================================================
// FILE: PrimaryKeyAttribute.cs
// PURPOSE: Marks a property as part of the entity's business/primary key.
//
// AI SUMMARY:
// - Designates business key columns (natural key, NOT surrogate row ID).
// - Can be composite: apply to multiple columns with Order parameter.
// - Used for:
//   * UPSERT conflict detection (ON CONFLICT, ON DUPLICATE KEY)
//   * RetrieveOneAsync(TEntity) lookup
//   * Unique constraint enforcement
// - Order parameter defines column sequence in composite keys (1, 2, 3...).
// - NEVER use on the same column as [Id] - they're mutually exclusive concepts.
// - [Id] = row identifier for TableGateway operations.
// - [PrimaryKey] = business uniqueness constraint.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as part of the entity's business/primary key.
/// </summary>
/// <remarks>
/// <para>
/// <strong>CRITICAL DISTINCTION:</strong> [PrimaryKey] is for business keys (natural keys),
/// NOT the surrogate row identifier. Use <see cref="IdAttribute"/> for row IDs.
/// </para>
/// <para>
/// <strong>Composite Keys:</strong> For multi-column business keys, apply [PrimaryKey] to
/// each column with increasing Order values:
/// </para>
/// <code>
/// [PrimaryKey(1)]
/// [Column("order_id", DbType.Int32)]
/// public int OrderId { get; set; }
///
/// [PrimaryKey(2)]
/// [Column("product_id", DbType.Int32)]
/// public int ProductId { get; set; }
/// </code>
/// <para>
/// <strong>Usage:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>UPSERT uses these columns for conflict detection</description></item>
/// <item><description><see cref="ITableGateway{TEntity,TRowID}.RetrieveOneAsync(TEntity,IDatabaseContext)"/> uses these for lookup</description></item>
/// </list>
/// </remarks>
/// <seealso cref="IdAttribute"/>
/// <seealso cref="ColumnAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified order in a composite key.
    /// </summary>
    /// <param name="order">The position of this column in the composite key (1, 2, 3...).</param>
    public PrimaryKeyAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// Initializes a new instance for a single-column primary key.
    /// </summary>
    public PrimaryKeyAttribute()
    {
        Order = 0;
    }

    /// <summary>
    /// Gets the position of this column in a composite primary key.
    /// </summary>
    /// <value>Lower values appear first in the key definition.</value>
    public int Order { get; }
}