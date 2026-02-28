// =============================================================================
// FILE: LastUpdatedOnAttribute.cs
// PURPOSE: Marks a property as the "last updated on" timestamp audit field.
//
// AI SUMMARY:
// - Set automatically during CreateAsync AND UpdateAsync to DateTime.UtcNow.
// - Does NOT require AuditValueResolver (timestamp only, not user-based).
// - Both operations set this value for consistent "last modified" queries.
// - Typically a DateTime or DateTimeOffset property.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as the "last updated on" timestamp audit column.
/// </summary>
/// <remarks>
/// <para>
/// This column is automatically set to the current UTC time when an entity is
/// created or updated.
/// </para>
/// <para>
/// <strong>No AuditValueResolver Required:</strong> Unlike <see cref="LastUpdatedByAttribute"/>,
/// this attribute works without an audit resolver (uses <see cref="DateTime.UtcNow"/>).
/// </para>
/// <para>
/// <strong>Behavior:</strong> Set on both CREATE and UPDATE operations.
/// </para>
/// </remarks>
/// <seealso cref="LastUpdatedByAttribute"/>
/// <seealso cref="CreatedOnAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class LastUpdatedOnAttribute : Attribute
{
}