// =============================================================================
// FILE: TransactionModeNotSupportedException.cs
// PURPOSE: Exception for unsupported transaction modes on specific providers.
//
// AI SUMMARY:
// - Thrown by IsolationResolver.ResolveForTransaction when a requested IsolationProfile
//   cannot be guaranteed by the database.
// - Extends NotSupportedException (more specific than Exception).
// - Cases: SafeNonBlockingReads on SQL Server with neither snapshot isolation nor READ_COMMITTED_SNAPSHOT, or any profile whose
//   strongest available isolation level is weaker than the profile requires.
// =============================================================================

namespace pengdows.crud.exceptions;

/// <remarks>
/// Obsolete: 3.0 removes this type. An isolation profile the database cannot honor throws
/// <see cref="NotSupportedException"/> there unless the caller passes <c>IsolationResolutionPolicy.AllowLower</c>.
/// This type derives from <see cref="NotSupportedException"/>, so catching that base type works on 2.0.x and 3.0.
/// </remarks>
[Obsolete("Removed in 3.0. Catch NotSupportedException instead; an isolation profile the database cannot honor throws NotSupportedException in 3.0 unless IsolationResolutionPolicy.AllowLower is passed.")]
public class TransactionModeNotSupportedException : NotSupportedException
{
    public TransactionModeNotSupportedException(string message)
        : base(message)
    {
    }
}
