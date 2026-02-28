// =============================================================================
// FILE: EnumColumnAttribute.cs
// PURPOSE: Explicitly specifies the enum type for a column property.
//
// AI SUMMARY:
// - Used when the property type is object/generic but stores an enum value.
// - Tells TypeMapRegistry which enum type to use for conversion.
// - Validates at construction that the provided type is actually an enum.
// - Not needed when the property type is already the enum type.
// - Use case: Dynamic/polymorphic properties that might hold different enums.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Explicitly specifies the enum type for a column that stores enum values.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute when the property type doesn't directly indicate the enum type
/// (e.g., when using object or generic types).
/// </para>
/// <para>
/// <strong>Not needed</strong> when the property is already typed as the enum:
/// </para>
/// <code>
/// // No [EnumColumn] needed - type is inferred
/// [Column("status", DbType.String)]
/// public StatusEnum Status { get; set; }
///
/// // [EnumColumn] needed - property type is object
/// [EnumColumn(typeof(StatusEnum))]
/// [Column("status", DbType.String)]
/// public object Status { get; set; }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EnumColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified enum type.
    /// </summary>
    /// <param name="enumType">The enum type this column represents.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="enumType"/> is not an enum.</exception>
    public EnumColumnAttribute(Type enumType)
    {
        if (!enumType.IsEnum)
        {
            throw new ArgumentException("Provided type must be an enum", nameof(enumType));
        }

        EnumType = enumType;
    }

    /// <summary>
    /// Gets the enum type for this column.
    /// </summary>
    public Type EnumType { get; }
}