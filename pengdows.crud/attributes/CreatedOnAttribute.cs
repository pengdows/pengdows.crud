// =============================================================================
// FILE: CreatedOnAttribute.cs
// PURPOSE: Marks a property as the "created on" timestamp audit field.
//
// AI SUMMARY:
// - Set automatically during CreateAsync to DateTime.UtcNow.
// - Does NOT require AuditValueResolver (timestamp only, not user-based).
// - Never modified after initial creation (non-updateable).
// - Typically a DateTime or DateTimeOffset property.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as the "created on" timestamp audit column.
/// </summary>
/// <remarks>
/// <para>
/// This column is automatically set to the current UTC time when an entity is created.
/// </para>
/// <para>
/// <strong>No AuditValueResolver Required:</strong> Unlike <see cref="CreatedByAttribute"/>,
/// this attribute works without an audit resolver (uses <see cref="DateTime.UtcNow"/>).
/// </para>
/// <para>
/// <strong>Behavior:</strong> Set on CREATE, never modified on UPDATE.
/// </para>
/// </remarks>
/// <seealso cref="CreatedByAttribute"/>
/// <seealso cref="LastUpdatedOnAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class CreatedOnAttribute : Attribute
{
}