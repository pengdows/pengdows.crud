using System.Data;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.isolation;

/// <summary>
/// Represents the result of resolving an <see cref="IsolationProfile"/> to a concrete
/// <see cref="IsolationLevel"/>, including whether the requested semantics had to be
/// degraded for the current database configuration.
/// </summary>
/// <param name="Profile">The profile that was requested.</param>
/// <param name="Level">The resolved isolation level.</param>
/// <param name="Degraded">
/// True when the resolver could not honor the requested semantics and fell back to a less
/// capable isolation level. Consumers can surface a warning or take alternative action.
/// Equivalent to <c>Kind == IsolationResolutionKind.Lower</c>.
/// </param>
/// <param name="Kind">
/// How <see cref="Level"/> relates to the guarantees <see cref="Profile"/> calls for.
/// </param>
public readonly record struct IsolationResolution(
    IsolationProfile Profile,
    IsolationLevel Level,
    bool Degraded,
    IsolationResolutionKind Kind)
{
    /// <summary>
    /// Constructs a resolution, deriving <see cref="Kind"/> from <paramref name="degraded"/> for
    /// callers that don't yet distinguish an exact match from a strictly-stronger substitute.
    /// </summary>
    public IsolationResolution(IsolationProfile profile, IsolationLevel level, bool degraded)
        : this(profile, level, degraded, degraded ? IsolationResolutionKind.Lower : IsolationResolutionKind.Exact)
    {
    }
}