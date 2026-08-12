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
// - Opt-in only: contexts that don't enable EnforceUniqueConnectionString never
//   call this type at all, so default behavior (and every existing test) is
//   completely unaffected.
// =============================================================================

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace pengdows.crud.@internal;

internal static class UniqueConnectionStringRegistry
{
    private static readonly ConcurrentDictionary<string, DatabaseContext> Claims = new();

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
}
