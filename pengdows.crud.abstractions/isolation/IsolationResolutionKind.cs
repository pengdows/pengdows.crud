namespace pengdows.crud.isolation;

/// <summary>
/// Describes how a resolved isolation level relates to the semantic guarantee the requested
/// <see cref="pengdows.crud.enums.IsolationProfile"/> is meant to provide.
/// </summary>
public enum IsolationResolutionKind
{
    /// <summary>
    /// The resolved level provides exactly the guarantees the profile calls for.
    /// </summary>
    Exact,

    /// <summary>
    /// The resolved level provides strictly stronger guarantees than the profile calls for.
    /// </summary>
    Higher,

    /// <summary>
    /// The resolved level falls short of the guarantees the profile calls for. This is always
    /// reported as degraded.
    /// </summary>
    Lower
}
