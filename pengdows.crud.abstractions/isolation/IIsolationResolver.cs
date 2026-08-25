using System.Data;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

namespace pengdows.crud.isolation;

/// <summary>
/// Resolves supported isolation levels and validates requested levels.
/// </summary>
public interface IIsolationResolver
{
    /// <summary>
    /// Maps an <see cref="IsolationProfile"/> to a concrete <see cref="IsolationLevel"/> using
    /// <see cref="IsolationResolutionPolicy.AllowHigher"/> — isolation is never silently weakened.
    /// </summary>
    IsolationLevel Resolve(IsolationProfile profile);

    /// <summary>
    /// Maps an <see cref="IsolationProfile"/> to a concrete <see cref="IsolationLevel"/>, permitting
    /// the substitutions <paramref name="policy"/> allows when the database's own ideal mapping
    /// isn't directly available.
    /// </summary>
    IsolationLevel Resolve(IsolationProfile profile, IsolationResolutionPolicy policy);

    /// <summary>
    /// Maps an <see cref="IsolationProfile"/> to a concrete <see cref="IsolationLevel"/> and
    /// surfaces whether the mapping had to be degraded for the current database capabilities.
    /// Uses <see cref="IsolationResolutionPolicy.AllowHigher"/>.
    /// </summary>
    IsolationResolution ResolveWithDetail(IsolationProfile profile);

    /// <summary>
    /// Same as <see cref="ResolveWithDetail(IsolationProfile)"/>, permitting the substitutions
    /// <paramref name="policy"/> allows when the database's own ideal mapping isn't directly
    /// available. Throws <see cref="NotSupportedException"/> if no resolution satisfying
    /// <paramref name="policy"/> exists.
    /// </summary>
    IsolationResolution ResolveWithDetail(IsolationProfile profile, IsolationResolutionPolicy policy);

    /// <summary>
    /// Validates that the supplied isolation level is supported.
    /// </summary>
    void Validate(IsolationLevel level);

    /// <summary>
    /// Returns the set of isolation levels supported by this resolver.
    /// </summary>
    IReadOnlySet<IsolationLevel> GetSupportedLevels();
}