// =============================================================================
// FILE: ConnectionFailedException.cs
// PURPOSE: Exception for database connection failures.
//
// AI SUMMARY:
// - Thrown during DatabaseContext initialization when a connection cannot be opened:
//   the initial connect (Phase "InitConnect") or read-only connection validation
//   (Phase "ReadOnlyValidation").
// - Use cases: network issues, invalid credentials, server unavailable.
// - A ConnectionException (DEC-001, 2026-09-30): catch (ConnectionException) / catch
//   (DatabaseException) see a failure at construction exactly like the same failure at runtime.
//   Before, it derived from Exception directly. Constructors, Phase and Role are unchanged.
// - Carries the underlying failure's Database, SqlState, ErrorCode and IsTransient: from a
//   translated inner DatabaseException, or SqlState/IsTransient from a provider DbException.
//   Database is Unknown until detection, which runs after the first connect.
// - Wraps the underlying provider exception as InnerException.
// =============================================================================

using System.Data.Common;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions;

public class ConnectionFailedException : ConnectionException
{
    public ConnectionFailedException(string message)
        : base(message, SupportedDatabase.Unknown)
    {
    }

    public ConnectionFailedException(string message, Exception innerException)
        : base(message,
            (innerException as DatabaseException)?.Database ?? SupportedDatabase.Unknown,
            innerException,
            innerException switch
            {
                DatabaseException db => db.SqlState,
                DbException provider => provider.SqlState,
                _ => null
            },
            (innerException as DatabaseException)?.ErrorCode,
            constraintName: null,
            innerException switch
            {
                DatabaseException db => db.IsTransient,
                DbException provider => provider.IsTransient,
                _ => null
            })
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
