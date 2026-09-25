// =============================================================================
// FILE: TransactionModeNotSupportedException.cs
// PURPOSE: Exception for unsupported transaction modes on specific providers.
//
// AI SUMMARY:
// - Thrown by IsolationResolver.ResolveForTransaction when a requested IsolationProfile
//   cannot be guaranteed by the database.
// - Extends NotSupportedException (more specific than Exception).
// - Cases: SafeNonBlockingReads on PostgreSQL/YugabyteDB, or any profile whose strongest
//   available isolation level is weaker than the profile requires.
// =============================================================================

namespace pengdows.crud.exceptions;

public class TransactionModeNotSupportedException : NotSupportedException
{
    public TransactionModeNotSupportedException(string message)
        : base(message)
    {
    }
}
