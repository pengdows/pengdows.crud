namespace pengdows.crud.isolation;

/// <summary>
/// The relationship between two <see cref="System.Data.IsolationLevel"/> values, expressed in
/// terms of the <see cref="IsolationGuarantees"/> each provides on a specific database — a partial
/// order, not a numeric ranking of the ADO.NET enum. Two levels with genuinely different
/// trade-offs (e.g. a blocking Serializable vs. a non-blocking Snapshot) are
/// <see cref="Incomparable"/> even though one "sounds" stronger than the other.
/// </summary>
internal enum IsolationLevelComparison
{
    /// <summary>Same level, or different levels with identical guarantees.</summary>
    Exact,

    /// <summary>The candidate provides a strict superset of the requested level's guarantees.</summary>
    Higher,

    /// <summary>The candidate provides a strict subset of the requested level's guarantees.</summary>
    Lower,

    /// <summary>
    /// Neither level's guarantees are a superset of the other's — substituting one for the other
    /// would trade away at least one guarantee the caller asked for, so it must never be treated
    /// as an acceptable "higher" or "lower" substitute.
    /// </summary>
    Incomparable
}
