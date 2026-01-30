// =============================================================================
// FILE: TooManyColumns.cs
// PURPOSE: Exception when entity has more columns than provider supports.
//
// AI SUMMARY:
// - Thrown when entity mapping exceeds database column limits.
// - Database limits: SQL Server 1024 (non-wide) or 30000 (wide tables).
// - Typically indicates design issue or auto-generated entities.
// - Consider splitting into multiple tables or using JSON columns.
// - Validation during TypeMapRegistry entity registration.
// =============================================================================

namespace pengdows.crud.exceptions;

public class TooManyColumns
    : Exception
{
    public TooManyColumns(string message) : base(message)
    {
    }
}