// =============================================================================
// FILE: ConnectionFailedException.cs
// PURPOSE: Exception for database connection failures.
//
// AI SUMMARY:
// - Thrown when database connection cannot be established.
// - Use cases: network issues, invalid credentials, server unavailable.
// - Extends Exception directly with message-only constructor.
// - Typically wraps underlying provider exceptions with context.
// - Connection strategies may throw this after retry attempts exhausted.
// =============================================================================

namespace pengdows.crud.exceptions;

public class ConnectionFailedException : Exception
{
    public ConnectionFailedException(string message) : base(message)
    {
    }

    public ConnectionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialization phase where the failure occurred (e.g., "InitConnect", "ReadOnlyValidation").
    /// </summary>
    public string? Phase { get; init; }

    /// <summary>
    /// Connection role that failed (e.g., "ReadWrite", "ReadOnly").
    /// </summary>
    public string? Role { get; init; }
}