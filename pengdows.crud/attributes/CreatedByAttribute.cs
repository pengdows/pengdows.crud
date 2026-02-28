// =============================================================================
// FILE: CreatedByAttribute.cs
// PURPOSE: Marks a property as the "created by" audit field.
//
// AI SUMMARY:
// - Set automatically during CreateAsync to the current user ID.
// - REQUIRES AuditValueResolver to be configured on TableGateway.
// - Never modified after initial creation (non-updateable).
// - User ID comes from IAuditValues.UserId (string, int, Guid, etc.).
// - Throws InvalidOperationException if no resolver and this column exists.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as the "created by" audit column.
/// </summary>
/// <remarks>
/// <para>
/// This column is automatically set to the current user ID when an entity is created.
/// </para>
/// <para>
/// <strong>Requirements:</strong> An <see cref="IAuditValueResolver"/> must be provided
/// to <see cref="TableGateway{TEntity,TRowID}"/> when using this attribute.
/// </para>
/// <para>
/// <strong>Behavior:</strong> Set on CREATE, never modified on UPDATE.
/// </para>
/// </remarks>
/// <seealso cref="CreatedOnAttribute"/>
/// <seealso cref="LastUpdatedByAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class CreatedByAttribute : Attribute
{
}