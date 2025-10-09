#region

using System.Data;
using pengdows.crud.enums;

#endregion

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
/// </param>
public readonly record struct IsolationResolution(IsolationProfile Profile, IsolationLevel Level, bool Degraded);
