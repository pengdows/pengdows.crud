namespace pengdows.crud.enums;

/// <summary>
/// Controls how <see cref="pengdows.crud.isolation.IIsolationResolver"/> may deviate from a
/// database's ideal mapping for a requested <see cref="IsolationProfile"/> when that ideal isn't
/// directly available. The default used by every no-policy overload is <see cref="AllowHigher"/> —
/// isolation is never silently weakened unless the caller opts in via <see cref="AllowLower"/>.
/// </summary>
[Flags]
public enum IsolationResolutionPolicy
{
    /// <summary>
    /// Only the exact resolved level is acceptable; no substitution is permitted.
    /// </summary>
    ExactOnly = 0,

    /// <summary>
    /// A level that provides strictly stronger guarantees than requested may be substituted.
    /// </summary>
    AllowHigher = 1,

    /// <summary>
    /// A level that provides strictly weaker guarantees than requested may be substituted.
    /// Any resolution that uses this counts as degraded.
    /// </summary>
    AllowLower = 2,

    /// <summary>
    /// Either a stronger or a weaker substitute may be used. When both are available, preference
    /// order is exact, then higher, then lower.
    /// </summary>
    AllowAny = AllowHigher | AllowLower
}
