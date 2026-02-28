// =============================================================================
// FILE: NonInsertableAttribute.cs
// PURPOSE: Excludes a column from INSERT statements.
//
// AI SUMMARY:
// - Column is excluded from CreateAsync/BuildCreate INSERT statements.
// - Use for computed columns, database defaults, or trigger-populated values.
// - Different from [Id(false)] which specifically handles identity columns.
// - Column can still be read and updated (unless also [NonUpdateable]).
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Excludes a column from INSERT statements.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute for columns that should not be included in INSERT statements,
/// such as computed columns, columns with database defaults, or trigger-populated values.
/// </para>
/// <para>
/// <strong>Note:</strong> For identity/auto-increment columns, prefer <c>[Id(false)]</c>
/// which also handles the INSERT exclusion plus additional identity-specific behavior.
/// </para>
/// </remarks>
/// <seealso cref="NonUpdateableAttribute"/>
/// <seealso cref="IdAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public class NonInsertableAttribute : Attribute
{
}