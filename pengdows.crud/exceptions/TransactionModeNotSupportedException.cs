// =============================================================================
// FILE: TransactionModeNotSupportedException.cs
// PURPOSE: Exception for unsupported transaction modes on specific providers.
//
// AI SUMMARY:
// - Thrown when requested transaction mode not supported by database.
// - Extends NotSupportedException (more specific than Exception).
// - Use cases: isolation levels, savepoints, read-only transactions.
// - Example: SQLite doesn't support all isolation levels.
// - Check dialect.SupportsXxx properties before requesting features.
// =============================================================================

namespace pengdows.crud.exceptions;

public class TransactionModeNotSupportedException : NotSupportedException
{
    public TransactionModeNotSupportedException(string message)
        : base(message)
    {
    }
}