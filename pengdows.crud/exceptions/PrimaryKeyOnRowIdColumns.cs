// =============================================================================
// FILE: PrimaryKeyOnRowIdColumns.cs
// PURPOSE: Exception for invalid combination of [Id] and [PrimaryKey] attributes.
//
// AI SUMMARY:
// - Thrown when same column has both [Id] and [PrimaryKey] attributes.
// - [Id] (pseudo key/row ID) and [PrimaryKey] (business key) are mutually exclusive.
// - [Id]: surrogate identifier for TableGateway operations.
// - [PrimaryKey]: business uniqueness constraint, can be composite.
// - Both can exist on an entity, but on DIFFERENT columns.
// - Validation occurs during TypeMapRegistry.GetTableInfo<T>() call.
// =============================================================================

namespace pengdows.crud.exceptions;

public class PrimaryKeyOnRowIdColumn
    : Exception
{
    public PrimaryKeyOnRowIdColumn(string message) : base(message)
    {
    }
}