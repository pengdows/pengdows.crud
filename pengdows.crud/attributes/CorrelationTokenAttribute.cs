// =============================================================================
// FILE: CorrelationTokenAttribute.cs
// PURPOSE: Marks a property as a correlation token for generated ID retrieval.
//
// AI SUMMARY:
// - Designates a column used to uniquely identify a row after insertion.
// - Used by TableGateway when the database doesn't support RETURNING/OUTPUT
//   and session-scoped identity functions are unreliable.
// - TableGateway generates a unique value (Guid or string), inserts it,
//   then performs a secondary lookup to retrieve the generated identity.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as a correlation token for generated ID retrieval.
/// </summary>
/// <remarks>
/// <para>
/// Used as a fallback strategy for retrieving database-generated IDs.
/// The framework will populate this column with a unique token during INSERT,
/// then immediately SELECT the row back using this token.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CorrelationTokenAttribute : Attribute
{
}
