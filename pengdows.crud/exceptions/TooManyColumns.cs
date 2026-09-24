// =============================================================================
// FILE: TooManyColumns.cs
// PURPOSE: Exception when an entity declares more than one of a single-instance column marker.
//
// AI SUMMARY:
// - Thrown when an entity has multiple [Id], [Version], or [CorrelationToken] columns.
// - Each of those markers may appear on at most one property per entity.
// - Validation during TypeMapRegistry entity registration.
// =============================================================================

namespace pengdows.crud.exceptions;

public class TooManyColumns
    : Exception
{
    public TooManyColumns(string message) : base(message)
    {
    }

    public TooManyColumns(string message, Exception innerException) : base(message, innerException)
    {
    }
}