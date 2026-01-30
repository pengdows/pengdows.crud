// =============================================================================
// FILE: ColumnInfo.cs
// PURPOSE: Metadata class that describes a single database column and its
//          mapping to an entity property, including type info, audit flags,
//          primary key status, and JSON/enum handling.
//
// AI SUMMARY:
// - This is the runtime representation of a column's metadata, built from
//   entity attributes ([Column], [Id], [PrimaryKey], [Version], etc.).
// - Created by TypeMapRegistry when it first encounters an entity type.
// - Contains all the information needed to:
//   * Generate SQL (column name, DbType, insertable/updateable flags)
//   * Read values from DataReader (FastGetter for compiled property access)
//   * Handle special types (enums stored as string vs numeric, JSON columns)
//   * Identify audit columns (CreatedBy, CreatedOn, LastUpdatedBy, LastUpdatedOn)
//   * Handle optimistic concurrency ([Version] columns)
// - MakeParameterValueFromField() extracts a property value and converts it
//   to the appropriate database format (enum conversion, JSON serialization).
// - Ordinal is the column index from entity reflection order.
// =============================================================================

#region

using System.Data;
using System.Reflection;
using System.Text.Json;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents metadata about a database column and its mapping to an entity property.
/// </summary>
/// <remarks>
/// <para>
/// This class holds all the information needed to map between a .NET property and
/// a database column, including type conversion, audit tracking, and SQL generation hints.
/// </para>
/// <para>
/// <strong>Creation:</strong> Instances are created automatically by
/// <see cref="TypeMapRegistry"/> when an entity type is first accessed.
/// </para>
/// <para>
/// <strong>Key vs Id:</strong> Remember that <see cref="IsId"/> marks the pseudo key
/// (surrogate/row identifier), while <see cref="IsPrimaryKey"/> marks business key columns.
/// Both can coexist on different columns of the same entity.
/// </para>
/// </remarks>
/// <seealso cref="IColumnInfo"/>
/// <seealso cref="TableInfo"/>
/// <seealso cref="TypeMapRegistry"/>
public class ColumnInfo : IColumnInfo
{
    /// <summary>
    /// Gets or sets a compiled delegate for fast property value retrieval.
    /// </summary>
    /// <remarks>
    /// When set, this provides significantly faster property access than reflection.
    /// Falls back to <see cref="PropertyInfo"/>.GetValue() if null.
    /// </remarks>
    public Func<object, object?>? FastGetter { get; set; }

    /// <summary>
    /// Gets or sets the enum type if this column represents an enum property.
    /// </summary>
    /// <value>The enum <see cref="Type"/>, or null if this is not an enum column.</value>
    public Type? EnumType { get; set; }

    /// <summary>
    /// Gets the database column name as specified in the <see cref="Attributes.ColumnAttribute"/>.
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// Gets the <see cref="PropertyInfo"/> for the mapped entity property.
    /// </summary>
    public PropertyInfo PropertyInfo { get; init; } = null!;

    /// <summary>
    /// Gets a value indicating whether this column is the entity's row identifier (pseudo key).
    /// </summary>
    /// <remarks>
    /// Marked with <see cref="Attributes.IdAttribute"/>. Used by TableGateway for
    /// single-entity operations like RetrieveOneAsync(TRowID) and DeleteAsync(TRowID).
    /// </remarks>
    public bool IsId { get; init; } = false;

    /// <summary>
    /// Gets or sets the ADO.NET <see cref="System.Data.DbType"/> for parameter creation.
    /// </summary>
    public DbType DbType { get; set; }

    /// <summary>
    /// Gets or sets whether this column is excluded from UPDATE statements.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="Attributes.NonUpdateableAttribute"/> or implicitly for
    /// CreatedBy/CreatedOn audit columns.
    /// </remarks>
    public bool IsNonUpdateable { get; set; }

    /// <summary>
    /// Gets or sets whether this column is excluded from INSERT statements.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="Attributes.NonInsertableAttribute"/> or implicitly for
    /// auto-increment ID columns (<c>[Id(false)]</c>).
    /// </remarks>
    public bool IsNonInsertable { get; set; }

    /// <summary>
    /// Gets or sets whether this column maps to an enum property.
    /// </summary>
    public bool IsEnum { get; set; }

    /// <summary>
    /// Gets or sets whether this column should be serialized as JSON.
    /// </summary>
    /// <remarks>Marked with <see cref="Attributes.JsonAttribute"/>.</remarks>
    public bool IsJsonType { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options for JSON columns.
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = JsonSerializerOptions.Default;

    /// <summary>
    /// Gets or sets whether the ID column accepts client-provided values.
    /// </summary>
    /// <remarks>
    /// True for <c>[Id]</c> or <c>[Id(true)]</c>. False for <c>[Id(false)]</c>
    /// (database-generated like IDENTITY/SERIAL).
    /// </remarks>
    public bool IsIdIsWritable { get; set; }

    /// <summary>
    /// Gets or sets whether this column is part of the entity's business/primary key.
    /// </summary>
    /// <remarks>
    /// Marked with <see cref="Attributes.PrimaryKeyAttribute"/>. Can be composite
    /// (multiple columns). Used for upsert conflict detection and RetrieveOneAsync(TEntity).
    /// </remarks>
    public bool IsPrimaryKey { get; set; } = false;

    /// <summary>
    /// Gets or sets the ordinal position in a composite primary key (1-based).
    /// </summary>
    public int PkOrder { get; set; }

    /// <summary>
    /// Gets or sets whether this is an optimistic concurrency version column.
    /// </summary>
    /// <remarks>
    /// Marked with <see cref="Attributes.VersionAttribute"/>. Auto-incremented on update,
    /// included in WHERE clause for concurrency checking.
    /// </remarks>
    public bool IsVersion { get; set; }

    /// <summary>
    /// Gets or sets whether this is a CreatedBy audit column.
    /// </summary>
    public bool IsCreatedBy { get; set; }

    /// <summary>
    /// Gets or sets whether this is a CreatedOn audit column.
    /// </summary>
    public bool IsCreatedOn { get; set; }

    /// <summary>
    /// Gets or sets whether this is a LastUpdatedBy audit column.
    /// </summary>
    public bool IsLastUpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets whether this is a LastUpdatedOn audit column.
    /// </summary>
    public bool IsLastUpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the zero-based ordinal index of this column in the entity's column list.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// Gets or sets the underlying type of the enum (e.g., int, byte, long).
    /// </summary>
    public Type? EnumUnderlyingType { get; set; }

    /// <summary>
    /// Gets or sets whether the enum should be stored as its string name rather than numeric value.
    /// </summary>
    /// <remarks>
    /// Determined by whether <see cref="DbType"/> is <see cref="System.Data.DbType.String"/>.
    /// </remarks>
    public bool EnumAsString { get; set; }

    /// <summary>
    /// Extracts the property value from an entity and converts it to a database-compatible format.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="objectToCreate">The entity instance to extract the value from.</param>
    /// <returns>
    /// The property value converted for database storage. For enums, returns either the
    /// string name or numeric value. For JSON columns, returns serialized JSON string.
    /// </returns>
    /// <remarks>
    /// This method handles:
    /// <list type="bullet">
    /// <item><description>Enum-to-string conversion when <see cref="EnumAsString"/> is true</description></item>
    /// <item><description>Enum-to-underlying-type conversion when stored as numeric</description></item>
    /// <item><description>JSON serialization for <see cref="IsJsonType"/> columns</description></item>
    /// </list>
    /// </remarks>
    public object? MakeParameterValueFromField<T>(T objectToCreate)
    {
        var value = FastGetter != null
            ? FastGetter(objectToCreate!)
            : PropertyInfo.GetValue(objectToCreate);
        var current = value;

        if (current != null)
        {
            if (EnumType != null)
            {
                if (DbType == DbType.String)
                {
                    value = current.ToString(); // Save enum as string name
                }
                else
                {
                    // Use cached underlying type, or determine it if not cached
                    var underlyingType = EnumUnderlyingType ?? Enum.GetUnderlyingType(EnumType);
                    value = Convert.ChangeType(current, underlyingType);
                }
            }

            if (IsJsonType)
            {
                var options = JsonSerializerOptions ?? JsonSerializerOptions.Default;
                value = TypeCoercionHelper.GetJsonText(current, options);
            }
        }

        return value;
    }
}