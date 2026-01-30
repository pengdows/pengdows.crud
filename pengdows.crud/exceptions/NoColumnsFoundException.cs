// =============================================================================
// FILE: NoColumnsFoundException.cs
// PURPOSE: Exception when entity type has no mapped columns.
//
// AI SUMMARY:
// - Thrown when TypeMapRegistry cannot find any [Column] attributes on type.
// - Indicates entity is missing required column mappings.
// - Entities must have at least one [Column] attribute for CRUD operations.
// - Validation occurs during GetTableInfo<T>() or EntityHelper creation.
// - Fix: Add [Column] attributes to properties that map to database columns.
// =============================================================================

namespace pengdows.crud.exceptions;

public class NoColumnsFoundException : Exception
{
    public NoColumnsFoundException(string message) : base(message)
    {
    }
}