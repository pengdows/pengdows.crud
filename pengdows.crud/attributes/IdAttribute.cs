// =============================================================================
// FILE: IdAttribute.cs
// PURPOSE: Marks a property as the entity's row identifier (pseudo key).
//
// AI SUMMARY:
// - Designates the surrogate/row ID column (NOT the business key).
// - Required for TableGateway single-ID operations (DeleteAsync, RetrieveOneAsync).
// - [Id] or [Id(true)] = Client provides value (included in INSERT).
// - [Id(false)] = Database generates value (IDENTITY/SERIAL, excluded from INSERT).
// - Only ONE [Id] column per entity (never composite).
// - Different from [PrimaryKey] which is for business keys (can be composite).
// - Both can coexist on DIFFERENT columns.
// =============================================================================

namespace pengdows.crud.attributes;

/// <summary>
/// Marks a property as the entity's row identifier (pseudo key/surrogate key).
/// </summary>
/// <remarks>
/// <para>
/// <strong>CRITICAL DISTINCTION:</strong> [Id] is for the surrogate row identifier,
/// NOT the business key. Use <see cref="PrimaryKeyAttribute"/> for business keys.
/// </para>
/// <para>
/// <strong>Writable vs Non-Writable:</strong>
/// </para>
/// <list type="bullet">
/// <item><description><c>[Id]</c> or <c>[Id(true)]</c> - Client provides the ID value (e.g., Guid)</description></item>
/// <item><description><c>[Id(false)]</c> - Database generates the value (IDENTITY, SERIAL, AUTO_INCREMENT)</description></item>
/// </list>
/// <para>
/// When <c>Writable=false</c>, the column is excluded from INSERT statements.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Auto-increment ID (database generates)
/// [Id(false)]
/// [Column("id", DbType.Int64)]
/// public long Id { get; set; }
///
/// // Client-provided GUID
/// [Id]
/// [Column("id", DbType.Guid)]
/// public Guid Id { get; set; }
/// </code>
/// </example>
/// <seealso cref="PrimaryKeyAttribute"/>
/// <seealso cref="ColumnAttribute"/>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IdAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the specified writability.
    /// </summary>
    /// <param name="writable">
    /// True if the client provides ID values; false if the database generates them.
    /// </param>
    public IdAttribute(bool writable = true)
    {
        Writable = writable;
    }

    /// <summary>
    /// Indicates whether the ID column accepts client-provided values.
    /// </summary>
    /// <value>
    /// <c>true</c> if the ID is included in INSERT statements (client-provided);
    /// <c>false</c> if the database generates the value (IDENTITY/SERIAL).
    /// </value>
    public bool Writable { get; }
}