using System.Data;
using System.Reflection;
using System.Text.Json;

namespace pengdows.crud;

/// <summary>
/// Describes metadata and behavior for a table column.
/// </summary>
internal interface IColumnInfo
{
    /// <summary>
    /// Column name as declared in the database.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Property associated with this column.
    /// </summary>
    PropertyInfo PropertyInfo { get; }

    /// <summary>
    /// Indicates whether this column represents the row identifier (pseudo key)
    /// rather than a business-defined primary key.
    /// </summary>
    bool IsId { get; }

    /// <summary>
    /// Database type for the column.
    /// </summary>
    DbType DbType { get; }

    /// <summary>
    /// True when the column should not be updated.
    /// </summary>
    bool IsNonUpdateable { get; }

    /// <summary>
    /// True when the column should not be inserted.
    /// </summary>
    bool IsNonInsertable { get; }

    /// <summary>
    /// Indicates whether the column maps to an enum.
    /// </summary>
    bool IsEnum { get; }

    /// <summary>
    /// Enum type used when <see cref="IsEnum"/> is true.
    /// </summary>
    Type? EnumType { get; }

    /// <summary>
    /// Underlying type of the enum when <see cref="IsEnum"/> is true.
    /// Cached to avoid reflection during mapping.
    /// </summary>
    Type? EnumUnderlyingType { get; }

    /// <summary>
    /// True when the enum should be stored as its string name rather than numeric value.
    /// </summary>
    bool EnumAsString { get; }

    /// <summary>
    /// True when the enum should be stored as its string name rather than numeric value.
    /// </summary>
    bool EnumAsString { get; set; }

    /// <summary>
    /// True when the column stores JSON data.
    /// </summary>
    bool IsJsonType { get; }

    /// <summary>
    /// Options used for JSON serialization.
    /// </summary>
    JsonSerializerOptions JsonSerializerOptions { get; }

    /// <summary>
    /// Indicates whether the identifier column is writable.
    /// </summary>
    bool IsIdWritable { get; }

    /// <summary>
    /// True when the column participates in the primary key.
    /// </summary>
    bool IsPrimaryKey { get; }

    /// <summary>
    /// True when the column is used as a correlation token for identity retrieval.
    /// </summary>
    bool IsCorrelationToken { get; }

    /// <summary>
    /// True when the column is used as a correlation token for identity retrieval.
    /// </summary>
    bool IsCorrelationToken { get; set; }

    /// <summary>
    /// Order of the column within a composite primary key.
    /// </summary>
    int PkOrder { get; }

    /// <summary>
    /// Indicates whether the column stores optimistic concurrency tokens.
    /// </summary>
    bool IsVersion { get; }

    /// <summary>
    /// True when the column captures the creator identifier.
    /// </summary>
    bool IsCreatedBy { get; }

    /// <summary>
    /// True when the column captures the creation timestamp.
    /// </summary>
    bool IsCreatedOn { get; }

    /// <summary>
    /// True when the column captures the last updater identifier.
    /// </summary>
    bool IsLastUpdatedBy { get; }

    /// <summary>
    /// True when the column captures the last update timestamp.
    /// </summary>
    bool IsLastUpdatedOn { get; }

    /// <summary>
    /// Ordinal position of the column within the table.
    /// </summary>
    int Ordinal { get; }

    /// <summary>
    /// Creates a parameter value from the entity field for use in commands.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="objectToCreate">Entity instance.</param>
    /// <returns>The value to use for a parameter.</returns>
    object? MakeParameterValueFromField<T>(T objectToCreate);
}