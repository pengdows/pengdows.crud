// =============================================================================
// FILE: LastUpdatedByAttribute.cs
// PURPOSE: Marks a property as the "last updated by" audit field.
//
// AI SUMMARY:
// - Set automatically during CreateAsync AND UpdateAsync to current user ID.
// - REQUIRES AuditValueResolver to be configured on TableGateway.
// - Both operations set this value (allows "last modified" queries on new rows).
// - User ID comes from IAuditValues.UserId.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as the "last updated by" audit column.
/// </summary>
/// <remarks>
/// <para>
/// This column is automatically set to the current user ID when an entity is
/// created or updated.
/// </para>
/// <para>
/// <strong>Requirements:</strong> An <see cref="IAuditValueResolver"/> must be provided
/// to <see cref="TableGateway{TEntity,TRowID}"/> when using this attribute.
/// </para>
/// <para>
/// <strong>Behavior:</strong> Set on both CREATE and UPDATE operations. This allows
/// "last modified" queries to work without checking if the entity was ever updated.
/// </para>
/// </remarks>
/// <seealso cref="LastUpdatedOnAttribute"/>
/// <seealso cref="CreatedByAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class LastUpdatedByAttribute : Attribute
{
}