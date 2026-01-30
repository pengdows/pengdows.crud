// =============================================================================
// FILE: InvalidValueException.cs
// PURPOSE: Exception for invalid values during entity mapping or parameter binding.
//
// AI SUMMARY:
// - Thrown when a value cannot be converted or is invalid for the context.
// - Use cases: type mismatches, out-of-range values, null where not allowed.
// - Extends Exception directly with message-only constructor.
// - Commonly thrown during DataReader mapping or parameter creation.
// =============================================================================

namespace pengdows.crud.exceptions;

public class InvalidValueException : Exception
{
    public InvalidValueException(string message) : base(message)
    {
    }
}