// =============================================================================
// FILE: AuditValues.cs
// PURPOSE: Concrete implementation of IAuditValues that holds the current
//          timestamp and user identifier for audit column population.
//
// AI SUMMARY:
// - This is the default implementation of IAuditValues, used to pass audit
//   data (timestamp + user ID) from an AuditValueResolver to TableGateway.
// - UtcNow defaults to DateTime.UtcNow but can be overridden for testing.
// - UserId is a required init-only property that holds the user identifier
//   (can be string, int, Guid, or any type matching your audit column type).
// - The As<T>() method provides typed access to UserId without boxing issues.
// - Created by AuditValueResolver.Resolve() implementations.
// =============================================================================

namespace pengdows.crud;

/// <summary>
/// Default implementation of <see cref="IAuditValues"/> that provides audit field values
/// for entity create and update operations.
/// </summary>
/// <remarks>
/// <para>
/// This class is typically created by an <see cref="AuditValueResolver"/> implementation
/// and consumed by <see cref="TableGateway{TEntity,TRowID}"/> to populate audit columns.
/// </para>
/// <para>
/// <strong>Important:</strong> Both CreatedBy/On AND LastUpdatedBy/On are set during
/// entity creation. This allows "last modified" queries without checking if the entity
/// was ever updated after creation.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public override IAuditValues Resolve()
/// {
///     return new AuditValues
///     {
///         UserId = GetCurrentUserId(),
///         UtcNow = DateTime.UtcNow // Optional, defaults to current time
///     };
/// }
/// </code>
/// </example>
/// <seealso cref="IAuditValues"/>
/// <seealso cref="AuditValueResolver"/>
public sealed class AuditValues : IAuditValues
{
    /// <summary>
    /// Gets or sets the UTC timestamp to use for audit columns.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="DateTime.UtcNow"/> at the time of object creation.
    /// Can be overridden for testing or to synchronize timestamps across operations.
    /// </value>
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the user identifier to use for CreatedBy and LastUpdatedBy audit columns.
    /// </summary>
    /// <value>
    /// The user identifier. Type should match the entity's audit column types
    /// (commonly string, int, long, or Guid).
    /// </value>
    public required object UserId { get; init; }

    /// <summary>
    /// Returns the <see cref="UserId"/> cast to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the user identifier.</typeparam>
    /// <returns>The user identifier cast to type <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidCastException">
    /// Thrown if <see cref="UserId"/> cannot be cast to <typeparamref name="T"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// var auditValues = new AuditValues { UserId = 42L };
    /// long userId = auditValues.As&lt;long&gt;(); // Returns 42L
    /// </code>
    /// </example>
    public T As<T>()
    {
        return (T)UserId;
    }
}