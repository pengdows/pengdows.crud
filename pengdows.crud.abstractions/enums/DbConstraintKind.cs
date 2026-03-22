namespace pengdows.crud.enums;

/// <summary>
/// Describes the specific kind of database constraint involved in a failure.
/// </summary>
public enum DbConstraintKind
{
    /// <summary>No constraint information is available.</summary>
    None = 0,

    /// <summary>A unique or primary-key constraint was violated.</summary>
    Unique = 1,

    /// <summary>A foreign-key constraint was violated.</summary>
    ForeignKey = 2,

    /// <summary>A not-null constraint was violated.</summary>
    NotNull = 3,

    /// <summary>A check constraint was violated.</summary>
    Check = 4,

    /// <summary>A constraint violation occurred, but the exact kind could not be determined.</summary>
    Unknown = 99
}
