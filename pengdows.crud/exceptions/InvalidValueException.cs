// =============================================================================
// FILE: InvalidValueException.cs
// PURPOSE: Exception for invalid values during entity mapping or parameter binding.
//
// AI SUMMARY:
// - Thrown when a value cannot be converted or is invalid for the context.
// - Use cases: type mismatches, out-of-range values, null where not allowed.
// - Extends Exception directly; message and message+inner-exception constructors.
// - Thrown during DataReader mapping (compiled mapper setters, GUID-from-binary coercion).
// =============================================================================

namespace pengdows.crud.exceptions;

public class InvalidValueException : Exception
{
    public InvalidValueException(string message) : base(message)
    {
    }

    public InvalidValueException(string message, Exception innerException) : base(message, innerException)
    {
    }
}