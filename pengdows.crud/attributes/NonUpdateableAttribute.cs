// =============================================================================
// FILE: NonUpdateableAttribute.cs
// PURPOSE: Excludes a column from UPDATE statements.
//
// AI SUMMARY:
// - Column is excluded from UpdateAsync/BuildUpdateAsync UPDATE statements.
// - Implicitly applied to CreatedBy and CreatedOn columns (they shouldn't change).
// - Use for immutable fields set at creation time.
// - Column can still be read and inserted (unless also [NonInsertable]).
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Excludes a column from UPDATE statements.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute for columns that should not be modified after initial creation,
/// such as creation timestamps, original values for auditing, or immutable identifiers.
/// </para>
/// <para>
/// <strong>Implicit Usage:</strong> Columns marked with <see cref="CreatedByAttribute"/>
/// or <see cref="CreatedOnAttribute"/> are automatically treated as non-updateable.
/// </para>
/// </remarks>
/// <seealso cref="NonInsertableAttribute"/>
/// <seealso cref="CreatedByAttribute"/>
/// <seealso cref="CreatedOnAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class NonUpdateableAttribute : Attribute
{
}