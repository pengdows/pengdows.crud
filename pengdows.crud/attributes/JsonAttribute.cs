// =============================================================================
// FILE: JsonAttribute.cs
// PURPOSE: Marks a property to be stored as JSON in the database.
//
// AI SUMMARY:
// - The property value is serialized to JSON string on write.
// - The column value is deserialized from JSON on read.
// - Works with complex types (objects, lists, dictionaries).
// - SerializerOptions allows custom JSON serialization settings.
// - Typically used with DbType.String column type.
// - Useful for storing dynamic/flexible data without schema changes.
// =============================================================================

#region

using System.Text.Json;

#endregion

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property to be stored as serialized JSON in the database.
/// </summary>
/// <remarks>
/// <para>
/// Properties marked with this attribute are automatically serialized to JSON
/// when writing to the database and deserialized when reading.
/// </para>
/// <para>
/// <strong>Usage:</strong> Apply to properties of complex types that should be
/// stored as JSON text in a string column.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Json]
/// [Column("metadata", DbType.String)]
/// public Dictionary&lt;string, object&gt; Metadata { get; set; }
///
/// // With custom options:
/// [Json(SerializerOptions = myOptions)]
/// [Column("config", DbType.String)]
/// public MyConfig Config { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public class JsonAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the JSON serializer options for this property.
    /// </summary>
    /// <value>Defaults to <see cref="JsonSerializerOptions.Default"/>.</value>
    public JsonSerializerOptions SerializerOptions { get; set; } = JsonSerializerOptions.Default;
}