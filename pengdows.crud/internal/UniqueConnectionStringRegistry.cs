// =============================================================================
// FILE: UniqueConnectionStringRegistry.cs
// PURPOSE: Process-wide opt-in guard against two live DatabaseContext instances
//          sharing the same connection string (see IDatabaseContextConfiguration.
//          EnforceUniqueConnectionString).
//
// AI SUMMARY:
// - Two DatabaseContexts on the same connection string run independent
//   PoolGovernor admission control, so their combined admitted connections can
//   exceed what the underlying provider pool was sized for.
// - ClaimAll/ReleaseAll are keyed by DatabaseContext.ComputePoolKeyHash output
//   (provider + redacted connection string), computed against the RAW
//   caller-supplied connection string(s), not the internally-decorated
//   reader/writer variants.
// - ClaimAll is all-or-nothing: if any key in the batch is already claimed by
//   another context, every key this call already claimed is rolled back
//   before throwing, so a failed construction never permanently blocks a
//   connection string for anyone else.
// - Opt-in only: enabling/disabling EnforceUniqueConnectionString never changes whether
//   a duplicate is *rejected* — that stays gated by the flag via ClaimAll/Claims below.
//   RegisterAllForWarning/Registrations is a separate, always-on, non-throwing table used
//   only to log a diagnostic warning so the misconfiguration isn't silent by default; it
//   never blocks construction and default behavior (and every existing enforcement test)
//   is unaffected.
// =============================================================================

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace pengdows.crud.@internal;

internal static class UniqueConnectionStringRegistry
{
    private static readonly ConcurrentDictionary<string, DatabaseContext> Claims = new();
    private static readonly ConcurrentDictionary<string, DatabaseContext> Registrations = new();

    /// <summary>
    /// Atomically claims every key in <paramref name="keys"/> for <paramref name="owner"/>.
    /// Throws <see cref="InvalidOperationException"/> if any key is already claimed by a
    /// different, still-live context — rolling back any keys this call already claimed first.
    /// </summary>
    internal static IReadOnlyList<string> ClaimAll(DatabaseContext owner, IReadOnlyList<string> keys)
    {
        var claimed = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!Claims.TryAdd(key, owner))
            {
                foreach (var alreadyClaimed in claimed)
                {
                    Claims.TryRemove(new KeyValuePair<string, DatabaseContext>(alreadyClaimed, owner));
                }

                throw new InvalidOperationException(
                    "EnforceUniqueConnectionString is enabled and another live DatabaseContext in " +
                    "this process is already using this connection string. DatabaseContext is meant " +
                    "to be a singleton per connection string — dispose the existing context first, " +
                    "share a single DatabaseContext instance, or use a distinct connection string.");
            }

            claimed.Add(key);
        }

        return claimed;
    }

    /// <summary>
    /// Releases every key previously claimed by <paramref name="owner"/>. Safe to call with an
    /// empty or null list, and safe to call even if nothing was ever claimed.
    /// </summary>
    internal static void ReleaseAll(DatabaseContext owner, IReadOnlyList<string>? keys)
    {
        if (keys == null)
        {
            return;
        }

        foreach (var key in keys)
        {
            Claims.TryRemove(new KeyValuePair<string, DatabaseContext>(key, owner));
        }
    }

    /// <summary>
    /// Records <paramref name="owner"/> as the last-known user of every key in <paramref name="keys"/>,
    /// logging a warning via <paramref name="logger"/> for any key already recorded under a
    /// different, still-registered owner. Never throws and never blocks construction — this is a
    /// diagnostic aid for the default (non-enforcing) path, independent of <see cref="Claims"/>.
    /// </summary>
    internal static IReadOnlyList<string> RegisterAllForWarning(DatabaseContext owner, IReadOnlyList<string> keys,
        ILogger? logger)
    {
        foreach (var key in keys)
        {
            if (Registrations.TryGetValue(key, out var existingOwner) && !ReferenceEquals(existingOwner, owner))
            {
                try
                {
                    logger?.LogWarning(
                        "Another live DatabaseContext in this process is already using this connection string. " +
                        "DatabaseContext is meant to be a singleton per connection string — dispose the existing " +
                        "context first, share a single DatabaseContext instance, or use a distinct connection " +
                        "string. Set EnforceUniqueConnectionString to true to turn this into a hard failure.");
                }
                catch
                {
                    // This method is purely a best-effort diagnostic aid (see class remarks) — a
                    // broken logging sink must not prevent registration from completing for the
                    // remaining keys, nor propagate out of DatabaseContext construction. Without
                    // this guard, a throw here would leave this key registered (Registrations[key]
                    // below never runs) while also aborting before any later key in the same call
                    // gets registered — a genuine registry-state leak, not just a lost log line.
                }
            }

            Registrations[key] = owner;
        }

        return keys;
    }

    /// <summary>
    /// Removes <paramref name="owner"/>'s registration for every key in <paramref name="keys"/>,
    /// but only where <paramref name="owner"/> is still the recorded owner — a key already
    /// overwritten by a newer registrant is left untouched. Safe to call with an empty or null
    /// list, and safe to call even if nothing was ever registered.
    /// </summary>
    internal static void UnregisterAllForWarning(DatabaseContext owner, IReadOnlyList<string>? keys)
    {
        if (keys == null)
        {
            return;
        }

        foreach (var key in keys)
        {
            Registrations.TryRemove(new KeyValuePair<string, DatabaseContext>(key, owner));
        }
    }
}
