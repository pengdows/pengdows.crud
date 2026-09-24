// =============================================================================
// FILE: ConnectionFailedException.cs
// PURPOSE: Exception for database connection failures.
//
// AI SUMMARY:
// - Thrown during DatabaseContext initialization when a connection cannot be opened:
//   the initial connect (Phase "InitConnect") or read-only connection validation
//   (Phase "ReadOnlyValidation").
// - Use cases: network issues, invalid credentials, server unavailable.
// - Extends Exception directly; message and message+inner-exception constructors.
// - Wraps the underlying provider exception as InnerException.
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