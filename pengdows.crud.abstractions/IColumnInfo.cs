#region

using System.Data;
using System.Reflection;
using System.Text.Json;

#endregion

namespace pengdows.crud;

/// <summary>
/// Describes metadata and behavior for a table column.
/// </summary>
public interface IColumnInfo
{
    /// <summary>
    /// Column name as declared in the database.
    /// </summary>
    string Name { get; init; }

    /// <summary>
    /// Property associated with this column.
    /// </summary>
    PropertyInfo PropertyInfo { get; init; }

    /// <summary>
    /// Indicates whether this column represents the row identifier (pseudo key)
    /// rather than a business-defined primary key.
    /// </summary>
    bool IsId { get; init; }

    /// <summary>
    /// Database type for the column.
    /// </summary>
    DbType DbType { get; set; }

    /// <summary>
    /// True when the column should not be updated.
    /// </summary>
    bool IsNonUpdateable { get; set; }

    /// <summary>
    /// True when the column should not be inserted.
    /// </summary>
    bool IsNonInsertable { get; set; }

    /// <summary>
    /// Indicates whether the column maps to an enum.
    /// </summary>
    bool IsEnum { get; set; }

    /// <summary>
    /// Enum type used when <see cref="IsEnum"/> is true.
    /// </summary>
    Type? EnumType { get; set; }

    /// <summary>
    /// True when the column stores JSON data.
    /// </summary>
    bool IsJsonType { get; set; }

    /// <summary>
    /// Options used for JSON serialization.
    /// </summary>
    JsonSerializerOptions JsonSerializerOptions { get; set; }

    /// <summary>
    /// Indicates whether the identifier column is writable.
    /// </summary>
    bool IsIdIsWritable { get; set; }

    /// <summary>
    /// True when the column participates in the primary key.
    /// </summary>
    bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Order of the column within a composite primary key.
    /// </summary>
    int PkOrder { get; set; }

    /// <summary>
    /// Indicates whether the column stores optimistic concurrency tokens.
    /// </summary>
    bool IsVersion { get; set; }

    /// <summary>
    /// True when the column captures the creator identifier.
    /// </summary>
    bool IsCreatedBy { get; set; }

    /// <summary>
    /// True when the column captures the creation timestamp.
    /// </summary>
    bool IsCreatedOn { get; set; }

    /// <summary>
    /// True when the column captures the last updater identifier.
    /// </summary>
    bool IsLastUpdatedBy { get; set; }

    /// <summary>
    /// True when the column captures the last update timestamp.
    /// </summary>
    bool IsLastUpdatedOn { get; set; }

    /// <summary>
    /// Ordinal position of the column within the table.
    /// </summary>
    int Ordinal { get; set; }

    /// <summary>
    /// Creates a parameter value from the entity field for use in commands.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="objectToCreate">Entity instance.</param>
    /// <returns>The value to use for a parameter.</returns>
    object? MakeParameterValueFromField<T>(T objectToCreate);
}
