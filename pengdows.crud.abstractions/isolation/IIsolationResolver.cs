#region

using System.Collections.Generic;
using System.Data;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.isolation;

/// <summary>
/// Resolves supported isolation levels and validates requested levels.
/// </summary>
public interface IIsolationResolver
{
    /// <summary>
    /// Maps an <see cref="IsolationProfile"/> to a concrete <see cref="IsolationLevel"/>.
    /// </summary>
    IsolationLevel Resolve(IsolationProfile profile);

    /// <summary>
    /// Validates that the supplied isolation level is supported.
    /// </summary>
    void Validate(IsolationLevel level);

    /// <summary>
    /// Returns the set of isolation levels supported by this resolver.
    /// </summary>
    IReadOnlySet<IsolationLevel> GetSupportedLevels();
}
