// =============================================================================
// FILE: TooManyParametersException.cs
// PURPOSE: Exception when SQL statement exceeds provider's parameter limit.
//
// AI SUMMARY:
// - Thrown when adding parameters would exceed database provider limits.
// - MaxAllowed property: the limit that was exceeded (the container's dialect
//   MaxParameterLimit, or the context's MaxParameterLimit).
// - Database limits vary: SQL Server ~2100, PostgreSQL ~32767, etc.
// - Commonly occurs with large IN clauses or batch operations.
// - Suggests batching or using table-valued parameters as alternatives.
// =============================================================================

namespace pengdows.crud.exceptions;

public class TooManyParametersException : Exception
{
    public TooManyParametersException(string? message, int contextMaxParameterLimit) : base(message)
    {
        MaxAllowed = contextMaxParameterLimit;
    }

    public int MaxAllowed { get; private set; }
}