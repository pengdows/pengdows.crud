// =============================================================================
// FILE: TableAttribute.cs
// PURPOSE: Marks a class as an entity mapped to a database table.
//
// AI SUMMARY:
// - Required on all entity classes used with TableGateway/TableGateway.
// - Specifies the database table name and optional schema.
// - TypeMapRegistry reads this to build TableInfo metadata.
// - Example: [Table("orders", "dbo")] maps to dbo.orders
// - Not inherited: Each entity class needs its own [Table] attribute.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a class as an entity mapped to a database table.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is required on all entity classes used with <see cref="TableGateway{TEntity,TRowID}"/>.
/// </para>
/// <para>
/// <strong>Usage:</strong>
/// </para>
/// <code>
/// [Table("users", "dbo")]
/// public class User
/// {
///     [Id]
///     [Column("id", DbType.Int32)]
///     public int Id { get; set; }
/// }
/// </code>
/// </remarks>
/// <seealso cref="ColumnAttribute"/>
/// <seealso cref="IdAttribute"/>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified table name and optional schema.
    /// </summary>
    /// <param name="name">The database table name.</param>
    /// <param name="schema">The database schema (e.g., "dbo", "public"). Null for default schema.</param>
    public TableAttribute(string name, string? schema = null)
    {
        Name = name;
        Schema = schema;
    }

    /// <summary>
    /// Gets the database table name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the database schema, or null for the default schema.
    /// </summary>
    public string? Schema { get; }
}