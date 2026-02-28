// =============================================================================
// FILE: ColumnAttribute.cs
// PURPOSE: Maps an entity property to a database column.
//
// AI SUMMARY:
// - Required on properties that correspond to database columns.
// - Specifies column name, DbType, and optional ordinal for ordering.
// - DbType is used for parameter creation and type coercion.
// - For enums: DbType.String stores as name, numeric DbType stores as value.
// - Ordinal can override default property order (useful for large entities).
// - Properties without [Column] are ignored by TableGateway.
// =============================================================================

#region

using System.Data;

#endregion

namespace pengdows.crud.attributes;

/// <summary>
/// Maps an entity property to a database column.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to properties that should be persisted to the database.
/// Properties without this attribute are ignored by <see cref="TableGateway{TEntity,TRowID}"/>.
/// </para>
/// <para>
/// <strong>DbType Selection:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>Use <see cref="DbType.String"/> for enums stored as their name</description></item>
/// <item><description>Use numeric DbType (Int32, etc.) for enums stored as their value</description></item>
/// <item><description>Use <see cref="DbType.Guid"/> for Guid columns</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// [Column("email", DbType.String)]
/// public string Email { get; set; }
///
/// [Column("status", DbType.String)]  // Stored as "Active", "Inactive"
/// public StatusEnum Status { get; set; }
/// </code>
/// </example>
/// <seealso cref="TableAttribute"/>
/// <seealso cref="IdAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified column name and type.
    /// </summary>
    /// <param name="name">The database column name.</param>
    /// <param name="type">The ADO.NET <see cref="DbType"/> for this column.</param>
    /// <param name="ordinal">Optional ordinal to control column ordering (default 0).</param>
    public ColumnAttribute(string name, DbType type, int ordinal = 0)
    {
        Name = name;
        Type = type;
        Ordinal = ordinal;
    }

    /// <summary>
    /// Gets the database column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the ADO.NET <see cref="DbType"/> used for parameter creation.
    /// </summary>
    public DbType Type { get; }

    /// <summary>
    /// Gets or sets the ordinal position for column ordering.
    /// </summary>
    /// <remarks>
    /// Lower values appear first in SELECT statements and parameter lists.
    /// </remarks>
    public int Ordinal { get; set; }
}