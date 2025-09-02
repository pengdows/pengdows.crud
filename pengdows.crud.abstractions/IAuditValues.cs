#region

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents immutable audit metadata for database operations.
/// </summary>
public interface IAuditValues
{
    /// <summary>
    /// Identifier of the current user associated with the audit entry.
    /// </summary>
    object UserId { get; init; }

    /// <summary>
    /// Timestamp, in UTC, when the audit entry was generated.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Returns the <see cref="UserId"/> cast to the specified type.
    /// </summary>
    /// <typeparam name="T">Type to cast the identifier to.</typeparam>
    /// <returns>The user identifier as the requested type.</returns>
    T As<T>()
    {
        return (T)UserId;
    }
}
