// =============================================================================
// FILE: VersionAttribute.cs
// PURPOSE: Marks a property for optimistic concurrency control.
//
// AI SUMMARY:
// - The version column is automatically incremented on each UPDATE.
// - UPDATE includes WHERE version = @currentVersion for conflict detection.
// - If UPDATE returns 0 rows, another process modified the row (conflict).
// - Typically used with an int or long property; byte[] and RowVersion are also accepted.
//   byte[] and RowVersion columns are database-maintained (TableGateway does not increment them).
// - On CREATE: If null/0, automatically set to 1 (numeric versions).
// - On UPDATE: SET version = version + 1 (numeric versions only).
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property for optimistic concurrency control.
/// </summary>
/// <remarks>
/// <para>
/// The version column enables optimistic locking by tracking row modifications.
/// </para>
/// <para>
/// <strong>Behavior:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>On CREATE: Set to 1 if null or zero (numeric versions)</description></item>
/// <item><description>On UPDATE: Incremented by 1 in SET clause (numeric versions; <c>byte[]</c> and
/// <c>RowVersion</c> are database-maintained and only compared)</description></item>
/// <item><description>UPDATE WHERE clause includes version check</description></item>
/// </list>
/// <para>
/// <strong>Conflict Detection:</strong> If UpdateAsync returns 0 rows affected,
/// another process modified the row (version mismatch).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Version]
/// [Column("version", DbType.Int32)]
/// public int Version { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VersionAttribute : Attribute
{
}