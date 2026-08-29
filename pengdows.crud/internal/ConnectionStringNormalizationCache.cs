using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace pengdows.crud.@internal;

// CORE-012: this cache previously keyed a static, unbounded ConcurrentDictionary on the raw
// connection string. Most providers embed credentials directly in the connection string
// (Password=..., Pwd=..., User Id=...), so the process-lifetime key set retained every
// distinct connection string — including rotated-out or per-tenant credentials — verbatim,
// even though the cached *value* already scrubs those same keys before storage. An
// application constructing many distinct connection strings at runtime (per-tenant
// credentials, credential rotation) also grew the cache without bound.
//
// Fixed by keying on a SHA-256 digest of the raw connection string instead of the string
// itself (the cached value never depends on which credential value was used, so digest
// collisions between different credentials on an otherwise-identical connection string are
// harmless — at worst a rare, safe cache-sharing opportunity), and bounding the cache via the
// same BoundedCache<TKey,TValue> LRU pattern used elsewhere in this codebase.
internal static class ConnectionStringNormalizationCache
{
    // Distinct connection strings per process are typically small in number (one per tenant/
    // provider combination); 256 comfortably covers realistic multi-tenant fleets while still
    // bounding worst-case growth from pathological per-request connection-string construction.
    private const int MaxEntries = 256;

    private static readonly BoundedCache<string, Dictionary<string, string>> Cache = new(MaxEntries);

    internal static bool TryGet(string connectionString, out Dictionary<string, string>? normalized)
    {
        return Cache.TryGet(HashKey(connectionString), out normalized);
    }

    internal static bool TryAdd(string connectionString, Dictionary<string, string> normalized)
    {
        var wasAdded = false;
        Cache.GetOrAdd(HashKey(connectionString), _ =>
        {
            wasAdded = true;
            return normalized;
        });

        return wasAdded;
    }

    internal static void ClearForTests()
    {
        Cache.Clear();
    }

    internal static int Count => Cache.Count;

    private static string HashKey(string connectionString)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(connectionString));
        return Convert.ToHexString(bytes);
    }
}